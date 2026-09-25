using System.Collections.Generic;
using Shadowbound.Core.Ai;
using Shadowbound.Core.Combat;
using Shadowbound.Core.Items;
using Shadowbound.Core.Numerics;
using Shadowbound.Core.Progression;
using Shadowbound.Core.Quests;
using Shadowbound.Core.Randomness;
using Shadowbound.Core.Serialization;
using Shadowbound.Core.Simulation;
using Shadowbound.Core.Stats;
using Shadowbound.Core.Tests.Support;
using Shadowbound.Core.World;
using Xunit;

namespace Shadowbound.Core.Tests.Simulation
{
    /// <summary>
    /// These tests exercise the whole runtime path rather than one system:
    /// a decision is taken, an ability is cast, a hit lands, a creature dies, and
    /// that death moves the journal, the experience bar and the inventory.
    ///
    /// If any link in that chain is disconnected, one of these fails.
    /// </summary>
    public class GameSessionTests
    {
        private static readonly Float3 Forward = new Float3(0f, 0f, 1f);

        private const string WalkerArchetype = "hollow-walker";

        private static AbilityDefinition Swing()
        {
            return new AbilityDefinition
            {
                Id = "swing",
                Kind = AbilityKind.Melee,
                DamageType = DamageType.Physical,
                WindupSeconds = 0.15f,
                RecoverySeconds = 0.2f,
                CooldownSeconds = 0.5f,
                Range = 2.5f,
                ConeHalfAngleDegrees = 120f,
                DamageMultiplier = 1f,
                Variance = 0f,
                MaxTargets = 1
            };
        }

        private static EnemyBrainSettings WalkerBrain()
        {
            return new EnemyBrainSettings
            {
                ViewDistance = 20f,
                ViewHalfAngleDegrees = 90f,
                AttackRange = 2.6f,
                PreferredRange = 2f,
                ReactionTime = 0.2f,
                AttackCommitment = 0.7f,
                IdleDuration = 100f
            };
        }

        private static GameSession MakeSession(
            float playerAttackPower = 500f,
            Dictionary<string, LootTable> lootTables = null)
        {
            var items = new ItemDatabase();
            items.Register(ItemTestData.Material("ash", 999));
            items.Register(ItemTestData.Material("bone-shard", 999));
            items.Register(ItemTestData.Weapon("umbral-blade", 40f));

            Combatant player = CombatantFactory.Create(
                "hero",
                Faction.Player,
                maxHealth: 500f,
                maxStamina: 200f,
                attackPower: playerAttackPower);

            player.FaceImmediately(Forward);

            var session = new GameSession(
                player,
                items,
                new ExperienceCurve(30, 120f, 1.55f),
                new[]
                {
                    new StatGrowth(StatId.MaxHealth, 20f),
                    new StatGrowth(StatId.AttackPower, 5f)
                },
                new DeterministicRng(20250925),
                WorldBounds.Square(50f));

            // The automatic quest lifecycle is deliberately off here.
            //
            // These tests are about the session's reward plumbing: a kill turning
            // into experience, loot and journal progress, and a quest being turned in
            // by hand. Leaving automatic advancement on would claim rewards mid-test
            // and change the experience totals these tests assert on, which would
            // make them pass or fail for reasons unrelated to what they examine.
            //
            // The automatic lifecycle - turning in, granting, and unlocking the next
            // quest - has its own class, QuestAdvancementTests, where it is the
            // subject rather than a side effect.
            session.AutoAdvanceQuests = false;

            session.RegisterLootTable(BuildWalkerLoot());

            if (lootTables != null)
            {
                foreach (KeyValuePair<string, LootTable> entry in lootTables)
                {
                    session.RegisterLootTable(entry.Value);
                }
            }

            return session;
        }

        private static LootTable BuildWalkerLoot()
        {
            return new LootTable
            {
                Id = "walker-loot",
                Guaranteed = new[] { new LootEntry("ash", 1f, 2, 2) },
                Weighted = new[] { new LootEntry("bone-shard", 1f, 1, 1) },
                MinRolls = 1,
                MaxRolls = 1
            };
        }

        private static Combatant SpawnWalker(
            GameSession session,
            Float3 position,
            float maxHealth = 100f,
            int experienceReward = 50,
            bool isBoss = false)
        {
            Combatant walker = CombatantFactory.Create(
                "walker-" + position.Z,
                Faction.Hostile,
                maxHealth: maxHealth,
                attackPower: 10f,
                position: position);

            walker.ArchetypeId = WalkerArchetype;
            walker.ExperienceReward = experienceReward;
            walker.LootTableId = "walker-loot";
            walker.IsBoss = isBoss;
            walker.DisplayName = "Hollow Walker";

            session.Encounter.AddEnemy(walker, new[] { Swing() }, WalkerBrain());
            return walker;
        }

        private static void RegisterHuntQuest(GameSession session, int kills = 1, QuestReward? reward = null)
        {
            session.Quests.Register(new QuestDefinition
            {
                Id = "q-hunt",
                Title = "Hollow Ground",
                ChapterId = "ch1",
                Objectives = new[]
                {
                    new ObjectiveDefinition("kills", ObjectiveKind.Kill, WalkerArchetype, kills)
                },
                Rewards = reward ?? new QuestReward
                {
                    Experience = 200,
                    AttributePoints = 1,
                    Items = new[] { new ItemStack("ash", 5) }
                }
            });
        }

        // ------------------------------ the whole chain ----------------------------

        [Fact]
        public void KillingAnEnemy_ProgressesTheQuestGrantsExperienceAndDropsLoot()
        {
            GameSession session = MakeSession();
            RegisterHuntQuest(session);
            Assert.True(session.Quests.TryStart("q-hunt"));

            SpawnWalker(session, new Float3(0f, 0f, 1.5f));

            session.Encounter.AddDriven(
                session.Player, new[] { Swing() }, new AggressiveMeleeDriver(), isPlayer: true);

            session.Encounter.Advance(3f);

            // The kill happened...
            Assert.Equal(0, session.Encounter.HostilesRemaining);

            // ...it granted experience...
            Assert.Equal(50, session.Progression.TotalExperience);

            // ...it dropped loot into the bag...
            Assert.Equal(2, session.Inventory.Count("ash"));
            Assert.Equal(1, session.Inventory.Count("bone-shard"));

            // ...and it advanced the journal.
            Assert.Equal(QuestStatus.Completed, session.Quests.StatusOf("q-hunt"));
        }

        [Fact]
        public void TurningInACompletedQuest_GrantsItsReward()
        {
            GameSession session = MakeSession();
            RegisterHuntQuest(session);
            session.Quests.TryStart("q-hunt");

            SpawnWalker(session, new Float3(0f, 0f, 1.5f));
            session.Encounter.AddDriven(
                session.Player, new[] { Swing() }, new AggressiveMeleeDriver(), isPlayer: true);
            session.Encounter.Advance(3f);

            int experienceBefore = session.Progression.TotalExperience;
            int ashBefore = session.Inventory.Count("ash");

            Assert.True(session.Quests.TryTurnIn("q-hunt", out QuestReward reward));
            session.GrantReward(reward);

            Assert.Equal(experienceBefore + 200, session.Progression.TotalExperience);
            Assert.Equal(ashBefore + 5, session.Inventory.Count("ash"));

            // Two points, from two distinct sources: one granted explicitly by the
            // quest reward, and one from reaching level 2 on the reward's
            // experience.
            Assert.Equal(2, session.Progression.UnspentAttributePoints);
            Assert.Equal(2, session.Progression.Level);
        }

        [Fact]
        public void RewardsCannotBeClaimedBeforeTheQuestIsComplete()
        {
            GameSession session = MakeSession();
            RegisterHuntQuest(session, kills: 3);
            session.Quests.TryStart("q-hunt");

            SpawnWalker(session, new Float3(0f, 0f, 1.5f));
            session.Encounter.AddDriven(
                session.Player, new[] { Swing() }, new AggressiveMeleeDriver(), isPlayer: true);
            session.Encounter.Advance(3f);

            Assert.Equal(QuestStatus.Active, session.Quests.StatusOf("q-hunt"));
            Assert.False(session.Quests.TryTurnIn("q-hunt", out _));
        }

        [Fact]
        public void ExperienceFromKills_EventuallyLevelsThePlayer()
        {
            GameSession session = MakeSession();

            // Enough kills to cross several level thresholds.
            for (int i = 0; i < 12; i++)
            {
                SpawnWalker(session, new Float3(0f, 0f, 1.5f), maxHealth: 50f, experienceReward: 60);
            }

            session.Encounter.AddDriven(
                session.Player, new[] { Swing() }, new AggressiveMeleeDriver(), isPlayer: true);

            session.Encounter.Advance(20f);

            Assert.True(session.Progression.Level > 1, "The player should have levelled up.");
            Assert.True(session.Progression.UnspentAttributePoints > 0);
            Assert.Equal(500f + (20f * (session.Progression.Level - 1)), session.Player.Vitals.MaxHealth, 2);
        }

        [Fact]
        public void DefeatingABoss_SatisfiesABossObjectiveRatherThanAKillObjective()
        {
            GameSession session = MakeSession(playerAttackPower: 900f);

            session.Quests.Register(new QuestDefinition
            {
                Id = "q-sentinel",
                Objectives = new[]
                {
                    new ObjectiveDefinition("boss", ObjectiveKind.DefeatBoss, "sentinel", 1)
                }
            });

            session.Quests.TryStart("q-sentinel");

            Combatant boss = CombatantFactory.Create(
                "sentinel", Faction.Hostile, maxHealth: 100f, position: new Float3(0f, 0f, 1.5f));
            boss.ArchetypeId = "sentinel";
            boss.IsBoss = true;
            boss.ExperienceReward = 500;

            session.Encounter.AddEnemy(boss, new[] { Swing() }, WalkerBrain());
            session.Encounter.AddDriven(
                session.Player, new[] { Swing() }, new AggressiveMeleeDriver(), isPlayer: true);

            session.Encounter.Advance(3f);

            Assert.Equal(QuestStatus.Completed, session.Quests.StatusOf("q-sentinel"));
        }

        // --------------------------- collection objectives --------------------------

        [Fact]
        public void CollectObjectives_TrackWhatThePlayerHoldsRatherThanPickups()
        {
            GameSession session = MakeSession();

            session.Quests.Register(new QuestDefinition
            {
                Id = "q-gather",
                Objectives = new[]
                {
                    new ObjectiveDefinition("hides", ObjectiveKind.Collect, "ash", 5)
                }
            });

            session.Quests.TryStart("q-gather");

            session.GrantItem("ash", 3);
            Assert.Equal(QuestStatus.Active, session.Quests.StatusOf("q-gather"));

            session.GrantItem("ash", 2);
            Assert.Equal(QuestStatus.Completed, session.Quests.StatusOf("q-gather"));
        }

        [Fact]
        public void DroppingCollectedItems_TakesBackTheObjectiveProgress()
        {
            // Collection objectives track what the player holds, not a tally of
            // pickups. Reporting pickups cumulatively would let a player pick an
            // item up and drop it repeatedly to finish the quest.
            //
            // The requirement is deliberately higher than the amount granted, so
            // the quest is still in progress and the regression is observable.
            // A completed quest does not un-complete because items were sold.
            GameSession session = MakeSession();

            session.Quests.Register(new QuestDefinition
            {
                Id = "q-gather",
                Objectives = new[]
                {
                    new ObjectiveDefinition("hides", ObjectiveKind.Collect, "ash", 8)
                }
            });

            session.Quests.TryStart("q-gather");
            session.GrantItem("ash", 5);
            Assert.Equal(5, session.Quests.State("q-gather").ProgressOf("hides"));

            session.Inventory.Remove("ash", 3);
            session.SyncCollectionObjectives();

            Assert.Equal(2, session.Quests.State("q-gather").ProgressOf("hides"));
        }

        [Fact]
        public void ACompletedCollectionQuest_StaysCompleteWhenItemsAreSold()
        {
            GameSession session = MakeSession();

            session.Quests.Register(new QuestDefinition
            {
                Id = "q-gather",
                Objectives = new[]
                {
                    new ObjectiveDefinition("hides", ObjectiveKind.Collect, "ash", 3)
                }
            });

            session.Quests.TryStart("q-gather");
            session.GrantItem("ash", 3);
            Assert.Equal(QuestStatus.Completed, session.Quests.StatusOf("q-gather"));

            session.Inventory.Remove("ash", 3);
            session.SyncCollectionObjectives();

            Assert.Equal(QuestStatus.Completed, session.Quests.StatusOf("q-gather"));
        }

        [Fact]
        public void GrantItem_ReportsHowMuchWasActuallyAccepted()
        {
            GameSession session = MakeSession();
            var granted = new List<ItemStack>();
            session.LootGranted += stack => granted.Add(stack);

            int accepted = session.GrantItem("ash", 4);

            Assert.Equal(4, accepted);
            Assert.Single(granted);
            Assert.Equal(4, granted[0].Quantity);
        }

        [Fact]
        public void GrantItem_OfAnUnknownItem_IsRefused()
        {
            GameSession session = MakeSession();

            Assert.Equal(0, session.GrantItem("not-a-real-item", 1));
        }

        // ----------------------------------- world ---------------------------------

        [Fact]
        public void EnteringARegion_RecordsItAndReportsItToQuests()
        {
            GameSession session = MakeSession();

            session.World.Register(new RegionDefinition { Id = "wilds", Connections = new[] { "ruins" } });
            session.World.Register(new RegionDefinition { Id = "ruins", RequiredChapterId = "ch1" });

            session.Quests.Register(new QuestDefinition
            {
                Id = "q-travel",
                Objectives = new[] { new ObjectiveDefinition("reach", ObjectiveKind.Reach, "wilds", 1) }
            });
            session.Quests.TryStart("q-travel");

            Assert.True(session.EnterRegion("wilds"));

            Assert.Equal("wilds", session.RegionId);
            Assert.Contains("wilds", session.DiscoveredRegions);
            Assert.Equal(QuestStatus.Completed, session.Quests.StatusOf("q-travel"));
        }

        [Fact]
        public void ARegionBehindAnIncompleteChapter_CannotBeEntered()
        {
            GameSession session = MakeSession();

            session.World.Register(new RegionDefinition { Id = "sanctum", RequiredChapterId = "ch1" });

            Assert.False(session.EnterRegion("sanctum"));
            Assert.DoesNotContain("sanctum", session.DiscoveredRegions);
        }

        [Fact]
        public void EnteringARegionTwice_RecordsItOnce()
        {
            GameSession session = MakeSession();
            session.World.Register(new RegionDefinition { Id = "wilds" });

            session.EnterRegion("wilds");
            session.EnterRegion("wilds");

            Assert.Single(session.DiscoveredRegions);
        }

        // --------------------------------- chapters --------------------------------

        [Fact]
        public void CompletingEveryQuestInAChapter_CompletesTheChapter()
        {
            GameSession session = MakeSession();
            RegisterHuntQuest(session);

            session.Chapters.Register(new ChapterDefinition
            {
                Id = "ch1",
                Title = "Ash and Silence",
                QuestIds = new[] { "q-hunt" }
            });

            session.Quests.TryStart("q-hunt");
            SpawnWalker(session, new Float3(0f, 0f, 1.5f));
            session.Encounter.AddDriven(
                session.Player, new[] { Swing() }, new AggressiveMeleeDriver(), isPlayer: true);
            session.Encounter.Advance(3f);

            // Completion is not enough; the chapter needs the quest handed in.
            Assert.False(session.Chapters.IsComplete("ch1"));

            session.Quests.TryTurnIn("q-hunt", out _);
            session.Chapters.Refresh();

            Assert.True(session.Chapters.IsComplete("ch1"));
            Assert.True(session.Chapters.IsStoryComplete);
        }

        // ----------------------------------- saving --------------------------------

        [Fact]
        public void APlayedSession_RoundTripsThroughASave()
        {
            GameSession session = MakeSession();
            RegisterHuntQuest(session);
            session.Quests.TryStart("q-hunt");
            session.World.Register(new RegionDefinition { Id = "wilds" });
            session.EnterRegion("wilds");

            SpawnWalker(session, new Float3(0f, 0f, 1.5f));
            session.Encounter.AddDriven(
                session.Player, new[] { Swing() }, new AggressiveMeleeDriver(), isPlayer: true);
            session.Encounter.Advance(3f);

            session.GrantItem("ash", 7);

            SaveGame save = session.CreateSave();
            string json = SaveSerializer.Serialize(save);

            // Load into a brand new session, as a fresh launch would.
            GameSession restored = MakeSession();
            RegisterHuntQuest(restored);
            restored.World.Register(new RegionDefinition { Id = "wilds" });

            SaveGame loaded = SaveSerializer.Deserialize(json);
            restored.ApplySave(loaded);

            Assert.Equal(session.RegionId, restored.RegionId);
            Assert.Equal(session.Progression.TotalExperience, restored.Progression.TotalExperience);
            Assert.Equal(session.Progression.Level, restored.Progression.Level);
            Assert.Equal(session.Inventory.Count("ash"), restored.Inventory.Count("ash"));
            Assert.Equal(session.Quests.StatusOf("q-hunt"), restored.Quests.StatusOf("q-hunt"));
            Assert.Contains("wilds", restored.DiscoveredRegions);
        }

        [Fact]
        public void LoadingASave_RestoresThePlayerToFullHealthAtTheSavedPosition()
        {
            GameSession session = MakeSession();
            session.Player.ApplyKnockback(new Float3(1f, 0f, 0f), 5f);
            session.Player.Vitals.ApplyDamage(200f, null);

            SaveGame save = session.CreateSave();

            GameSession restored = MakeSession();
            restored.ApplySave(save);

            Assert.Equal(500f, restored.Player.Vitals.Health, 2);
            Assert.Equal(save.Position, restored.Player.Position);
        }

        [Fact]
        public void LoadingASave_DoesNotDoubleApplyLevelGrowth()
        {
            // Growth is applied as a difference, so a load after the character has
            // already levelled must not count those levels a second time.
            GameSession session = MakeSession();
            session.Progression.AddExperience(100000);
            int level = session.Progression.Level;
            float expectedHealth = session.Player.Vitals.MaxHealth;

            SaveGame save = session.CreateSave();

            session.ApplySave(save);

            Assert.Equal(level, session.Progression.Level);
            Assert.Equal(expectedHealth, session.Player.Vitals.MaxHealth, 2);
        }

        [Fact]
        public void LoadingASave_RestoresTheGeneratorSoRollsContinue()
        {
            GameSession session = MakeSession();

            // Move the sequence off its starting point first.
            session.Rng.NextUInt();

            SaveGame save = session.CreateSave();

            // The value the ORIGINAL session would produce next.
            uint expectedNext = session.Rng.NextUInt();

            GameSession restored = MakeSession();
            restored.ApplySave(save);

            Assert.Equal(save.RngState, restored.Rng.State);
            Assert.Equal(save.RngIncrement, restored.Rng.Increment);
            Assert.Equal(expectedNext, restored.Rng.NextUInt());
        }

        [Fact]
        public void LootAndCombatShareOneGeneratorStream()
        {
            // Two separate streams would mean the saved state described only one
            // of them, so a load could replay values the other had consumed.
            GameSession session = MakeSession();

            Assert.Same(session.Encounter.Rng, session.Rng);
        }

        [Fact]
        public void CreatingASave_RecordsOnlyStartedQuests()
        {
            GameSession session = MakeSession();
            RegisterHuntQuest(session);
            session.Quests.Register(new QuestDefinition
            {
                Id = "q-later",
                Objectives = new[] { new ObjectiveDefinition("k", ObjectiveKind.Kill, "x", 1) }
            });

            session.Quests.TryStart("q-hunt");

            SaveGame save = session.CreateSave();

            // An untouched quest is not worth storing, and storing it would
            // freeze its status against future content changes.
            Assert.Single(save.Quests);
            Assert.Equal("q-hunt", save.Quests[0].QuestId);
        }

        [Fact]
        public void ApplySave_OfAnEmptySave_LeavesAFreshSessionPlayable()
        {
            GameSession session = MakeSession();
            var empty = new SaveGame { SlotId = "slot-1" };

            session.ApplySave(empty);

            Assert.Equal(1, session.Progression.Level);
            Assert.Equal(0, session.Inventory.UsedSlots);
            Assert.True(session.Player.IsAlive);
        }

        [Fact]
        public void ApplySave_RejectsNull()
        {
            GameSession session = MakeSession();

            Assert.Throws<System.ArgumentNullException>(() => session.ApplySave(null));
        }

        // --------------------------------- utilities -------------------------------

        [Fact]
        public void Update_AdvancesOnlyWhenTimeElapses()
        {
            GameSession session = MakeSession();

            Assert.Equal(0, session.Update(0f));
            Assert.Equal(1, session.Update(1f / 60f));
            Assert.True(session.PlaytimeSeconds > 0f);
        }

        [Fact]
        public void EncounterCleared_IsFalseWhileHostilesRemain()
        {
            GameSession session = MakeSession();
            SpawnWalker(session, new Float3(0f, 0f, 30f));

            Assert.False(session.EncounterCleared);
        }

        [Fact]
        public void SetEncounter_RewiresRewardDelivery()
        {
            // If the death subscription were not moved to the new encounter, a
            // region change would silently stop granting rewards.
            GameSession session = MakeSession();
            RegisterHuntQuest(session);
            session.Quests.TryStart("q-hunt");

            var replacement = new EncounterSimulation(new DeterministicRng(5), WorldBounds.Square(50f));
            session.SetEncounter(replacement);

            Combatant walker = SpawnWalker(session, new Float3(0f, 0f, 1.5f));

            var damage = new DamageResult(1000f, 1000f, 1000f, false, 0f);
            walker.ReceiveDamage(damage, session.Player);

            Assert.Equal(50, session.Progression.TotalExperience);
            Assert.Equal(QuestStatus.Completed, session.Quests.StatusOf("q-hunt"));
        }
    }
}
