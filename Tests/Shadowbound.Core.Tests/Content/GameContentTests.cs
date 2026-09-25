using System.Collections.Generic;
using Shadowbound.Core.Combat;
using Shadowbound.Core.Content;
using Shadowbound.Core.Items;
using Shadowbound.Core.Numerics;
using Shadowbound.Core.Progression;
using Shadowbound.Core.Quests;
using Shadowbound.Core.Randomness;
using Shadowbound.Core.Simulation;
using Shadowbound.Core.Stats;
using Shadowbound.Core.World;
using Xunit;

namespace Shadowbound.Core.Tests.Content
{
    /// <summary>
    /// Lints the authored content.
    ///
    /// Content mistakes are the ones that survive compilation and only surface
    /// late: a loot table naming an item that does not exist silently drops
    /// nothing, and a chapter listing a quest with a typo can never be completed.
    /// Both are invisible until a player reaches that exact point, so they are
    /// checked here instead.
    /// </summary>
    public class GameContentTests
    {
        private static readonly ItemDatabase Items = GameContent.BuildItems();

        [Fact]
        public void EveryItemIdInTheDatabase_IsUsableAndWellFormed()
        {
            foreach (ItemDefinition item in Items.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(item.Id));
                Assert.False(string.IsNullOrWhiteSpace(item.DisplayName), item.Id + " has no display name.");
                Assert.True(item.MaxStack >= 1, item.Id + " has an invalid stack size.");

                if (item.IsEquippable)
                {
                    Assert.NotNull(item.Slot);
                    Assert.True(item.Modifiers.Length > 0, item.Id + " is equippable but changes nothing.");
                }
            }
        }

        [Fact]
        public void EveryLootEntry_NamesARealItem()
        {
            foreach (KeyValuePair<string, LootTable> entry in GameContent.BuildLootTables())
            {
                LootTable table = entry.Value;

                Assert.True(Items.Contains(table.Id) == false, "Loot table ids and item ids must not collide.");

                foreach (LootEntry drop in table.Guaranteed)
                {
                    Assert.True(Items.Contains(drop.ItemId),
                        "Loot table '" + table.Id + "' guarantees '" + drop.ItemId + "', which is not an item.");
                    Assert.True(drop.MinQuantity <= drop.MaxQuantity);
                }

                foreach (LootEntry drop in table.Weighted)
                {
                    Assert.True(Items.Contains(drop.ItemId),
                        "Loot table '" + table.Id + "' can drop '" + drop.ItemId + "', which is not an item.");
                    Assert.True(drop.Weight > 0f, "A zero-weight entry can never drop and is dead content.");
                }
            }
        }

        [Fact]
        public void EveryLootTable_CanYieldSomething()
        {
            foreach (KeyValuePair<string, LootTable> entry in GameContent.BuildLootTables())
            {
                LootTable table = entry.Value;
                Assert.True(
                    table.Guaranteed.Length > 0 || table.MaxRolls > 0,
                    "Loot table '" + table.Id + "' can never produce a drop.");
            }
        }

        [Fact]
        public void EveryEnemy_NamesARealLootTable()
        {
            Dictionary<string, LootTable> tables = GameContent.BuildLootTables();

            foreach (EnemyArchetype archetype in GameContent.BuildEnemyArchetypes())
            {
                Assert.False(string.IsNullOrWhiteSpace(archetype.Id));
                Assert.False(string.IsNullOrWhiteSpace(archetype.DisplayName));
                Assert.True(archetype.MaxHealth > 0f);
                Assert.True(archetype.AttackAbilityIndex >= 0);
                Assert.True(
                    archetype.AttackAbilityIndex < archetype.Abilities.Count,
                    archetype.Id + " attacks with an ability index it does not have.");

                if (!string.IsNullOrEmpty(archetype.LootTableId))
                {
                    Assert.True(tables.ContainsKey(archetype.LootTableId),
                        archetype.Id + " points at loot table '" + archetype.LootTableId + "', which does not exist.");
                }
            }
        }

        [Fact]
        public void EveryEnemyAbility_IsActuallyUsable()
        {
            foreach (EnemyArchetype archetype in GameContent.BuildEnemyArchetypes())
            {
                foreach (AbilityDefinition ability in archetype.Abilities)
                {
                    Assert.False(string.IsNullOrWhiteSpace(ability.Id));

                    // An enemy with no stamina regeneration would stall after its
                    // first swing if any ability cost anything.
                    Assert.True(
                        ability.StaminaCost <= 100f,
                        archetype.Id + "'s ability '" + ability.Id + "' costs more stamina than an enemy has.");
                }
            }
        }

        [Fact]
        public void ArchetypeInstances_DoNotShareMutableState()
        {
            // Three Hollow Walkers must be three independent creatures.
            EnemyArchetype walker = GameContent.BuildHollowWalker();

            Combatant first = walker.Create("a", Float3.Zero);
            Combatant second = walker.Create("b", Float3.Zero);

            first.Vitals.ApplyDamage(50f, null);
            first.Resistances.Set(DamageType.Ember, 0.9f);

            Assert.Equal(walker.MaxHealth, second.Vitals.Health, 2);
            Assert.True(second.Resistances.Get(DamageType.Ember) < 0.9f);
        }

        [Fact]
        public void ArchetypeInstances_CarryTheirIdentity()
        {
            Combatant walker = GameContent.BuildHollowWalker().Create("walker-1", new Float3(1f, 0f, 2f));

            Assert.Equal(GameContent.ArchetypeHollowWalker, walker.ArchetypeId);
            Assert.Equal(Faction.Hostile, walker.Faction);
            Assert.True(walker.ExperienceReward > 0);
            Assert.Equal(new Float3(1f, 0f, 2f), walker.Position);
        }

        [Fact]
        public void HigherLevelPlacements_AreTougherThanTheBaseline()
        {
            EnemyArchetype walker = GameContent.BuildHollowWalker();

            Combatant baseline = walker.Create("a", Float3.Zero);
            Combatant veteran = walker.Create("b", Float3.Zero, levelOverride: 8);

            Assert.True(veteran.Vitals.MaxHealth > baseline.Vitals.MaxHealth);
            Assert.True(veteran.Stats.Get(StatId.AttackPower) > baseline.Stats.Get(StatId.AttackPower));
        }

        [Fact]
        public void ThePlayerStartsWithAPlayableKit()
        {
            Combatant player = GameContent.CreatePlayer();
            List<AbilityDefinition> abilities = GameContent.BuildPlayerAbilities();

            Assert.True(player.Vitals.IsAlive);
            Assert.True(player.Vitals.Health > 0f);
            Assert.True(player.Stats.Get(StatId.MoveSpeed) > 0f);

            Assert.True(abilities.Count >= 4, "The Warden needs more than a basic attack.");

            // Index 0 is the basic attack and must be affordable repeatedly.
            Assert.Equal(AbilityKind.Melee, abilities[0].Kind);

            foreach (AbilityDefinition ability in abilities)
            {
                Assert.True(
                    ability.StaminaCost <= player.Vitals.MaxStamina,
                    "Ability '" + ability.Id + "' costs more stamina than the player has.");
            }

            // At least one option must answer armour and one must answer distance.
            bool hasShadow = false;
            bool hasMobility = false;

            foreach (AbilityDefinition ability in abilities)
            {
                hasShadow |= ability.DamageType == DamageType.Shadow;
                hasMobility |= ability.Kind == AbilityKind.Dash;
            }

            Assert.True(hasShadow, "The kit needs an Umbra answer.");
            Assert.True(hasMobility, "The kit needs a way out.");
        }

        [Fact]
        public void OnlyTheHeaviestAbility_CostsTheCasterWithSelfStagger()
        {
            // Self-stagger is the price of a heavy blow. If a cheap ability also
            // charged it, the basic attack would feel unresponsive.
            List<AbilityDefinition> abilities = GameContent.BuildPlayerAbilities();

            Assert.True(abilities[0].SelfStaggerSeconds <= 0f, "The basic attack must not stagger its own user.");

            bool anySelfStagger = false;
            foreach (AbilityDefinition ability in abilities)
            {
                anySelfStagger |= ability.SelfStaggerSeconds > 0f;
            }

            Assert.True(anySelfStagger, "At least one ability should carry a commitment cost.");
        }

        [Fact]
        public void EveryQuestObjective_NamesSomethingThatExists()
        {
            var regionIds = new HashSet<string>();
            foreach (RegionDefinition region in GameContent.BuildRegions())
            {
                regionIds.Add(region.Id);
            }

            var archetypeIds = new HashSet<string>();
            foreach (EnemyArchetype archetype in GameContent.BuildEnemyArchetypes())
            {
                archetypeIds.Add(archetype.Id);
            }

            foreach (QuestDefinition quest in GameContent.BuildQuests())
            {
                Assert.False(string.IsNullOrWhiteSpace(quest.Id));
                Assert.False(string.IsNullOrWhiteSpace(quest.Title));
                Assert.True(quest.Objectives.Length > 0, quest.Id + " has no objectives.");
                Assert.True(quest.RequiredObjectiveCount > 0, quest.Id + " has only optional objectives.");

                foreach (ObjectiveDefinition objective in quest.Objectives)
                {
                    Assert.False(string.IsNullOrWhiteSpace(objective.Id));
                    Assert.True(objective.RequiredCount >= 1);

                    switch (objective.Kind)
                    {
                        case ObjectiveKind.Kill:
                        case ObjectiveKind.DefeatBoss:
                            Assert.True(archetypeIds.Contains(objective.TargetId),
                                quest.Id + " targets creature '" + objective.TargetId + "', which does not exist.");
                            break;

                        case ObjectiveKind.Reach:
                            Assert.True(regionIds.Contains(objective.TargetId),
                                quest.Id + " reaches region '" + objective.TargetId + "', which does not exist.");
                            break;

                        case ObjectiveKind.Collect:
                            Assert.True(Items.Contains(objective.TargetId),
                                quest.Id + " collects '" + objective.TargetId + "', which is not an item.");
                            break;
                    }
                }
            }
        }

        [Fact]
        public void EveryPrerequisiteQuest_Exists()
        {
            List<QuestDefinition> quests = GameContent.BuildQuests();
            var byId = new Dictionary<string, QuestDefinition>();

            foreach (QuestDefinition quest in quests)
            {
                byId[quest.Id] = quest;
            }

            foreach (QuestDefinition quest in quests)
            {
                foreach (string prerequisite in quest.PrerequisiteQuestIds)
                {
                    Assert.True(byId.ContainsKey(prerequisite),
                        quest.Id + " requires '" + prerequisite + "', which does not exist.");
                }
            }
        }

        [Fact]
        public void TheQuestChain_ContainsNoCycles()
        {
            // A cycle would make the quests permanently unstartable, with no error
            // and nothing to see until someone played to that point.
            List<QuestDefinition> quests = GameContent.BuildQuests();
            var byId = new Dictionary<string, QuestDefinition>();

            foreach (QuestDefinition quest in quests)
            {
                byId[quest.Id] = quest;
            }

            var visiting = new HashSet<string>();
            var done = new HashSet<string>();

            foreach (QuestDefinition quest in quests)
            {
                Assert.False(
                    HasPrerequisiteCycle(quest.Id, byId, visiting, done),
                    "The quest chain contains a cycle involving '" + quest.Id + "'.");
            }
        }

        private static bool HasPrerequisiteCycle(
            string questId,
            Dictionary<string, QuestDefinition> byId,
            HashSet<string> visiting,
            HashSet<string> done)
        {
            if (done.Contains(questId))
            {
                return false;
            }

            if (!visiting.Add(questId))
            {
                return true;
            }

            foreach (string prerequisite in byId[questId].PrerequisiteQuestIds)
            {
                if (byId.ContainsKey(prerequisite)
                    && HasPrerequisiteCycle(prerequisite, byId, visiting, done))
                {
                    return true;
                }
            }

            visiting.Remove(questId);
            done.Add(questId);
            return false;
        }

        [Fact]
        public void EveryQuest_IsReachableInStoryOrder()
        {
            // A quest nobody depends on and that depends on nothing would never be
            // offered, which is the quiet way to write content players never see.
            List<QuestDefinition> quests = GameContent.BuildQuests();
            var reachable = new HashSet<string> { GameContent.QuestArrival };

            bool changed = true;
            while (changed)
            {
                changed = false;

                foreach (QuestDefinition candidate in quests)
                {
                    if (reachable.Contains(candidate.Id))
                    {
                        continue;
                    }

                    for (int i = 0; i < candidate.PrerequisiteQuestIds.Length; i++)
                    {
                        if (reachable.Contains(candidate.PrerequisiteQuestIds[i]))
                        {
                            reachable.Add(candidate.Id);
                            changed = true;
                            break;
                        }
                    }
                }
            }

            Assert.Equal(quests.Count, reachable.Count);
            Assert.Contains(GameContent.QuestSentinel, reachable);
        }

        [Fact]
        public void EveryChapter_ListsRealQuestsAndRealChapters()
        {
            List<QuestDefinition> quests = GameContent.BuildQuests();
            List<ChapterDefinition> chapters = GameContent.BuildChapters();

            var questIds = new HashSet<string>();
            foreach (QuestDefinition quest in quests)
            {
                questIds.Add(quest.Id);
            }

            var chapterIds = new HashSet<string>();
            foreach (ChapterDefinition chapter in chapters)
            {
                chapterIds.Add(chapter.Id);
            }

            foreach (ChapterDefinition chapter in chapters)
            {
                Assert.False(string.IsNullOrWhiteSpace(chapter.Title));
                Assert.True(chapter.QuestIds.Length > 0, chapter.Id + " has no quests.");

                foreach (string questId in chapter.QuestIds)
                {
                    Assert.True(questIds.Contains(questId),
                        chapter.Id + " lists quest '" + questId + "', which does not exist.");
                }

                foreach (string required in chapter.RequiredChapterIds)
                {
                    Assert.True(chapterIds.Contains(required),
                        chapter.Id + " requires chapter '" + required + "', which does not exist.");
                }
            }

            // Every quest must belong to a chapter, or it would never gate anything.
            foreach (QuestDefinition quest in quests)
            {
                Assert.True(chapterIds.Contains(quest.ChapterId),
                    quest.Id + " belongs to chapter '" + quest.ChapterId + "', which does not exist.");
            }
        }

        [Fact]
        public void EveryRegion_NamesRealChaptersAndCreatures()
        {
            var chapterIds = new HashSet<string>();
            foreach (ChapterDefinition chapter in GameContent.BuildChapters())
            {
                chapterIds.Add(chapter.Id);
            }

            var archetypeIds = new HashSet<string>();
            foreach (EnemyArchetype archetype in GameContent.BuildEnemyArchetypes())
            {
                archetypeIds.Add(archetype.Id);
            }

            var lootTableIds = new HashSet<string>();
            foreach (KeyValuePair<string, LootTable> table in GameContent.BuildLootTables())
            {
                lootTableIds.Add(table.Key);
            }

            foreach (RegionDefinition region in GameContent.BuildRegions())
            {
                Assert.False(string.IsNullOrWhiteSpace(region.DisplayName));

                if (!string.IsNullOrEmpty(region.RequiredChapterId))
                {
                    Assert.True(chapterIds.Contains(region.RequiredChapterId),
                        region.Id + " requires chapter '" + region.RequiredChapterId + "', which does not exist.");
                }

                foreach (string encounter in region.EncounterIds)
                {
                    Assert.True(archetypeIds.Contains(encounter),
                        region.Id + " spawns '" + encounter + "', which is not a creature.");
                }

                if (!string.IsNullOrEmpty(region.LootTableId))
                {
                    Assert.True(lootTableIds.Contains(region.LootTableId),
                        region.Id + " points at loot table '" + region.LootTableId + "', which does not exist.");
                }
            }
        }

        [Fact]
        public void TheWorld_IsFullyConnected()
        {
            // An isolated region can be authored but never reached, which is a
            // silent hole in the map.
            List<RegionDefinition> regions = GameContent.BuildRegions();

            var graph = new WorldGraph();
            graph.RegisterRange(regions);

            for (int i = 0; i < regions.Count; i++)
            {
                Assert.True(
                    graph.Distance(GameContent.RegionCamp, regions[i].Id) >= 0,
                    "Region '" + regions[i].Id + "' cannot be reached from the starting camp.");
            }
        }

        [Fact]
        public void EveryRegionBehindAChapter_IsUnlockedEventually()
        {
            // Walk the intended story order and confirm each gated region opens.
            var log = new QuestLog();
            log.RegisterRange(GameContent.BuildQuests());

            var chapters = new ChapterTracker(log);
            chapters.RegisterRange(GameContent.BuildChapters());

            var graph = new WorldGraph();
            graph.RegisterRange(GameContent.BuildRegions());

            Assert.Equal(AccessFailure.None, graph.CanEnter(GameContent.RegionWilds, chapters));
            Assert.Equal(AccessFailure.ChapterIncomplete, graph.CanEnter(GameContent.RegionRuins, chapters));

            CompleteQuest(log, GameContent.QuestArrival);
            CompleteQuest(log, GameContent.QuestFirstBlood);
            chapters.Refresh();

            Assert.Equal(AccessFailure.None, graph.CanEnter(GameContent.RegionRuins, chapters));
            Assert.Equal(AccessFailure.ChapterIncomplete, graph.CanEnter(GameContent.RegionSanctum, chapters));

            CompleteQuest(log, GameContent.QuestDescent);
            CompleteQuest(log, GameContent.QuestSplinters);
            chapters.Refresh();

            Assert.Equal(AccessFailure.None, graph.CanEnter(GameContent.RegionSanctum, chapters));
            Assert.Equal(AccessFailure.None, graph.CanEnter(GameContent.RegionWard, chapters));
            Assert.False(chapters.IsStoryComplete);

            CompleteQuest(log, GameContent.QuestSentinel);
            chapters.Refresh();

            Assert.True(chapters.IsStoryComplete, "The story should be completable end to end.");
        }

        private static void CompleteQuest(QuestLog log, string questId)
        {
            Assert.True(log.IsStartable(questId), "Quest '" + questId + "' was not startable in story order.");
            Assert.True(log.TryStart(questId));

            QuestState state = log.State(questId);
            foreach (ObjectiveDefinition objective in state.Definition.Objectives)
            {
                if (objective.Kind == ObjectiveKind.Collect)
                {
                    for (int i = 0; i < objective.RequiredCount; i++)
                    {
                        log.SetProgress(questId, objective.Id, i + 1);
                    }
                }
                else
                {
                    // Routed through the log rather than the quest state, because
                    // the log is what owns the transition to Completed. Advancing
                    // a state directly would move the progress bar without ever
                    // marking the quest done.
                    log.Report(new QuestEvent(objective.Kind, objective.TargetId, objective.RequiredCount));
                }
            }

            Assert.Equal(QuestStatus.Completed, log.StatusOf(questId));
            Assert.True(log.TryTurnIn(questId, out _));
        }

        [Fact]
        public void EveryReward_CanActuallyBeGranted()
        {
            foreach (QuestDefinition quest in GameContent.BuildQuests())
            {
                ItemStack[] items = quest.Rewards.Items;
                if (items == null)
                {
                    continue;
                }

                foreach (ItemStack stack in items)
                {
                    Assert.True(Items.Contains(stack.ItemId),
                        quest.Id + " rewards '" + stack.ItemId + "', which is not an item.");
                    Assert.True(stack.Quantity > 0);
                }
            }
        }

        [Fact]
        public void ThePlayerCanCarryEveryQuestReward()
        {
            // A collection objective whose target cannot be stacked to its required
            // count would be impossible to complete.
            foreach (QuestDefinition quest in GameContent.BuildQuests())
            {
                foreach (ObjectiveDefinition objective in quest.Objectives)
                {
                    if (objective.Kind != ObjectiveKind.Collect)
                    {
                        continue;
                    }

                    ItemDefinition item = Items.Get(objective.TargetId);
                    Assert.True(
                        item.MaxStack >= objective.RequiredCount,
                        quest.Id + " needs " + objective.RequiredCount + " of '" + objective.TargetId
                        + "', but that item stacks only to " + item.MaxStack + ".");
                }
            }
        }

        [Fact]
        public void EveryBossObjective_TargetsSomethingMarkedAsABoss()
        {
            // Otherwise the objective could never be satisfied, because only bosses
            // raise a DefeatBoss event.
            foreach (QuestDefinition quest in GameContent.BuildQuests())
            {
                foreach (ObjectiveDefinition objective in quest.Objectives)
                {
                    if (objective.Kind != ObjectiveKind.DefeatBoss)
                    {
                        continue;
                    }

                    EnemyArchetype archetype = GameContent.FindArchetype(objective.TargetId);
                    Assert.NotNull(archetype);
                    Assert.True(archetype.IsBoss,
                        quest.Id + " requires defeating '" + objective.TargetId + "', which is not marked as a boss.");
                }
            }
        }

        [Fact]
        public void Populate_InstallsEverythingASessionNeeds()
        {
            var items = GameContent.BuildItems();
            Combatant player = GameContent.CreatePlayer();

            var session = new GameSession(
                player,
                items,
                new ExperienceCurve(),
                GameContent.BuildPlayerGrowth(),
                new DeterministicRng(1));

            GameContent.Populate(session);

            Assert.Equal(GameContent.BuildQuests().Count, session.Quests.Count);
            Assert.Equal(GameContent.BuildChapters().Count, session.Chapters.Count);
            Assert.Equal(GameContent.BuildRegions().Count, session.World.Count);
            Assert.Equal(GameContent.RegionCamp, session.RegionId);

            // The first quest must be startable immediately, or the game opens with
            // nothing to do.
            Assert.True(session.Quests.IsStartable(GameContent.QuestArrival));
        }
    }
}
