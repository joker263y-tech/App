using System;
using System.Collections.Generic;
using Shadowbound.Core.Numerics;
using Shadowbound.Core.Stats;

namespace Shadowbound.Core.Combat
{
    /// <summary>
    /// Holds and advances the status effects on one combatant.
    ///
    /// Two behavioural groups, chosen so that neither can run away:
    ///
    ///   Damage over time (Burning, Bleeding) - magnitudes SUM across stacks.
    ///   Five bleeds means five bleeds' worth of damage per tick. Stacks are
    ///   capped per effect.
    ///
    ///   Modifiers (Chilled, Warded, Empowered, Marked, Staggered) - the
    ///   STRONGEST instance applies, not the sum. Stacking slows additively
    ///   would let a group of enemies freeze the player permanently, so the
    ///   system takes the maximum instead and lets duration do the work.
    ///
    /// Incoming durations are scaled by the target's Resolve (StatusResistance),
    /// which is the stat that makes later-game enemies feel more resistant
    /// without them being flatly immune.
    /// </summary>
    public sealed class StatusEffectSystem
    {
        /// <summary>Longest a single application can last, after resistance. A safety ceiling.</summary>
        public const float MaxDuration = 30f;

        private readonly List<StatusEffect> _active;
        private readonly StatSet _stats;
        private readonly Vitals _vitals;

        public StatusEffectSystem(StatSet stats, Vitals vitals)
        {
            if (stats == null)
            {
                throw new ArgumentNullException(nameof(stats));
            }

            if (vitals == null)
            {
                throw new ArgumentNullException(nameof(vitals));
            }

            _stats = stats;
            _vitals = vitals;
            _active = new List<StatusEffect>(8);
        }

        /// <summary>Raised when an effect is newly applied or refreshed, for VFX.</summary>
        public event Action<StatusEffect> Applied;

        public event Action<StatusEffect> Expired;

        /// <summary>Raised when an effect deals damage. Arguments: effect, damage applied.</summary>
        public event Action<StatusEffect, float> Ticked;

        public IReadOnlyList<StatusEffect> Active
        {
            get { return _active; }
        }

        public int ActiveCount
        {
            get { return _active.Count; }
        }

        /// <summary>True while staggered, which blocks all actions including movement.</summary>
        public bool IsStunned
        {
            get { return Has(StatusKind.Staggered); }
        }

        /// <summary>Combined movement speed multiplier from slows, in 0..1.</summary>
        public float MoveSpeedMultiplier
        {
            get
            {
                float slow = StrongestMagnitude(StatusKind.Chilled);
                return FMath.Clamp01(1f - slow);
            }
        }

        /// <summary>Multiplier applied to cooldown recovery. Chilled makes abilities come back slower.</summary>
        public float CooldownRateMultiplier
        {
            get
            {
                float slow = StrongestMagnitude(StatusKind.Chilled);
                return FMath.Clamp01(1f - (slow * 0.5f));
            }
        }

        /// <summary>Multiplier applied to outgoing damage, from Empowered.</summary>
        public float DamageDealtMultiplier
        {
            get { return 1f + StrongestMagnitude(StatusKind.Empowered); }
        }

        /// <summary>
        /// Multiplier applied to incoming damage. Warded reduces it, Marked
        /// increases it, and both compose: a warded, marked target sits between
        /// the two rather than one cancelling the other arbitrarily.
        /// </summary>
        public float DamageTakenMultiplier
        {
            get
            {
                float ward = StrongestMagnitude(StatusKind.Warded);
                float mark = StrongestMagnitude(StatusKind.Marked);
                return (1f - FMath.Clamp01(ward)) * (1f + mark);
            }
        }

        /// <summary>
        /// Applies an effect, honouring its stacking rule and the target's
        /// Resolve. The incoming instance is not stored directly, so callers can
        /// safely reuse a template object.
        /// </summary>
        public void Apply(StatusEffect incoming)
        {
            if (incoming == null || !_vitals.IsAlive)
            {
                return;
            }

            float duration = ScaleDuration(incoming.Duration);
            if (duration <= 0f)
            {
                return;
            }

            StatusEffect existing = Find(incoming.Kind);
            if (existing == null)
            {
                var created = new StatusEffect
                {
                    Kind = incoming.Kind,
                    DamageType = incoming.DamageType,
                    Magnitude = incoming.Magnitude,
                    Duration = duration,
                    Remaining = duration,
                    TickInterval = incoming.TickInterval,
                    TickAccumulator = 0f,
                    Stacks = 1,
                    MaxStacks = incoming.MaxStacks < 1 ? 1 : incoming.MaxStacks,
                    StackRule = incoming.StackRule,
                    Source = incoming.Source
                };

                _active.Add(created);
                Applied?.Invoke(created);
                return;
            }

            switch (existing.StackRule)
            {
                case StatusStackRule.Stack:
                    if (existing.Stacks < existing.MaxStacks)
                    {
                        existing.Stacks++;
                    }

                    existing.Remaining = duration > existing.Remaining ? duration : existing.Remaining;
                    // A stronger source raising the weakest stack is the more
                    // useful reading of a mixed-strength re-application.
                    if (incoming.Magnitude > existing.Magnitude)
                    {
                        existing.Magnitude = incoming.Magnitude;
                        existing.Source = incoming.Source;
                    }

                    Applied?.Invoke(existing);
                    return;

                case StatusStackRule.Ignore:
                    if (incoming.Magnitude <= existing.Magnitude)
                    {
                        return;
                    }

                    existing.Magnitude = incoming.Magnitude;
                    existing.Remaining = duration;
                    existing.Source = incoming.Source;
                    Applied?.Invoke(existing);
                    return;

                default:
                    existing.Remaining = duration > existing.Remaining ? duration : existing.Remaining;
                    if (incoming.Magnitude > existing.Magnitude)
                    {
                        existing.Magnitude = incoming.Magnitude;
                        existing.Source = incoming.Source;
                    }

                    Applied?.Invoke(existing);
                    return;
            }
        }

        /// <summary>
        /// Advances all effects by <paramref name="deltaTime"/>, applying damage
        /// over time and removing expired effects. Effects that tick more than
        /// once in a long frame (a hitch) are caught up rather than losing ticks.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f || _active.Count == 0)
            {
                return;
            }

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                StatusEffect effect = _active[i];

                if (effect.DealsDamageOverTime && _vitals.IsAlive)
                {
                    effect.TickAccumulator += deltaTime;

                    // Guard against a huge delta producing an unbounded loop.
                    int guard = 0;
                    while (effect.TickAccumulator >= effect.TickInterval && guard < 32)
                    {
                        effect.TickAccumulator -= effect.TickInterval;
                        guard++;

                        float damage = effect.DamagePerTick;
                        float applied = _vitals.ApplyDamage(damage, effect.Source);
                        Ticked?.Invoke(effect, applied);

                        if (!_vitals.IsAlive)
                        {
                            break;
                        }
                    }
                }

                effect.Remaining -= deltaTime;

                if (effect.IsExpired)
                {
                    _active.RemoveAt(i);
                    Expired?.Invoke(effect);
                }
            }
        }

        public bool Has(StatusKind kind)
        {
            return Find(kind) != null;
        }

        public int StackCount(StatusKind kind)
        {
            StatusEffect effect = Find(kind);
            return effect == null ? 0 : effect.Stacks;
        }

        /// <summary>
        /// Effective magnitude of a kind. For damage over time this is the total
        /// per tick across stacks; for modifiers it is the strongest single
        /// instance. Returns 0 when the effect is absent.
        /// </summary>
        public float Magnitude(StatusKind kind)
        {
            if (IsDamageOverTime(kind))
            {
                float total = 0f;
                for (int i = 0; i < _active.Count; i++)
                {
                    if (_active[i].Kind == kind)
                    {
                        total += _active[i].DamagePerTick;
                    }
                }

                return total;
            }

            return StrongestMagnitude(kind);
        }

        public bool TryGet(StatusKind kind, out StatusEffect effect)
        {
            effect = Find(kind);
            return effect != null;
        }

        /// <summary>Removes every instance of a kind. Used by cleanse abilities.</summary>
        public int RemoveAll(StatusKind kind)
        {
            int removed = 0;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i].Kind == kind)
                {
                    StatusEffect effect = _active[i];
                    _active.RemoveAt(i);
                    Expired?.Invoke(effect);
                    removed++;
                }
            }

            return removed;
        }

        /// <summary>Removes all damage-over-time effects. The one cleanse the game needs.</summary>
        public int RemoveAllDamageOverTime()
        {
            int removed = 0;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (IsDamageOverTime(_active[i].Kind))
                {
                    StatusEffect effect = _active[i];
                    _active.RemoveAt(i);
                    Expired?.Invoke(effect);
                    removed++;
                }
            }

            return removed;
        }

        public void Clear()
        {
            if (_active.Count == 0)
            {
                return;
            }

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                StatusEffect effect = _active[i];
                _active.RemoveAt(i);
                Expired?.Invoke(effect);
            }
        }

        public static bool IsDamageOverTime(StatusKind kind)
        {
            return kind == StatusKind.Burning || kind == StatusKind.Bleeding;
        }

        private StatusEffect Find(StatusKind kind)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].Kind == kind)
                {
                    return _active[i];
                }
            }

            return null;
        }

        private float StrongestMagnitude(StatusKind kind)
        {
            float best = 0f;
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].Kind == kind && _active[i].Magnitude > best)
                {
                    best = _active[i].Magnitude;
                }
            }

            return best;
        }

        /// <summary>Applies Resolve and the global duration ceiling.</summary>
        private float ScaleDuration(float duration)
        {
            float resistance = FMath.Clamp(_stats.Get(StatId.StatusResistance), 0f, 0.9f);
            float scaled = duration * (1f - resistance);
            return FMath.Clamp(scaled, 0f, MaxDuration);
        }
    }
}
