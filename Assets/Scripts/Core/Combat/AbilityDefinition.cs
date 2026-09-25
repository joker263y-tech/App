using System;

namespace Shadowbound.Core.Combat
{
    /// <summary>Broad shape of an ability, which decides how targets are selected.</summary>
    public enum AbilityKind
    {
        /// <summary>Single-target strike in front of the attacker.</summary>
        Melee = 0,

        /// <summary>Wide swing hitting everything in a cone.</summary>
        Cleave = 1,

        /// <summary>A ranged projectile. Resolved instantly along the facing direction.</summary>
        Bolt = 2,

        /// <summary>A burst centred on the caster, hitting in all directions.</summary>
        Burst = 3,

        /// <summary>Movement ability. Deals no damage.</summary>
        Dash = 4,

        /// <summary>Applies effects to the caster only.</summary>
        Self = 5
    }

    /// <summary>A status effect an ability can inflict, with an independent chance to land.</summary>
    public readonly struct StatusApplication
    {
        public readonly StatusKind Kind;
        public readonly float Magnitude;
        public readonly float Duration;
        public readonly float TickInterval;
        public readonly int MaxStacks;
        public readonly float Chance;

        public StatusApplication(
            StatusKind kind,
            float magnitude,
            float duration,
            float tickInterval = 0f,
            int maxStacks = 1,
            float chance = 1f)
        {
            Kind = kind;
            Magnitude = magnitude;
            Duration = duration;
            TickInterval = tickInterval;
            MaxStacks = maxStacks < 1 ? 1 : maxStacks;
            Chance = magnitude <= 0f ? 0f : (chance < 0f ? 0f : (chance > 1f ? 1f : chance));
        }

        /// <summary>The damage school a damage-over-time application should use.</summary>
        public DamageType ResolveDamageType(DamageType fallback)
        {
            switch (Kind)
            {
                case StatusKind.Burning: return DamageType.Ember;
                case StatusKind.Bleeding: return fallback;
                default: return fallback;
            }
        }
    }

    /// <summary>
    /// A single action a combatant can perform. Authored content, not state:
    /// the same definition is shared by every combatant that knows the ability.
    /// Runtime state such as cooldown timers lives in <see cref="AbilityController"/>.
    /// </summary>
    public sealed class AbilityDefinition
    {
        public string Id = "unnamed";
        public string DisplayName = "";
        public AbilityKind Kind = AbilityKind.Melee;

        /// <summary>School of the direct damage.</summary>
        public DamageType DamageType = DamageType.Physical;

        /// <summary>Scale off Umbra instead of Attack. The player's shadow abilities do this.</summary>
        public bool UsesShadowPower;

        public float StaminaCost;
        public float CooldownSeconds = 1f;

        /// <summary>Seconds of telegraph before the blow lands. The counterplay window.</summary>
        public float WindupSeconds = 0.25f;

        /// <summary>Seconds of lockout after the blow. Prevents instant re-casting.</summary>
        public float RecoverySeconds = 0.3f;

        /// <summary>Reach in world units, measured on the horizontal plane.</summary>
        public float Range = 2.2f;

        /// <summary>Half-angle of the hit cone in degrees. 180 means all around.</summary>
        public float ConeHalfAngleDegrees = 60f;

        /// <summary>Multiplier applied to the attacker's power stat.</summary>
        public float DamageMultiplier = 1f;

        public float CritChanceBonus;
        public float Variance = 0.05f;

        /// <summary>Maximum targets hit. Zero means unlimited.</summary>
        public int MaxTargets = 1;

        public float KnockbackSpeed;

        /// <summary>Fraction of movement speed retained while winding up. 0 roots the attacker.</summary>
        public float MoveSpeedDuringWindup;

        /// <summary>Seconds the caster is staggered by their own ability. Heavy attacks cost commitment.</summary>
        public float SelfStaggerSeconds;

        /// <summary>Distance travelled by a <see cref="AbilityKind.Dash"/>, along the facing direction.</summary>
        public float DashDistance;

        public StatusApplication[] OnHitStatuses = Array.Empty<StatusApplication>();

        /// <summary>True when this ability can damage anything at all.</summary>
        public bool DealsDamage
        {
            get { return Kind != AbilityKind.Dash && Kind != AbilityKind.Self && DamageMultiplier > 0f; }
        }

        /// <summary>Total time the caster is committed to this ability.</summary>
        public float TotalDuration
        {
            get { return WindupSeconds + RecoverySeconds; }
        }

        /// <summary>Full cooldown including the cast itself.</summary>
        public float EffectiveCooldown
        {
            get { return CooldownSeconds + TotalDuration; }
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(DisplayName) ? Id : DisplayName;
        }
    }
}
