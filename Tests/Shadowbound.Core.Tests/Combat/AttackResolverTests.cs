using System.Collections.Generic;
using Shadowbound.Core.Combat;
using Shadowbound.Core.Numerics;
using Shadowbound.Core.Randomness;
using Shadowbound.Core.Tests.Support;
using Xunit;

namespace Shadowbound.Core.Tests.Combat
{
    public class AttackResolverTests
    {
        private static readonly Float3 Forward = new Float3(0f, 0f, 1f);

        private static Combatant Attacker(float attackPower = 100f, float shadowPower = 0f, float critChance = 0f)
        {
            Combatant attacker = CombatantFactory.Create(
                "hero",
                Faction.Player,
                maxHealth: 500f,
                attackPower: attackPower,
                shadowPower: shadowPower,
                critChance: critChance);

            attacker.FaceImmediately(Forward);
            return attacker;
        }

        private static List<Combatant> Enemies(params Float3[] positions)
        {
            var list = new List<Combatant>();
            for (int i = 0; i < positions.Length; i++)
            {
                list.Add(CombatantFactory.Create(
                    "enemy" + i,
                    Faction.Hostile,
                    maxHealth: 500f,
                    position: positions[i]));
            }

            return list;
        }

        private static AbilityDefinition Ability(
            float range = 2.5f,
            float coneHalfAngle = 60f,
            int maxTargets = 1,
            float damageMultiplier = 1f,
            bool usesShadow = false)
        {
            return new AbilityDefinition
            {
                Id = "test",
                Kind = AbilityKind.Melee,
                DamageType = DamageType.Physical,
                UsesShadowPower = usesShadow,
                Range = range,
                ConeHalfAngleDegrees = coneHalfAngle,
                MaxTargets = maxTargets,
                DamageMultiplier = damageMultiplier,
                Variance = 0f
            };
        }

        // ---------------------------- target selection ----------------------------

        [Fact]
        public void Resolve_HitsTheNearestTargetWhenLimitedToOne()
        {
            Combatant attacker = Attacker();
            List<Combatant> candidates = Enemies(new Float3(0f, 0f, 2f), new Float3(0f, 0f, 1f));
            var hits = new List<Combatant>();

            int count = AttackResolver.Resolve(attacker, Ability(), candidates, new DeterministicRng(1), hits);

            Assert.Equal(1, count);
            Assert.Same(candidates[1], hits[0]);
        }

        [Fact]
        public void Resolve_IgnoresTargetsBeyondRange()
        {
            Combatant attacker = Attacker();
            List<Combatant> candidates = Enemies(new Float3(0f, 0f, 5f));
            var hits = new List<Combatant>();

            int count = AttackResolver.Resolve(attacker, Ability(range: 2.5f), candidates, new DeterministicRng(1), hits);

            Assert.Equal(0, count);
            Assert.Empty(hits);
        }

        [Fact]
        public void Resolve_IgnoresTargetsBehindTheAttacker()
        {
            Combatant attacker = Attacker();
            List<Combatant> candidates = Enemies(new Float3(0f, 0f, -2f));
            var hits = new List<Combatant>();

            int count = AttackResolver.Resolve(attacker, Ability(coneHalfAngle: 60f), candidates, new DeterministicRng(1), hits);

            Assert.Equal(0, count);
        }

        [Fact]
        public void Resolve_IgnoresTargetsOutsideTheCone()
        {
            Combatant attacker = Attacker();
            // Directly to the right, but the cone only covers 60 degrees ahead.
            List<Combatant> candidates = Enemies(new Float3(2f, 0f, 0f));
            var hits = new List<Combatant>();

            int count = AttackResolver.Resolve(attacker, Ability(coneHalfAngle: 30f), candidates, new DeterministicRng(1), hits);

            Assert.Equal(0, count);
        }

        [Fact]
        public void Resolve_WithFullCone_HitsBehindTheAttacker()
        {
            Combatant attacker = Attacker();
            List<Combatant> candidates = Enemies(new Float3(0f, 0f, -2f));
            var hits = new List<Combatant>();

            int count = AttackResolver.Resolve(attacker, Ability(coneHalfAngle: 180f), candidates, new DeterministicRng(1), hits);

            Assert.Equal(1, count);
        }

        [Fact]
        public void Resolve_IgnoresSameFactionCombatants()
        {
            Combatant attacker = Attacker();
            var ally = CombatantFactory.Create("ally", Faction.Player, position: new Float3(0f, 0f, 1f));
            var candidates = new List<Combatant> { ally };
            var hits = new List<Combatant>();

            int count = AttackResolver.Resolve(attacker, Ability(), candidates, new DeterministicRng(1), hits);

            Assert.Equal(0, count);
        }

        [Fact]
        public void Resolve_IgnoresDeadTargets()
        {
            Combatant attacker = Attacker();
            List<Combatant> candidates = Enemies(new Float3(0f, 0f, 1f));
            candidates[0].Vitals.ApplyDamage(1000f, null);
            var hits = new List<Combatant>();

            int count = AttackResolver.Resolve(attacker, Ability(), candidates, new DeterministicRng(1), hits);

            Assert.Equal(0, count);
        }

        [Fact]
        public void Resolve_WithACleave_HitsUpToItsTargetLimit()
        {
            Combatant attacker = Attacker();
            List<Combatant> candidates = Enemies(
                new Float3(0f, 0f, 1f),
                new Float3(0.5f, 0f, 2f),
                new Float3(-0.5f, 0f, 2f));
            var hits = new List<Combatant>();

            int count = AttackResolver.Resolve(
                attacker,
                Ability(range: 3f, coneHalfAngle: 180f, maxTargets: 2),
                candidates,
                new DeterministicRng(1),
                hits);

            Assert.Equal(2, count);
            Assert.Same(candidates[0], hits[0]);
        }

        [Fact]
        public void Resolve_WithUnlimitedTargets_HitsEverythingInRange()
        {
            Combatant attacker = Attacker();
            List<Combatant> candidates = Enemies(
                new Float3(0f, 0f, 1f),
                new Float3(0f, 0f, 2f),
                new Float3(0f, 0f, 3f));
            var hits = new List<Combatant>();

            int count = AttackResolver.Resolve(
                attacker,
                Ability(range: 4f, coneHalfAngle: 180f, maxTargets: 0),
                candidates,
                new DeterministicRng(1),
                hits);

            Assert.Equal(3, count);
        }

        // --------------------------------- damage ---------------------------------

        [Fact]
        public void Resolve_AppliesDamageScaledByAttackPower()
        {
            Combatant attacker = Attacker(attackPower: 100f);
            List<Combatant> candidates = Enemies(new Float3(0f, 0f, 1f));

            AttackResolver.Resolve(attacker, Ability(damageMultiplier: 1f), candidates, new DeterministicRng(1), new List<Combatant>());

            Assert.Equal(400f, candidates[0].Vitals.Health, 2);
        }

        [Fact]
        public void Resolve_ScalesDamageByTheAbilityMultiplier()
        {
            Combatant attacker = Attacker(attackPower: 100f);
            List<Combatant> candidates = Enemies(new Float3(0f, 0f, 1f));

            AttackResolver.Resolve(attacker, Ability(damageMultiplier: 2.5f), candidates, new DeterministicRng(1), new List<Combatant>());

            Assert.Equal(250f, candidates[0].Vitals.Health, 2);
        }

        [Fact]
        public void Resolve_ShadowAbility_ScalesOffShadowPowerNotAttack()
        {
            Combatant attacker = Attacker(attackPower: 10f, shadowPower: 200f);
            List<Combatant> candidates = Enemies(new Float3(0f, 0f, 1f));

            AttackResolver.Resolve(
                attacker,
                Ability(usesShadow: true),
                candidates,
                new DeterministicRng(1),
                new List<Combatant>());

            Assert.Equal(300f, candidates[0].Vitals.Health, 2);
        }

        [Fact]
        public void Resolve_ArmorReducesDamage()
        {
            Combatant attacker = Attacker(attackPower: 100f);
            var target = CombatantFactory.Create("tank", Faction.Hostile, maxHealth: 500f, armor: 100f, position: new Float3(0f, 0f, 1f));
            var candidates = new List<Combatant> { target };

            AttackResolver.Resolve(attacker, Ability(), candidates, new DeterministicRng(1), new List<Combatant>());

            float mitigation = DamageCalculator.MitigationFraction(100f, 0f, 1);
            Assert.Equal(500f - (100f * (1f - mitigation)), target.Vitals.Health, 2);
        }

        [Fact]
        public void Resolve_EmpoweredAttackerDealsMoreDamage()
        {
            Combatant attacker = Attacker(attackPower: 100f);
            attacker.Statuses.Apply(StatusEffect.Modifier(StatusKind.Empowered, 0.5f, 10f, null));
            List<Combatant> candidates = Enemies(new Float3(0f, 0f, 1f));

            AttackResolver.Resolve(attacker, Ability(), candidates, new DeterministicRng(1), new List<Combatant>());

            Assert.Equal(350f, candidates[0].Vitals.Health, 2);
        }

        [Fact]
        public void Resolve_WardedTargetTakesLessDamage()
        {
            Combatant attacker = Attacker(attackPower: 100f);
            List<Combatant> candidates = Enemies(new Float3(0f, 0f, 1f));
            candidates[0].Statuses.Apply(StatusEffect.Modifier(StatusKind.Warded, 0.25f, 10f, null));

            AttackResolver.Resolve(attacker, Ability(), candidates, new DeterministicRng(1), new List<Combatant>());

            Assert.Equal(500f - 75f, candidates[0].Vitals.Health, 2);
        }

        [Fact]
        public void Resolve_CanKillATarget()
        {
            Combatant attacker = Attacker(attackPower: 1000f);
            List<Combatant> candidates = Enemies(new Float3(0f, 0f, 1f));

            AttackResolver.Resolve(attacker, Ability(), candidates, new DeterministicRng(1), new List<Combatant>());

            Assert.False(candidates[0].IsAlive);
        }

        [Fact]
        public void Resolve_CritChanceBonusStacksWithTheStat()
        {
            Combatant attacker = Attacker(attackPower: 100f, critChance: 0.5f);
            AbilityDefinition ability = Ability();
            ability.CritChanceBonus = 0.5f;

            // Guaranteed crit, so the doubled crit multiplier always applies.
            var rng = new DeterministicRng(1);
            List<Combatant> candidates = Enemies(new Float3(0f, 0f, 1f));

            AttackResolver.Resolve(attacker, ability, candidates, rng, new List<Combatant>());

            Assert.Equal(500f - 150f, candidates[0].Vitals.Health, 2);
        }

        // --------------------------------- effects --------------------------------

        [Fact]
        public void Resolve_AppliesOnHitStatuses()
        {
            Combatant attacker = Attacker();
            List<Combatant> candidates = Enemies(new Float3(0f, 0f, 1f));

            AbilityDefinition ability = Ability();
            ability.OnHitStatuses = new[]
            {
                new StatusApplication(StatusKind.Bleeding, 7f, 6f, 1f, 3)
            };

            AttackResolver.Resolve(attacker, ability, candidates, new DeterministicRng(1), new List<Combatant>());

            Assert.True(candidates[0].Statuses.Has(StatusKind.Bleeding));
            Assert.Equal(7f, candidates[0].Statuses.Magnitude(StatusKind.Bleeding), 3);
        }

        [Fact]
        public void Resolve_BurningAlwaysUsesEmberDamage()
        {
            // Burning must not inherit a physical ability's school, or fire
            // resistance would be meaningless.
            Combatant attacker = Attacker();
            List<Combatant> candidates = Enemies(new Float3(0f, 0f, 1f));

            AbilityDefinition ability = Ability();
            ability.DamageType = DamageType.Physical;
            ability.OnHitStatuses = new[]
            {
                new StatusApplication(StatusKind.Burning, 5f, 4f, 1f, 1)
            };

            AttackResolver.Resolve(attacker, ability, candidates, new DeterministicRng(1), new List<Combatant>());

            Assert.True(candidates[0].Statuses.TryGet(StatusKind.Burning, out StatusEffect burning));
            Assert.Equal(DamageType.Ember, burning.DamageType);
        }

        [Fact]
        public void Resolve_StatusWithZeroChanceNeverApplies()
        {
            Combatant attacker = Attacker();
            List<Combatant> candidates = Enemies(new Float3(0f, 0f, 1f));

            var ability = Ability();
            ability.OnHitStatuses = new[] { new StatusApplication(StatusKind.Chilled, 0f, 5f, 0f, 1, 1f) };

            AttackResolver.Resolve(attacker, ability, candidates, new DeterministicRng(1), new List<Combatant>());

            Assert.False(candidates[0].Statuses.Has(StatusKind.Chilled));
        }

        [Fact]
        public void Resolve_AppliesKnockbackAwayFromTheAttacker()
        {
            Combatant attacker = Attacker();
            List<Combatant> candidates = Enemies(new Float3(0f, 0f, 1f));

            AbilityDefinition ability = Ability();
            ability.KnockbackSpeed = 12f;

            AttackResolver.Resolve(attacker, ability, candidates, new DeterministicRng(1), new List<Combatant>());

            Assert.Equal(12f, candidates[0].KnockbackSpeed, 3);
            Assert.Equal(new Float3(0f, 0f, 1f), candidates[0].KnockbackDirection);
        }

        [Fact]
        public void Resolve_SelfStaggerIsPaidEvenOnAWhiff()
        {
            // Otherwise whiffing a heavy attack would be completely free.
            Combatant attacker = Attacker();
            var candidates = new List<Combatant>();

            AbilityDefinition ability = Ability();
            ability.SelfStaggerSeconds = 1.2f;

            AttackResolver.Resolve(attacker, ability, candidates, new DeterministicRng(1), new List<Combatant>());

            Assert.True(attacker.Statuses.IsStunned);
        }

        [Fact]
        public void Resolve_SelfStaggerAppliesOncePerSwingNotPerTarget()
        {
            Combatant attacker = Attacker();
            List<Combatant> candidates = Enemies(
                new Float3(0f, 0f, 1f),
                new Float3(0f, 0f, 2f),
                new Float3(0f, 0f, 3f));

            AbilityDefinition ability = Ability(range: 4f, coneHalfAngle: 180f, maxTargets: 0);
            ability.SelfStaggerSeconds = 2f;

            AttackResolver.Resolve(attacker, ability, candidates, new DeterministicRng(1), new List<Combatant>());

            Assert.True(attacker.Statuses.TryGet(StatusKind.Staggered, out StatusEffect stagger));
            Assert.Equal(1, stagger.Stacks);
            Assert.Equal(2f, stagger.Remaining, 3);
        }

        // ---------------------------------- dash ----------------------------------

        [Fact]
        public void Resolve_DashMovesTheAttackerForward()
        {
            Combatant attacker = Attacker();
            var ability = Ability();
            ability.Kind = AbilityKind.Dash;
            ability.DashDistance = 6f;

            int count = AttackResolver.Resolve(attacker, ability, new List<Combatant>(), new DeterministicRng(1), new List<Combatant>());

            Assert.Equal(0, count);
            Assert.Equal(new Float3(0f, 0f, 6f), attacker.Position);
        }

        [Fact]
        public void Resolve_DashIgnoresEnemiesInTheWay()
        {
            Combatant attacker = Attacker();
            List<Combatant> candidates = Enemies(new Float3(0f, 0f, 1f));

            var ability = Ability();
            ability.Kind = AbilityKind.Dash;
            ability.DashDistance = 6f;

            AttackResolver.Resolve(attacker, ability, candidates, new DeterministicRng(1), new List<Combatant>());

            Assert.Equal(500f, candidates[0].Vitals.Health, 2);
        }

        // -------------------------------- self-cast --------------------------------

        [Fact]
        public void Resolve_SelfAbilityAppliesStatusesToTheCaster()
        {
            Combatant attacker = Attacker();
            var ability = Ability();
            ability.Kind = AbilityKind.Self;
            ability.OnHitStatuses = new[]
            {
                new StatusApplication(StatusKind.Warded, 0.4f, 8f, 0f, 1)
            };

            AttackResolver.Resolve(attacker, ability, new List<Combatant>(), new DeterministicRng(1), new List<Combatant>());

            Assert.True(attacker.Statuses.Has(StatusKind.Warded));
            Assert.Equal(0.6f, attacker.Statuses.DamageTakenMultiplier, 3);
        }

        // --------------------------------- guards ---------------------------------

        [Fact]
        public void Resolve_WithDeadAttacker_DoesNothing()
        {
            Combatant attacker = Attacker();
            attacker.Vitals.ApplyDamage(1000f, null);
            List<Combatant> candidates = Enemies(new Float3(0f, 0f, 1f));

            int count = AttackResolver.Resolve(attacker, Ability(), candidates, new DeterministicRng(1), new List<Combatant>());

            Assert.Equal(0, count);
            Assert.Equal(500f, candidates[0].Vitals.Health, 2);
        }

        [Fact]
        public void Resolve_WithNullAbility_DoesNotThrow()
        {
            Combatant attacker = Attacker();
            int count = AttackResolver.Resolve(attacker, null, new List<Combatant>(), new DeterministicRng(1), new List<Combatant>());

            Assert.Equal(0, count);
        }

        [Fact]
        public void Resolve_ReusesTheProvidedHitBuffer()
        {
            Combatant attacker = Attacker();
            List<Combatant> candidates = Enemies(new Float3(0f, 0f, 1f));
            var hits = new List<Combatant>();

            AttackResolver.Resolve(attacker, Ability(), candidates, new DeterministicRng(1), hits);
            Assert.Single(hits);

            // A second call with a different attacker must not accumulate.
            Combatant other = Attacker();
            candidates[0].Vitals.ResetToFull();
            AttackResolver.Resolve(other, Ability(), candidates, new DeterministicRng(1), hits);

            Assert.Single(hits);
        }
    }
}
