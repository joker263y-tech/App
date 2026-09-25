using Shadowbound.Core.Combat;
using Shadowbound.Core.Numerics;
using Shadowbound.Core.Stats;

namespace Shadowbound.Core.Tests.Support
{
    /// <summary>
    /// Builds fully configured combatants for tests.
    ///
    /// Every stat that a system reads is set explicitly rather than left at its
    /// zero default, so a test failure means the system behaved unexpectedly
    /// rather than that a stat was simply never initialised.
    /// </summary>
    internal static class CombatantFactory
    {
        public static Combatant Create(
            string id = "combatant",
            Faction faction = Faction.Hostile,
            float maxHealth = 100f,
            float maxStamina = 100f,
            float attackPower = 10f,
            float shadowPower = 0f,
            float armor = 0f,
            float moveSpeed = 5f,
            float critChance = 0f,
            float critMultiplier = 1.5f,
            float cooldownRate = 1f,
            float healthRegen = 0f,
            float staminaRegen = 0f,
            float statusResistance = 0f,
            int level = 1,
            Float3 position = default(Float3))
        {
            var combatant = new Combatant(id, faction, level);
            StatSet stats = combatant.Stats;

            stats.SetBase(StatId.MaxHealth, maxHealth);
            stats.SetBase(StatId.MaxStamina, maxStamina);
            stats.SetBase(StatId.AttackPower, attackPower);
            stats.SetBase(StatId.ShadowPower, shadowPower);
            stats.SetBase(StatId.Armor, armor);
            stats.SetBase(StatId.MoveSpeed, moveSpeed);
            stats.SetBase(StatId.CritChance, critChance);
            stats.SetBase(StatId.CritMultiplier, critMultiplier);
            stats.SetBase(StatId.CooldownRate, cooldownRate);
            stats.SetBase(StatId.HealthRegen, healthRegen);
            stats.SetBase(StatId.StaminaRegen, staminaRegen);
            stats.SetBase(StatId.StatusResistance, statusResistance);

            combatant.Vitals.ResetToFull();
            combatant.SetPosition(position);
            return combatant;
        }

        /// <summary>A generic melee swing used across ability tests.</summary>
        public static AbilityDefinition Strike(
            float windup = 0.25f,
            float recovery = 0.3f,
            float cooldown = 1f,
            float staminaCost = 10f,
            float damageMultiplier = 1f,
            float range = 2.5f,
            float coneHalfAngle = 60f)
        {
            return new AbilityDefinition
            {
                Id = "strike",
                DisplayName = "Strike",
                Kind = AbilityKind.Melee,
                DamageType = DamageType.Physical,
                UsesShadowPower = false,
                StaminaCost = staminaCost,
                CooldownSeconds = cooldown,
                WindupSeconds = windup,
                RecoverySeconds = recovery,
                Range = range,
                ConeHalfAngleDegrees = coneHalfAngle,
                DamageMultiplier = damageMultiplier,
                Variance = 0f,
                MaxTargets = 1,
                MoveSpeedDuringWindup = 0.25f
            };
        }
    }
}
