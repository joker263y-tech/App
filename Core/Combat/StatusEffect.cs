namespace Shadowbound.Core.Combat
{
    /// <summary>
    /// Status effects in the game. Members fall into two behavioural groups,
    /// described on <see cref="StatusEffectSystem"/>.
    /// </summary>
    public enum StatusKind
    {
        /// <summary>Damage over time. Ember. Sums across stacks.</summary>
        Burning = 0,

        /// <summary>Damage over time. Physical. Sums across stacks.</summary>
        Bleeding = 1,

        /// <summary>Movement and recovery slowed. Strongest instance applies.</summary>
        Chilled = 2,

        /// <summary>Cannot act. Strongest instance applies.</summary>
        Staggered = 3,

        /// <summary>Damage taken reduced. Strongest instance applies.</summary>
        Warded = 4,

        /// <summary>Damage dealt increased. Strongest instance applies.</summary>
        Empowered = 5,

        /// <summary>Damage taken increased, from being marked by the Umbra. Strongest instance applies.</summary>
        Marked = 6
    }

    /// <summary>What happens when a status is applied while one of its kind is already active.</summary>
    public enum StatusStackRule
    {
        /// <summary>Resets the duration of the existing effect and keeps the stronger magnitude.</summary>
        Refresh = 0,

        /// <summary>Adds another stack, up to <see cref="StatusEffect.MaxStacks"/>.</summary>
        Stack = 1,

        /// <summary>Ignored unless the incoming magnitude is stronger.</summary>
        Ignore = 2
    }

    /// <summary>
    /// One live status effect on one combatant. Mutable by design: the system
    /// ticks durations and accumulators in place to avoid per-frame allocation.
    /// </summary>
    public sealed class StatusEffect
    {
        public StatusKind Kind;

        /// <summary>School used when this effect deals damage over time.</summary>
        public DamageType DamageType;

        /// <summary>
        /// Meaning depends on <see cref="Kind"/>: damage per tick for damage
        /// over time, or a 0..1 fraction for the modifier kinds.
        /// </summary>
        public float Magnitude;

        /// <summary>Total duration in seconds, as originally applied.</summary>
        public float Duration;

        /// <summary>Seconds left before expiry.</summary>
        public float Remaining;

        /// <summary>Seconds between damage ticks. Zero means the effect never ticks damage.</summary>
        public float TickInterval;

        /// <summary>Accumulated time toward the next tick.</summary>
        public float TickAccumulator;

        /// <summary>Number of stacked instances, never below 1 while active.</summary>
        public int Stacks;

        public int MaxStacks;

        public StatusStackRule StackRule;

        /// <summary>The dealer that applied this effect, for damage attribution and kill credit.</summary>
        public object Source;

        public bool IsExpired
        {
            get { return Remaining <= 0f; }
        }

        /// <summary>True when this effect should deal damage on its tick.</summary>
        public bool DealsDamageOverTime
        {
            get { return TickInterval > 0f && Magnitude > 0f; }
        }

        public float FractionRemaining
        {
            get { return Duration <= 0f ? 0f : Remaining / Duration; }
        }

        /// <summary>Damage this effect applies on a single tick, across all stacks.</summary>
        public float DamagePerTick
        {
            get { return Magnitude * Stacks; }
        }

        public static StatusEffect Dot(StatusKind kind, DamageType type, float damagePerTick, float duration, float tickInterval, object source, int maxStacks)
        {
            return new StatusEffect
            {
                Kind = kind,
                DamageType = type,
                Magnitude = damagePerTick,
                Duration = duration,
                Remaining = duration,
                TickInterval = tickInterval,
                TickAccumulator = 0f,
                Stacks = 1,
                MaxStacks = maxStacks < 1 ? 1 : maxStacks,
                StackRule = maxStacks > 1 ? StatusStackRule.Stack : StatusStackRule.Refresh,
                Source = source
            };
        }

        public static StatusEffect Modifier(StatusKind kind, float magnitude, float duration, object source)
        {
            return new StatusEffect
            {
                Kind = kind,
                DamageType = DamageType.Physical,
                Magnitude = magnitude,
                Duration = duration,
                Remaining = duration,
                TickInterval = 0f,
                TickAccumulator = 0f,
                Stacks = 1,
                MaxStacks = 1,
                StackRule = StatusStackRule.Refresh,
                Source = source
            };
        }

        public override string ToString()
        {
            return Kind + " x" + Stacks + " " + Magnitude.ToString("0.##") + " (" + Remaining.ToString("0.#") + "s)";
        }
    }
}
