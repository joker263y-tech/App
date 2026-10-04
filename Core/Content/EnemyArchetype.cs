using System;
using System.Collections.Generic;
using Shadowbound.Core.Ai;
using Shadowbound.Core.Combat;
using Shadowbound.Core.Numerics;
using Shadowbound.Core.Stats;

namespace Shadowbound.Core.Content
{
    /// <summary>
    /// A reusable enemy template: the stats, abilities and behaviour every
    /// instance of a creature shares.
    ///
    /// Distinct from a <see cref="Combatant"/>, which is one living instance.
    /// Three Hollow Walkers are three combatants built from one archetype, each
    /// with its own health, position and status effects.
    /// </summary>
    public sealed class EnemyArchetype
    {
        public string Id = "";
        public string DisplayName = "";

        /// <summary>Flavour text shown on the first encounter. Original to this game.</summary>
        public string Description = "";

        public int Level = 1;

        public float MaxHealth = 100f;
        public float AttackPower = 10f;
        public float ShadowPower;
        public float Armor;
        public float MoveSpeed = 4f;
        public float CritChance;
        public float CritMultiplier = 1.5f;
        public float StatusResistance;
        public float HealthRegen;

        /// <summary>Resistance per damage school. Defaults to none.</summary>
        public ResistanceSet Resistances = new ResistanceSet();

        public int ExperienceReward = 10;
        public string LootTableId = "";

        public bool IsBoss;

        /// <summary>Index into <see cref="Abilities"/> that the AI attacks with.</summary>
        public int AttackAbilityIndex;

        public List<AbilityDefinition> Abilities = new List<AbilityDefinition>();

        public EnemyBrainSettings Brain = new EnemyBrainSettings();

        /// <summary>Visually, how large this creature is. The placeholder art uses it for scale.</summary>
        public float BodyScale = 1f;

        /// <summary>Colour used by the procedural content generator for placeholder materials.</summary>
        public float[] TintRgb = { 0.5f, 0.5f, 0.5f };

        /// <summary>Builds one living instance at a position.</summary>
        public Combatant Create(string instanceId, Float3 position, int levelOverride = 0)
        {
            if (string.IsNullOrEmpty(instanceId))
            {
                throw new ArgumentException("An enemy instance needs an id.", nameof(instanceId));
            }

            int level = levelOverride > 0 ? levelOverride : Level;

            var combatant = new Combatant(instanceId, Faction.Hostile, level)
            {
                DisplayName = DisplayName,
                ArchetypeId = Id,
                ExperienceReward = ExperienceReward,
                LootTableId = LootTableId,
                IsBoss = IsBoss
            };

            ApplyStats(combatant, level);

            // Values are copied into the combatant's existing profile rather than
            // the profile being replaced, because the health pool shares that
            // instance and a replacement would be ignored by damage resolution.
            combatant.CopyResistancesFrom(Resistances);

            combatant.Vitals.ResetToFull();
            combatant.SetPosition(position);
            combatant.FaceImmediately(new Float3(0f, 0f, 1f));

            return combatant;
        }

        /// <summary>
        /// Writes base stats, scaling health and power with level for enemies placed
        /// above or below their archetype's baseline. Without this a late-game zone
        /// would reuse the same numbers as the opening area.
        /// </summary>
        public void ApplyStats(Combatant combatant, int level)
        {
            float levelDelta = level - Level;
            float healthScale = 1f + (0.18f * levelDelta);
            float powerScale = 1f + (0.12f * levelDelta);

            if (healthScale < 0.2f)
            {
                healthScale = 0.2f;
            }

            if (powerScale < 0.2f)
            {
                powerScale = 0.2f;
            }

            StatSet stats = combatant.Stats;
            stats.SetBase(StatId.MaxHealth, MaxHealth * healthScale);
            stats.SetBase(StatId.MaxStamina, 100f);
            stats.SetBase(StatId.AttackPower, AttackPower * powerScale);
            stats.SetBase(StatId.ShadowPower, ShadowPower * powerScale);
            stats.SetBase(StatId.Armor, Armor);
            stats.SetBase(StatId.MoveSpeed, MoveSpeed);
            stats.SetBase(StatId.CritChance, CritChance);
            stats.SetBase(StatId.CritMultiplier, CritMultiplier);
            stats.SetBase(StatId.CooldownRate, 1f);
            stats.SetBase(StatId.HealthRegen, HealthRegen);
            stats.SetBase(StatId.StaminaRegen, 10f);
            stats.SetBase(StatId.StatusResistance, StatusResistance);
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(DisplayName) ? Id : DisplayName;
        }
    }
}
