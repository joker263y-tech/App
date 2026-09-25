namespace Shadowbound.Core.Stats
{
    /// <summary>
    /// Every numerical property that can describe a combatant. Values are held
    /// in arrays indexed by this enum, so iteration order is a performance and
    /// determinism concern: append new members at the end, never reorder.
    /// </summary>
    public enum StatId
    {
        MaxHealth = 0,
        MaxStamina = 1,
        AttackPower = 2,
        ShadowPower = 3,
        Armor = 4,
        MoveSpeed = 5,
        CritChance = 6,
        CritMultiplier = 7,
        HealthRegen = 8,
        StaminaRegen = 9,
        CooldownRate = 10,
        StatusResistance = 11
    }

    /// <summary>
    /// Metadata for <see cref="StatId"/>. Kept separate from the enum so the
    /// count and iteration order are explicit and testable rather than relying
    /// on reflection or enum arithmetic.
    /// </summary>
    public static class StatIds
    {
        public const int Count = 12;

        public static readonly StatId[] All =
        {
            StatId.MaxHealth,
            StatId.MaxStamina,
            StatId.AttackPower,
            StatId.ShadowPower,
            StatId.Armor,
            StatId.MoveSpeed,
            StatId.CritChance,
            StatId.CritMultiplier,
            StatId.HealthRegen,
            StatId.StaminaRegen,
            StatId.CooldownRate,
            StatId.StatusResistance
        };

        /// <summary>Human-readable label, used by tooling and the HUD.</summary>
        public static string Name(StatId id)
        {
            switch (id)
            {
                case StatId.MaxHealth: return "Vitality";
                case StatId.MaxStamina: return "Endurance";
                case StatId.AttackPower: return "Attack";
                case StatId.ShadowPower: return "Umbra";
                case StatId.Armor: return "Ward";
                case StatId.MoveSpeed: return "Speed";
                case StatId.CritChance: return "Precision";
                case StatId.CritMultiplier: return "Severity";
                case StatId.HealthRegen: return "Mending";
                case StatId.StaminaRegen: return "Recovery";
                case StatId.CooldownRate: return "Haste";
                case StatId.StatusResistance: return "Resolve";
                default: return id.ToString();
            }
        }

        /// <summary>
        /// Stats that are stored as a 0..1 fraction rather than a raw number.
        /// Tooling uses this to display them as percentages.
        /// </summary>
        public static bool IsFraction(StatId id)
        {
            return id == StatId.CritChance || id == StatId.StatusResistance;
        }
    }
}
