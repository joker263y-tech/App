using System;
using System.Collections.Generic;
using Shadowbound.Core.Ai;
using Shadowbound.Core.Combat;
using Shadowbound.Core.Items;
using Shadowbound.Core.Progression;
using Shadowbound.Core.Quests;
using Shadowbound.Core.Simulation;
using Shadowbound.Core.Stats;
using Shadowbound.Core.World;

namespace Shadowbound.Core.Content
{
    /// <summary>
    /// All authored content for SHADOWBOUND: THE LAST NIGHT.
    ///
    /// The fiction, in brief. The world ended once already, in an event history
    /// remembers as the Long Nightfall, when a border in the dark called the Umbra
    /// gave way. What survived are the Embers: walled refuges held open by fire
    /// that someone has to keep feeding. The player is a Warden of the Last Ember,
    /// sent out to find out why the border is thinning again.
    ///
    /// Every creature, place and name here is original to this project.
    ///
    /// Content lives in code rather than in serialised assets so it can be
    /// validated. A test asserts that every loot entry names a real item, every
    /// quest objective names a real creature or region, and every chapter names
    /// real quests - mistakes that are otherwise only discovered by playing to the
    /// exact point where a drop silently yields nothing.
    /// </summary>
    public static class GameContent
    {
        // --------------------------------- items ---------------------------------

        public const string ItemAsh = "ash";
        public const string ItemBoneShard = "bone-shard";
        public const string ItemVeilSplinter = "veil-splinter";
        public const string ItemEmberDraught = "ember-draught";
        public const string ItemWardensBlade = "wardens-blade";
        public const string ItemUmbralBlade = "umbral-blade";
        public const string ItemAshenPlate = "ashen-plate";
        public const string ItemEmberRelic = "ember-relic";
        public const string ItemSentinelsCore = "sentinels-core";

        // ------------------------------- archetypes ------------------------------

        public const string ArchetypeHollowWalker = "hollow-walker";
        public const string ArchetypeCinderHound = "cinder-hound";
        public const string ArchetypeVeilwarden = "veilwarden";
        public const string ArchetypeAshenSentinel = "ashen-sentinel";

        // -------------------------------- regions --------------------------------

        public const string RegionCamp = "last-ember-camp";
        public const string RegionWilds = "grey-wilds";
        public const string RegionRuins = "hollowed-ruins";
        public const string RegionWard = "sunken-ward";
        public const string RegionSanctum = "umbral-sanctum";

        // --------------------------------- story ---------------------------------

        public const string ChapterAshAndSilence = "ch-ash-and-silence";
        public const string ChapterTheHollowedRuins = "ch-the-hollowed-ruins";
        public const string ChapterTheUmbralSanctum = "ch-the-umbral-sanctum";

        public const string QuestArrival = "q-arrival";
        public const string QuestFirstBlood = "q-first-blood";
        public const string QuestDescent = "q-descent";
        public const string QuestSplinters = "q-splinters";
        public const string QuestSentinel = "q-sentinel";

        // ------------------------------- loot tables -----------------------------

        private const string LootHollowWalker = "loot-hollow-walker";
        private const string LootCinderHound = "loot-cinder-hound";
        private const string LootVeilwarden = "loot-veilwarden";
        private const string LootSentinel = "loot-sentinel";

        // ================================== items ==================================

        public static ItemDatabase BuildItems()
        {
            var database = new ItemDatabase();

            database.RegisterRange(new[]
            {
                new ItemDefinition
                {
                    Id = ItemAsh,
                    DisplayName = "Ember Ash",
                    Description = "What is left of a fire that was put out on purpose.",
                    Kind = ItemKind.Material,
                    Rarity = ItemRarity.Common,
                    MaxStack = 999
                },
                new ItemDefinition
                {
                    Id = ItemBoneShard,
                    DisplayName = "Bone Shard",
                    Description = "Hollow Walkers come apart into these. They are always warm.",
                    Kind = ItemKind.Material,
                    Rarity = ItemRarity.Common,
                    MaxStack = 999
                },
                new ItemDefinition
                {
                    Id = ItemVeilSplinter,
                    DisplayName = "Veil Splinter",
                    Description = "A shard of the border. It hums when the Umbra presses close.",
                    Kind = ItemKind.Material,
                    Rarity = ItemRarity.Rare,
                    MaxStack = 99
                },
                new ItemDefinition
                {
                    Id = ItemEmberDraught,
                    DisplayName = "Ember Draught",
                    Description = "Bitter, and hot all the way down.",
                    Kind = ItemKind.Consumable,
                    Rarity = ItemRarity.Common,
                    MaxStack = 10,
                    Effects = new[]
                    {
                        new ItemEffect(EffectKind.RestoreHealth, 120f),
                        new ItemEffect(EffectKind.RestoreStamina, 40f)
                    }
                },
                new ItemDefinition
                {
                    Id = ItemWardensBlade,
                    DisplayName = "Warden's Blade",
                    Description = "Standard issue. Balanced, unfashionable, reliable.",
                    Kind = ItemKind.Weapon,
                    Rarity = ItemRarity.Common,
                    MaxStack = 1,
                    Modifiers = new[]
                    {
                        StatModifier.Flat(StatId.AttackPower, 22f),
                        StatModifier.Flat(StatId.CritChance, 0.02f)
                    }
                },
                new ItemDefinition
                {
                    Id = ItemUmbralBlade,
                    DisplayName = "Umbral Edge",
                    Description = "It cuts the dark as readily as it cuts flesh.",
                    Kind = ItemKind.Weapon,
                    Rarity = ItemRarity.Umbral,
                    MaxStack = 1,
                    RequiredLevel = 8,
                    Modifiers = new[]
                    {
                        StatModifier.Flat(StatId.AttackPower, 34f),
                        StatModifier.Flat(StatId.ShadowPower, 46f),
                        StatModifier.Percent(StatId.CritChance, 0.08f)
                    }
                },
                new ItemDefinition
                {
                    Id = ItemAshenPlate,
                    DisplayName = "Ashen Plate",
                    Description = "Salvaged from a Sentinel that had stopped moving.",
                    Kind = ItemKind.Armor,
                    Rarity = ItemRarity.Rare,
                    MaxStack = 1,
                    RequiredLevel = 6,
                    Modifiers = new[]
                    {
                        StatModifier.Flat(StatId.Armor, 45f),
                        StatModifier.Flat(StatId.MaxHealth, 80f),
                        StatModifier.Flat(StatId.MoveSpeed, -0.4f)
                    }
                },
                new ItemDefinition
                {
                    Id = ItemEmberRelic,
                    DisplayName = "Ember Relic",
                    Description = "A coal that has refused to go out for two hundred years.",
                    Kind = ItemKind.Relic,
                    Rarity = ItemRarity.Rare,
                    MaxStack = 1,
                    Modifiers = new[]
                    {
                        StatModifier.Flat(StatId.ShadowPower, 18f),
                        StatModifier.Flat(StatId.HealthRegen, 2.5f),
                        StatModifier.Percent(StatId.StatusResistance, 0.15f)
                    }
                },
                new ItemDefinition
                {
                    Id = ItemSentinelsCore,
                    DisplayName = "Sentinel's Core",
                    Description = "Still trying to give orders to something that no longer exists.",
                    Kind = ItemKind.Quest,
                    Rarity = ItemRarity.Mythic,
                    MaxStack = 1,
                    IsBound = true
                }
            });

            return database;
        }

        // =============================== player kit ===============================

        /// <summary>
        /// The Warden's starting kit. Order matters: index 0 is the basic attack,
        /// which is what the AI and the default input binding assume.
        /// </summary>
        public static List<AbilityDefinition> BuildPlayerAbilities()
        {
            return new List<AbilityDefinition>
            {
                // 0 - fast, cheap, the bread and butter.
                new AbilityDefinition
                {
                    Id = "ember-edge",
                    DisplayName = "Ember Edge",
                    Kind = AbilityKind.Melee,
                    DamageType = DamageType.Physical,
                    StaminaCost = 8f,
                    CooldownSeconds = 0.35f,
                    WindupSeconds = 0.18f,
                    RecoverySeconds = 0.16f,
                    Range = 2.6f,
                    ConeHalfAngleDegrees = 70f,
                    DamageMultiplier = 1f,
                    Variance = 0.06f,
                    MaxTargets = 1,
                    MoveSpeedDuringWindup = 0.3f,
                    KnockbackSpeed = 3f
                },

                // 1 - the Umbra answer. Scales off Shadow Power, not Attack.
                new AbilityDefinition
                {
                    Id = "umbra-lance",
                    DisplayName = "Umbra Lance",
                    Kind = AbilityKind.Bolt,
                    DamageType = DamageType.Shadow,
                    UsesShadowPower = true,
                    StaminaCost = 22f,
                    CooldownSeconds = 2.2f,
                    WindupSeconds = 0.35f,
                    RecoverySeconds = 0.3f,
                    Range = 12f,
                    ConeHalfAngleDegrees = 12f,
                    DamageMultiplier = 1.35f,
                    Variance = 0.08f,
                    MaxTargets = 1,
                    MoveSpeedDuringWindup = 0.2f,
                    OnHitStatuses = new[]
                    {
                        new StatusApplication(StatusKind.Marked, 0.2f, 5f, 0f, 1)
                    }
                },

                // 2 - mobility. Deals no damage by design.
                new AbilityDefinition
                {
                    Id = "ashstep",
                    DisplayName = "Ashstep",
                    Kind = AbilityKind.Dash,
                    DamageType = DamageType.Physical,
                    StaminaCost = 18f,
                    CooldownSeconds = 3f,
                    WindupSeconds = 0.08f,
                    RecoverySeconds = 0.12f,
                    DashDistance = 5.5f,
                    MoveSpeedDuringWindup = 0f
                },

                // 3 - the heavy answer. Wide, slow, and it costs the caster.
                new AbilityDefinition
                {
                    Id = "sunder",
                    DisplayName = "Sunder",
                    Kind = AbilityKind.Cleave,
                    DamageType = DamageType.Physical,
                    StaminaCost = 32f,
                    CooldownSeconds = 5f,
                    WindupSeconds = 0.55f,
                    RecoverySeconds = 0.45f,
                    Range = 3.4f,
                    ConeHalfAngleDegrees = 130f,
                    DamageMultiplier = 2.1f,
                    Variance = 0.1f,
                    MaxTargets = 4,
                    KnockbackSpeed = 9f,
                    MoveSpeedDuringWindup = 0f,
                    SelfStaggerSeconds = 0.35f
                },

                // 4 - the defensive option.
                new AbilityDefinition
                {
                    Id = "ward-of-embers",
                    DisplayName = "Ward of Embers",
                    Kind = AbilityKind.Self,
                    StaminaCost = 25f,
                    CooldownSeconds = 14f,
                    WindupSeconds = 0.3f,
                    RecoverySeconds = 0.4f,
                    Range = 0f,
                    MaxTargets = 0,
                    DamageMultiplier = 0f,
                    MoveSpeedDuringWindup = 0.5f,
                    OnHitStatuses = new[]
                    {
                        new StatusApplication(StatusKind.Warded, 0.45f, 8f, 0f, 1),
                        new StatusApplication(StatusKind.Empowered, 0.2f, 8f, 0f, 1)
                    }
                }
            };
        }

        public static StatGrowth[] BuildPlayerGrowth()
        {
            return new[]
            {
                new StatGrowth(StatId.MaxHealth, 22f),
                new StatGrowth(StatId.MaxStamina, 6f),
                new StatGrowth(StatId.AttackPower, 2.5f),
                new StatGrowth(StatId.ShadowPower, 2.5f),
                new StatGrowth(StatId.Armor, 1.5f)
            };
        }

        /// <summary>
        /// What one attribute point buys in each stat.
        ///
        /// Points arrive slower than gear does, so one point is worth a little less
        /// than one level of automatic growth: enough to shape a character (Vitality
        /// over Attack) without outcompeting the items they find. Fraction stats are
        /// stored as fractions, so 0.01 there is one percentage point.
        /// </summary>
        public static float AttributeAward(StatId stat)
        {
            switch (stat)
            {
                case StatId.MaxHealth: return 15f;
                case StatId.MaxStamina: return 8f;
                case StatId.AttackPower: return 2f;
                case StatId.ShadowPower: return 2f;
                case StatId.Armor: return 2f;
                case StatId.MoveSpeed: return 0.15f;
                case StatId.CritChance: return 0.01f;
                case StatId.CritMultiplier: return 0.05f;
                case StatId.HealthRegen: return 0.4f;
                case StatId.StaminaRegen: return 0.6f;
                case StatId.CooldownRate: return 0.05f;
                case StatId.StatusResistance: return 0.02f;
                default: return 0f;
            }
        }

        /// <summary>A level 1 Warden at full health, ready to place in a scene.</summary>
        public static Combatant CreatePlayer(string id = "warden")
        {
            var player = new Combatant(id, Faction.Player, 1)
            {
                DisplayName = "Warden",
                ArchetypeId = "warden",
                IsPersistent = true
            };

            StatSet stats = player.Stats;
            stats.SetBase(StatId.MaxHealth, 320f);
            stats.SetBase(StatId.MaxStamina, 100f);
            stats.SetBase(StatId.AttackPower, 18f);
            stats.SetBase(StatId.ShadowPower, 16f);
            stats.SetBase(StatId.Armor, 10f);
            stats.SetBase(StatId.MoveSpeed, 5.5f);
            stats.SetBase(StatId.CritChance, 0.05f);
            stats.SetBase(StatId.CritMultiplier, 1.6f);
            stats.SetBase(StatId.CooldownRate, 1f);
            stats.SetBase(StatId.HealthRegen, 1.5f);
            stats.SetBase(StatId.StaminaRegen, 14f);
            stats.SetBase(StatId.StatusResistance, 0.1f);

            player.Vitals.ResetToFull();
            return player;
        }

        // ================================ creatures ================================

        private static EnemyBrainSettings AggressiveBrain(float attackRange, float reactionTime, float viewDistance)
        {
            return new EnemyBrainSettings
            {
                ViewDistance = viewDistance,
                ViewHalfAngleDegrees = 80f,
                ProximityRadius = 3.5f,
                DeaggroRange = 34f,
                LoseSightGrace = 5f,
                AttackRange = attackRange,
                PreferredRange = attackRange * 0.8f,
                ReactionTime = reactionTime,
                IdleDuration = 4f,
                PatrolRadius = 10f,
                PatrolSpeedMultiplier = 0.4f,
                ChaseSpeedMultiplier = 1f,
                BackoffSpeedMultiplier = 0.5f,
                AttackCommitment = 1f,
                PatrolWaypointTimeout = 7f
            };
        }

        public static EnemyArchetype BuildHollowWalker()
        {
            return new EnemyArchetype
            {
                Id = ArchetypeHollowWalker,
                DisplayName = "Hollow Walker",
                Description = "It was a person. Whatever is left walks, and reaches.",
                Level = 1,
                MaxHealth = 90f,
                AttackPower = 14f,
                Armor = 5f,
                MoveSpeed = 3.6f,
                CritChance = 0.02f,
                Resistances = new ResistanceSet(),
                ExperienceReward = 35,
                LootTableId = LootHollowWalker,
                BodyScale = 0.95f,
                TintRgb = new[] { 0.42f, 0.40f, 0.36f },
                AttackAbilityIndex = 0,
                Brain = AggressiveBrain(2.4f, 0.5f, 15f),
                Abilities = new List<AbilityDefinition>
                {
                    new AbilityDefinition
                    {
                        Id = "hollow-claw",
                        DisplayName = "Hollow Claw",
                        Kind = AbilityKind.Melee,
                        DamageType = DamageType.Physical,
                        CooldownSeconds = 1.1f,
                        WindupSeconds = 0.45f,
                        RecoverySeconds = 0.35f,
                        Range = 2.4f,
                        ConeHalfAngleDegrees = 80f,
                        DamageMultiplier = 1f,
                        Variance = 0.08f,
                        MaxTargets = 1,
                        MoveSpeedDuringWindup = 0.15f,
                        KnockbackSpeed = 2f
                    }
                }
            };
        }

        public static EnemyArchetype BuildCinderHound()
        {
            var resistances = new ResistanceSet();
            resistances.Set(DamageType.Ember, 0.6f);
            resistances.Set(DamageType.Frost, -0.25f);

            return new EnemyArchetype
            {
                Id = ArchetypeCinderHound,
                DisplayName = "Cinder Hound",
                Description = "It burned to death and kept running. Fire still likes it.",
                Level = 3,
                MaxHealth = 130f,
                AttackPower = 20f,
                Armor = 0f,
                MoveSpeed = 5.2f,
                CritChance = 0.08f,
                Resistances = resistances,
                ExperienceReward = 60,
                LootTableId = LootCinderHound,
                BodyScale = 0.8f,
                TintRgb = new[] { 0.72f, 0.32f, 0.16f },
                AttackAbilityIndex = 0,
                Brain = AggressiveBrain(2.2f, 0.32f, 17f),
                Abilities = new List<AbilityDefinition>
                {
                    new AbilityDefinition
                    {
                        Id = "cinder-maul",
                        DisplayName = "Cinder Maul",
                        Kind = AbilityKind.Melee,
                        DamageType = DamageType.Physical,
                        CooldownSeconds = 1.4f,
                        WindupSeconds = 0.38f,
                        RecoverySeconds = 0.3f,
                        Range = 2.2f,
                        ConeHalfAngleDegrees = 60f,
                        DamageMultiplier = 0.9f,
                        Variance = 0.1f,
                        MaxTargets = 1,
                        MoveSpeedDuringWindup = 0.4f,
                        OnHitStatuses = new[]
                        {
                            new StatusApplication(StatusKind.Burning, 7f, 5f, 1f, 3, 0.6f)
                        }
                    }
                }
            };
        }

        public static EnemyArchetype BuildVeilwarden()
        {
            var resistances = new ResistanceSet();
            resistances.Set(DamageType.Shadow, 0.45f);
            resistances.Set(DamageType.Vital, -0.2f);

            return new EnemyArchetype
            {
                Id = ArchetypeVeilwarden,
                DisplayName = "Veilwarden",
                Description = "It still thinks it is guarding something. It is not wrong.",
                Level = 6,
                MaxHealth = 320f,
                AttackPower = 30f,
                ShadowPower = 24f,
                Armor = 40f,
                MoveSpeed = 4.2f,
                CritChance = 0.1f,
                StatusResistance = 0.3f,
                Resistances = resistances,
                ExperienceReward = 180,
                LootTableId = LootVeilwarden,
                BodyScale = 1.35f,
                TintRgb = new[] { 0.28f, 0.30f, 0.42f },
                AttackAbilityIndex = 0,
                Brain = AggressiveBrain(3f, 0.42f, 18f),
                Abilities = new List<AbilityDefinition>
                {
                    new AbilityDefinition
                    {
                        Id = "veil-sweep",
                        DisplayName = "Veil Sweep",
                        Kind = AbilityKind.Cleave,
                        DamageType = DamageType.Shadow,
                        UsesShadowPower = true,
                        CooldownSeconds = 2.4f,
                        WindupSeconds = 0.7f,
                        RecoverySeconds = 0.5f,
                        Range = 3.6f,
                        ConeHalfAngleDegrees = 140f,
                        DamageMultiplier = 1.3f,
                        CritChanceBonus = 0.05f,
                        Variance = 0.08f,
                        MaxTargets = 3,
                        MoveSpeedDuringWindup = 0.1f,
                        KnockbackSpeed = 6f
                    }
                }
            };
        }

        public static EnemyArchetype BuildAshenSentinel()
        {
            var resistances = new ResistanceSet();
            resistances.Set(DamageType.Physical, 0.25f);
            resistances.Set(DamageType.Ember, 0.5f);
            resistances.Set(DamageType.Frost, -0.35f);

            return new EnemyArchetype
            {
                Id = ArchetypeAshenSentinel,
                DisplayName = "The Ashen Sentinel",
                Description = "It was left here to hold the gate. Nobody ever relieved it.",
                Level = 10,
                MaxHealth = 1500f,
                AttackPower = 46f,
                ShadowPower = 30f,
                Armor = 70f,
                MoveSpeed = 3.4f,
                CritChance = 0.12f,
                CritMultiplier = 1.8f,
                StatusResistance = 0.45f,
                HealthRegen = 4f,
                Resistances = resistances,
                ExperienceReward = 1200,
                LootTableId = LootSentinel,
                IsBoss = true,
                BodyScale = 2.2f,
                TintRgb = new[] { 0.34f, 0.26f, 0.24f },
                AttackAbilityIndex = 0,
                Brain = AggressiveBrain(3.4f, 0.6f, 22f),
                Abilities = new List<AbilityDefinition>
                {
                    // 0 - the basic, wide, slow cleave.
                    new AbilityDefinition
                    {
                        Id = "sentinel-cleave",
                        DisplayName = "Grave Cleave",
                        Kind = AbilityKind.Cleave,
                        DamageType = DamageType.Physical,
                        CooldownSeconds = 2.6f,
                        WindupSeconds = 0.85f,
                        RecoverySeconds = 0.6f,
                        Range = 4.2f,
                        ConeHalfAngleDegrees = 160f,
                        DamageMultiplier = 1.5f,
                        Variance = 0.08f,
                        MaxTargets = 4,
                        MoveSpeedDuringWindup = 0f,
                        KnockbackSpeed = 10f,
                        SelfStaggerSeconds = 0.4f
                    }
                }
            };
        }

        public static List<EnemyArchetype> BuildEnemyArchetypes()
        {
            return new List<EnemyArchetype>
            {
                BuildHollowWalker(),
                BuildCinderHound(),
                BuildVeilwarden(),
                BuildAshenSentinel()
            };
        }

        public static EnemyArchetype FindArchetype(string id)
        {
            List<EnemyArchetype> archetypes = BuildEnemyArchetypes();

            for (int i = 0; i < archetypes.Count; i++)
            {
                if (archetypes[i].Id == id)
                {
                    return archetypes[i];
                }
            }

            return null;
        }

        // =============================== loot tables ===============================

        public static Dictionary<string, LootTable> BuildLootTables()
        {
            var tables = new Dictionary<string, LootTable>(StringComparer.Ordinal);

            tables[LootHollowWalker] = new LootTable
            {
                Id = LootHollowWalker,
                Guaranteed = new[] { new LootEntry(ItemAsh, 1f, 1, 2) },
                Weighted = new[]
                {
                    new LootEntry(ItemBoneShard, 6f, 1, 1),
                    new LootEntry(ItemEmberDraught, 2f, 1, 1),
                    new LootEntry(ItemAsh, 4f, 1, 2)
                },
                MinRolls = 1,
                MaxRolls = 1,
                NoDropChance = 0.1f
            };

            tables[LootCinderHound] = new LootTable
            {
                Id = LootCinderHound,
                Guaranteed = new[] { new LootEntry(ItemAsh, 1f, 2, 3) },
                Weighted = new[]
                {
                    new LootEntry(ItemBoneShard, 3f, 1, 2),
                    new LootEntry(ItemEmberDraught, 3f, 1, 1),
                    new LootEntry(ItemEmberRelic, 0.4f, 1, 1)
                },
                MinRolls = 1,
                MaxRolls = 2
            };

            tables[LootVeilwarden] = new LootTable
            {
                Id = LootVeilwarden,
                Guaranteed = new[] { new LootEntry(ItemVeilSplinter, 1f, 1, 1) },
                Weighted = new[]
                {
                    new LootEntry(ItemEmberRelic, 3f, 1, 1),
                    new LootEntry(ItemEmberDraught, 4f, 1, 2),
                    new LootEntry(ItemAshenPlate, 2f, 1, 1)
                },
                MinRolls = 1,
                MaxRolls = 2
            };

            tables[LootSentinel] = new LootTable
            {
                Id = LootSentinel,
                Guaranteed = new[]
                {
                    new LootEntry(ItemSentinelsCore, 1f, 1, 1),
                    new LootEntry(ItemVeilSplinter, 1f, 3, 3),
                    new LootEntry(ItemAshenPlate, 1f, 1, 1)
                },
                Weighted = new[]
                {
                    new LootEntry(ItemUmbralBlade, 1f, 1, 1),
                    new LootEntry(ItemEmberRelic, 3f, 1, 1),
                    new LootEntry(ItemEmberDraught, 4f, 2, 3)
                },
                MinRolls = 2,
                MaxRolls = 3
            };

            return tables;
        }

        // ================================== world ==================================

        public static List<RegionDefinition> BuildRegions()
        {
            return new List<RegionDefinition>
            {
                new RegionDefinition
                {
                    Id = RegionCamp,
                    DisplayName = "The Last Ember",
                    Kind = RegionKind.Camp,
                    Connections = new[] { RegionWilds },
                    RecommendedLevel = 1,
                    LootTableId = "",
                    EncounterIds = Array.Empty<string>()
                },
                new RegionDefinition
                {
                    Id = RegionWilds,
                    DisplayName = "The Grey Wilds",
                    Kind = RegionKind.Wilds,
                    Connections = new[] { RegionCamp, RegionRuins, RegionWard },
                    RecommendedLevel = 2,
                    EncounterIds = new[] { ArchetypeHollowWalker, ArchetypeCinderHound }
                },
                new RegionDefinition
                {
                    Id = RegionRuins,
                    DisplayName = "The Hollowed Ruins",
                    Kind = RegionKind.Ruins,
                    Connections = new[] { RegionWilds, RegionSanctum },
                    RequiredChapterId = ChapterAshAndSilence,
                    RecommendedLevel = 5,
                    EncounterIds = new[] { ArchetypeHollowWalker, ArchetypeVeilwarden }
                },
                new RegionDefinition
                {
                    Id = RegionWard,
                    DisplayName = "The Sunken Ward",
                    Kind = RegionKind.Ruins,
                    Connections = new[] { RegionWilds },
                    RequiredChapterId = ChapterTheHollowedRuins,
                    RecommendedLevel = 8,
                    EncounterIds = new[] { ArchetypeVeilwarden }
                },
                new RegionDefinition
                {
                    Id = RegionSanctum,
                    DisplayName = "The Umbral Sanctum",
                    Kind = RegionKind.Threshold,
                    Connections = new[] { RegionRuins },
                    RequiredChapterId = ChapterTheHollowedRuins,
                    RecommendedLevel = 10,
                    EncounterIds = new[] { ArchetypeAshenSentinel }
                }
            };
        }

        // ============================== quests and story ============================

        public static List<QuestDefinition> BuildQuests()
        {
            return new List<QuestDefinition>
            {
                new QuestDefinition
                {
                    Id = QuestArrival,
                    Title = "Beyond the Wall",
                    Summary = "Step outside the Ember and see what is still standing.",
                    ChapterId = ChapterAshAndSilence,
                    Objectives = new[]
                    {
                        new ObjectiveDefinition("reach-wilds", ObjectiveKind.Reach, RegionWilds, 1, false, "Reach the Grey Wilds")
                    },
                    Rewards = new QuestReward
                    {
                        Experience = 60,
                        AttributePoints = 1,
                        Items = new[] { new ItemStack(ItemEmberDraught, 2) }
                    }
                },
                new QuestDefinition
                {
                    Id = QuestFirstBlood,
                    Title = "Ash and Silence",
                    Summary = "The Hollow Walkers were people once. They are not now.",
                    ChapterId = ChapterAshAndSilence,
                    PrerequisiteQuestIds = new[] { QuestArrival },
                    Objectives = new[]
                    {
                        new ObjectiveDefinition("kills", ObjectiveKind.Kill, ArchetypeHollowWalker, 4, false, "Put down Hollow Walkers")
                    },
                    Rewards = new QuestReward
                    {
                        Experience = 220,
                        AttributePoints = 1,
                        Items = new[]
                        {
                            new ItemStack(ItemWardensBlade, 1),
                            new ItemStack(ItemEmberDraught, 3)
                        }
                    }
                },
                new QuestDefinition
                {
                    Id = QuestDescent,
                    Title = "The Hollowed Ruins",
                    Summary = "Something down there is still drawing breath through the Veil.",
                    ChapterId = ChapterTheHollowedRuins,
                    PrerequisiteQuestIds = new[] { QuestFirstBlood },
                    Objectives = new[]
                    {
                        new ObjectiveDefinition("reach-ruins", ObjectiveKind.Reach, RegionRuins, 1, false, "Enter the Hollowed Ruins")
                    },
                    Rewards = new QuestReward
                    {
                        Experience = 300,
                        AttributePoints = 1
                    }
                },
                new QuestDefinition
                {
                    Id = QuestSplinters,
                    Title = "Splinters of the Veil",
                    Summary = "Collect what the Veilwardens carry. It is the only thing that tells us where the border is thin.",
                    ChapterId = ChapterTheHollowedRuins,
                    PrerequisiteQuestIds = new[] { QuestDescent },
                    Objectives = new[]
                    {
                        new ObjectiveDefinition("splinters", ObjectiveKind.Collect, ItemVeilSplinter, 3, false, "Recover Veil Splinters")
                    },
                    Rewards = new QuestReward
                    {
                        Experience = 450,
                        AttributePoints = 1,
                        Items = new[] { new ItemStack(ItemEmberRelic, 1) }
                    }
                },
                new QuestDefinition
                {
                    Id = QuestSentinel,
                    Title = "The Last Night",
                    Summary = "Whatever has been keeping the gate closed is awake, and it has been waiting.",
                    ChapterId = ChapterTheUmbralSanctum,
                    PrerequisiteQuestIds = new[] { QuestSplinters },
                    Objectives = new[]
                    {
                        new ObjectiveDefinition("sentinel", ObjectiveKind.DefeatBoss, ArchetypeAshenSentinel, 1, false, "Defeat the Ashen Sentinel")
                    },
                    Rewards = new QuestReward
                    {
                        Experience = 1500,
                        AttributePoints = 2,
                        Items = new[] { new ItemStack(ItemUmbralBlade, 1) }
                    }
                }
            };
        }

        public static List<ChapterDefinition> BuildChapters()
        {
            return new List<ChapterDefinition>
            {
                new ChapterDefinition
                {
                    Id = ChapterAshAndSilence,
                    Title = "Ash and Silence",
                    Summary = "The Ember is failing. Find out why.",
                    QuestIds = new[] { QuestArrival, QuestFirstBlood },
                    RegionId = RegionWilds
                },
                new ChapterDefinition
                {
                    Id = ChapterTheHollowedRuins,
                    Title = "The Hollowed Ruins",
                    Summary = "The border is thin where the old city fell.",
                    QuestIds = new[] { QuestDescent, QuestSplinters },
                    RequiredChapterIds = new[] { ChapterAshAndSilence },
                    RegionId = RegionRuins
                },
                new ChapterDefinition
                {
                    Id = ChapterTheUmbralSanctum,
                    Title = "The Last Night",
                    Summary = "Hold the gate, or watch it open.",
                    QuestIds = new[] { QuestSentinel },
                    RequiredChapterIds = new[] { ChapterTheHollowedRuins },
                    RegionId = RegionSanctum
                }
            };
        }

        /// <summary>
        /// Installs all content into a session: quests, chapters, regions and loot.
        /// Items and the player's kit are supplied by the caller, since those belong
        /// to the session's construction.
        /// </summary>
        public static void Populate(GameSession session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            session.Quests.RegisterRange(BuildQuests());
            session.Chapters.RegisterRange(BuildChapters());

            List<RegionDefinition> regions = BuildRegions();
            for (int i = 0; i < regions.Count; i++)
            {
                session.World.Register(regions[i]);
            }

            Dictionary<string, LootTable> tables = BuildLootTables();
            foreach (KeyValuePair<string, LootTable> entry in tables)
            {
                session.RegisterLootTable(entry.Value);
            }

            session.RegionId = RegionCamp;
        }
    }
}
