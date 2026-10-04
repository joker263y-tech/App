using System.Collections.Generic;
using Shadowbound.Core.Numerics;
using Shadowbound.Core.Randomness;
using Shadowbound.Core.Stats;

namespace Shadowbound.Core.Combat
{
    /// <summary>
    /// Turns a landed ability into actual hits: picks targets, rolls damage and
    /// applies effects.
    ///
    /// Separated from <see cref="AbilityController"/> so that "when does the blow
    /// land" and "what does the blow hit" can be tested independently. This class
    /// is stateless and does no allocation on the hot path: the caller supplies a
    /// reusable buffer for the hit list, and nearest-target selection is done by
    /// replacement rather than sorting.
    /// </summary>
    public static class AttackResolver
    {
        /// <summary>
        /// Selects targets for a landed ability and applies damage and effects.
        /// Fills <paramref name="hits"/> with the combatants actually struck, for
        /// presentation. Returns the number of targets hit.
        /// </summary>
        public static int Resolve(
            Combatant attacker,
            AbilityDefinition ability,
            IReadOnlyList<Combatant> candidates,
            DeterministicRng rng,
            List<Combatant> hits)
        {
            if (hits == null)
            {
                return 0;
            }

            hits.Clear();

            if (attacker == null || ability == null || !attacker.IsAlive)
            {
                return 0;
            }

            // A dash is pure movement. It still resolves so that the caller has a
            // single entry point, but it damages nothing.
            if (ability.Kind == AbilityKind.Dash)
            {
                MoveAttacker(attacker, ability);
                return 0;
            }

            // Self-stagger is the cost of committing to the ability, so it is
            // paid whether or not the blow connects. Applying it only on a hit
            // would make whiffing a heavy attack free, and would remove the
            // risk that gives heavy attacks their weight.
            //
            // It is also applied once per swing rather than once per target, so
            // a cleave that hits five enemies does not stagger its own caster
            // five times.
            if (ability.SelfStaggerSeconds > 0f)
            {
                ApplySelfStagger(attacker, ability.SelfStaggerSeconds);
            }

            if (ability.Kind == AbilityKind.Self)
            {
                ApplyStatusesToSelf(attacker, ability);
                return 0;
            }

            SelectTargets(attacker, ability, candidates, hits);

            if (hits.Count == 0)
            {
                return 0;
            }

            for (int i = 0; i < hits.Count; i++)
            {
                Strike(attacker, ability, hits[i], rng);
            }

            return hits.Count;
        }

        /// <summary>
        /// Fills <paramref name="hits"/> with the hostile combatants the ability
        /// reaches, nearest first, capped at the ability's target limit.
        ///
        /// Selection is by repeated replacement rather than by sorting: the
        /// candidate set is small, and this keeps the method allocation-free,
        /// which matters when a boss cleave runs against twenty enemies on a
        /// mobile CPU.
        /// </summary>
        public static void SelectTargets(
            Combatant attacker,
            AbilityDefinition ability,
            IReadOnlyList<Combatant> candidates,
            List<Combatant> hits)
        {
            hits.Clear();

            if (candidates == null)
            {
                return;
            }

            float range = ability.Range;
            int limit = ability.MaxTargets <= 0 ? int.MaxValue : ability.MaxTargets;
            Float3 forward = attacker.Forward;

            for (int i = 0; i < candidates.Count; i++)
            {
                Combatant candidate = candidates[i];
                if (candidate == null || !candidate.IsAlive || !attacker.IsHostileTo(candidate))
                {
                    continue;
                }

                float distance = Float3.DistanceXZ(attacker.Position, candidate.Position);
                if (distance > range)
                {
                    continue;
                }

                if (!FMath.WithinCone(attacker.Position, forward, candidate.Position, ability.ConeHalfAngleDegrees, range))
                {
                    continue;
                }

                InsertNearest(attacker, hits, candidate, limit);
            }
        }

        /// <summary>
        /// Maintains a nearest-first list of at most <paramref name="limit"/>
        /// entries. A new candidate replaces the current farthest entry, and only
        /// when it is actually nearer.
        /// </summary>
        private static void InsertNearest(Combatant attacker, List<Combatant> hits, Combatant candidate, int limit)
        {
            if (hits.Count < limit)
            {
                hits.Add(candidate);
                return;
            }

            int farthestIndex = -1;
            float farthestDistance = -1f;
            for (int i = 0; i < hits.Count; i++)
            {
                float distance = Float3.DistanceXZ(attacker.Position, hits[i].Position);
                if (distance > farthestDistance)
                {
                    farthestDistance = distance;
                    farthestIndex = i;
                }
            }

            float candidateDistance = Float3.DistanceXZ(attacker.Position, candidate.Position);
            if (farthestIndex >= 0 && candidateDistance < farthestDistance)
            {
                hits[farthestIndex] = candidate;
            }
        }

        /// <summary>Resolves and applies one hit against one target.</summary>
        public static DamageResult Strike(
            Combatant attacker,
            AbilityDefinition ability,
            Combatant target,
            DeterministicRng rng)
        {
            if (!ability.DealsDamage || target == null || !target.IsAlive)
            {
                return DamageResult.None;
            }

            StatId powerStat = ability.UsesShadowPower ? StatId.ShadowPower : StatId.AttackPower;
            float baseDamage = attacker.Stats.Get(powerStat) * ability.DamageMultiplier;

            var request = new DamageRequest(
                ability.DamageType,
                baseDamage,
                armorPenetration: 0f,
                critChance: attacker.Stats.Get(StatId.CritChance) + ability.CritChanceBonus,
                critMultiplier: attacker.Stats.Get(StatId.CritMultiplier),
                attackerLevel: attacker.Level,
                variance: ability.Variance);

            DamageResult result = DamageCalculator.Resolve(
                request,
                defenderArmor: target.Stats.Get(StatId.Armor),
                defenderResistance: target.Vitals.Resistance.Get(ability.DamageType),
                attackerDamageMultiplier: attacker.Statuses.DamageDealtMultiplier,
                defenderDamageTakenMultiplier: target.Statuses.DamageTakenMultiplier,
                rng: rng);

            if (result.IsZero)
            {
                return result;
            }

            if (ability.KnockbackSpeed > 0f)
            {
                Float3 push = target.Position - attacker.Position;
                target.ApplyKnockback(push, ability.KnockbackSpeed);
            }

            target.ReceiveDamage(result, attacker);
            ApplyStatuses(target, ability, rng, attacker);
            return result;
        }

        /// <summary>
        /// Rolls and applies an ability's on-hit statuses. Damage-over-time uses
        /// the school declared by the status kind, so burning always burns with
        /// Ember regardless of the ability's own damage type.
        /// </summary>
        public static void ApplyStatuses(
            Combatant target,
            AbilityDefinition ability,
            DeterministicRng rng,
            Combatant source)
        {
            StatusApplication[] applications = ability.OnHitStatuses;
            if (applications == null || applications.Length == 0)
            {
                return;
            }

            for (int i = 0; i < applications.Length; i++)
            {
                StatusApplication application = applications[i];
                if (application.Chance <= 0f)
                {
                    continue;
                }

                if (application.Chance < 1f && rng != null && !rng.Chance(application.Chance))
                {
                    continue;
                }

                target.Statuses.Apply(BuildStatus(application, ability.DamageType, source));
            }
        }

        /// <summary>Builds the runtime status effect for one application.</summary>
        public static StatusEffect BuildStatus(StatusApplication application, DamageType fallback, object source)
        {
            if (StatusEffectSystem.IsDamageOverTime(application.Kind))
            {
                return StatusEffect.Dot(
                    application.Kind,
                    application.ResolveDamageType(fallback),
                    application.Magnitude,
                    application.Duration,
                    application.TickInterval,
                    source,
                    application.MaxStacks);
            }

            return StatusEffect.Modifier(application.Kind, application.Magnitude, application.Duration, source);
        }

        private static void ApplyStatusesToSelf(Combatant attacker, AbilityDefinition ability)
        {
            StatusApplication[] applications = ability.OnHitStatuses;
            if (applications == null)
            {
                return;
            }

            for (int i = 0; i < applications.Length; i++)
            {
                attacker.Statuses.Apply(BuildStatus(applications[i], ability.DamageType, attacker));
            }
        }

        private static void ApplySelfStagger(Combatant attacker, float seconds)
        {
            attacker.Statuses.Apply(StatusEffect.Modifier(StatusKind.Staggered, 1f, seconds, null));
        }

        /// <summary>
        /// Advances a dashing attacker along its facing direction.
        ///
        /// Bounds are intentionally not clamped here: the core has no notion of
        /// a world yet, and clamping is the responsibility of whatever owns the
        /// space (the simulation, or the Unity layer against real colliders).
        /// A dash that would leave the arena is stopped there, not here.
        /// </summary>
        private static void MoveAttacker(Combatant attacker, AbilityDefinition ability)
        {
            if (ability.DashDistance <= 0f)
            {
                return;
            }

            Float3 destination = attacker.Position + (attacker.Forward * ability.DashDistance);
            attacker.SetPosition(destination);
        }
    }
}
