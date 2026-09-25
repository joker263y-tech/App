using System.Collections.Generic;
using Shadowbound.Core.Combat;
using Shadowbound.Core.Content;
using Shadowbound.Core.Items;
using Shadowbound.Core.Numerics;
using Shadowbound.Core.Quests;
using Shadowbound.Core.World;
using Xunit;

namespace Shadowbound.Core.Tests.Content
{
    /// <summary>
    /// Tests the content validator.
    ///
    /// A validator that reports "clean" is worthless unless it is also proven to
    /// fail when something is wrong. Every test below injects one specific fault
    /// into real shipped content and asserts the validator names it - which is what
    /// makes the clean result on the shipped content mean something.
    /// </summary>
    public class ContentValidatorTests
    {
        /// <summary>A full set of shipped content, mutable so faults can be injected.</summary>
        private sealed class Shipped
        {
            public ItemDatabase Items;
            public List<EnemyArchetype> Archetypes;
            public Dictionary<string, LootTable> LootTables;
            public List<QuestDefinition> Quests;
            public List<ChapterDefinition> Chapters;
            public List<RegionDefinition> Regions;
            public List<AbilityDefinition> Abilities;

            public static Shipped Build()
            {
                return new Shipped
                {
                    Items = GameContent.BuildItems(),
                    Archetypes = GameContent.BuildEnemyArchetypes(),
                    LootTables = GameContent.BuildLootTables(),
                    Quests = GameContent.BuildQuests(),
                    Chapters = GameContent.BuildChapters(),
                    Regions = GameContent.BuildRegions(),
                    Abilities = GameContent.BuildPlayerAbilities()
                };
            }

            public ContentReport Validate()
            {
                return ContentValidator.Validate(
                    Items, Archetypes, LootTables, Quests, Chapters, Regions, Abilities);
            }

            public int ErrorCount()
            {
                return Validate().ErrorCount;
            }
        }

        /// <summary>
        /// True when any problem names the given content, in either its location or
        /// its message. Both are searched because a human reading the report sees the
        /// rendered line, and an identifier can legitimately appear in either half.
        /// </summary>
        private static bool Mentions(ContentReport report, string fragment)
        {
            for (int i = 0; i < report.Problems.Count; i++)
            {
                if (report.Problems[i].ToString().IndexOf(fragment, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------ the shipped game ---------------------------

        [Fact]
        public void ShippedContent_ValidatesWithoutErrors()
        {
            ContentReport report = Shipped.Build().Validate();

            // A failure here means the game's own content is inconsistent, so the
            // message matters more than the count. Report every problem.
            Assert.True(
                report.IsClean,
                "Shipped content has problems:\n" + Describe(report));
        }

        [Fact]
        public void ShippedContent_ReportsCounts()
        {
            ContentReport report = Shipped.Build().Validate();

            Assert.True(report.ItemCount > 0);
            Assert.True(report.ArchetypeCount > 0);
            Assert.True(report.LootTableCount > 0);
            Assert.True(report.AbilityCount > 0);
            Assert.True(report.QuestCount > 0);
            Assert.True(report.ChapterCount > 0);
            Assert.True(report.RegionCount > 0);
        }

        [Fact]
        public void ValidateShippedContent_MatchesExplicitBuild()
        {
            // The convenience overload must not drift from the explicit one that the
            // editor tooling calls.
            ContentReport viaConvenience = ContentValidator.ValidateShippedContent();
            ContentReport viaExplicit = Shipped.Build().Validate();

            Assert.Equal(viaExplicit.ErrorCount, viaConvenience.ErrorCount);
            Assert.Equal(viaExplicit.ItemCount, viaConvenience.ItemCount);
            Assert.Equal(viaExplicit.QuestCount, viaConvenience.QuestCount);
        }

        [Fact]
        public void Summary_IsOneLine()
        {
            string summary = Shipped.Build().Validate().Summary();

            Assert.False(string.IsNullOrWhiteSpace(summary));
            Assert.DoesNotContain("\n", summary);
        }

        // --------------------------------- loot faults -----------------------------

        [Fact]
        public void LootTableNamingAMissingItem_IsReported()
        {
            Shipped content = Shipped.Build();
            LootTable table = FirstLootTable(content.LootTables);

            table.Guaranteed = new[] { new LootEntry("no-such-item", 1f) };

            ContentReport report = content.Validate();

            Assert.False(report.IsClean);
            Assert.True(Mentions(report, "no-such-item"), Describe(report));
        }

        [Fact]
        public void LootEntryWithInvalidQuantityRange_IsReported()
        {
            Shipped content = Shipped.Build();
            LootTable table = FirstLootTable(content.LootTables);

            table.Guaranteed = new[] { new LootEntry(GameContent.ItemAsh, 1f, 5, 2) };

            ContentReport report = content.Validate();

            Assert.False(report.IsClean);
            Assert.True(Mentions(report, "quantity range"), Describe(report));
        }

        [Fact]
        public void LootTableThatDropsNothing_IsAWarningNotAnError()
        {
            Shipped content = Shipped.Build();
            LootTable table = FirstLootTable(content.LootTables);

            table.Guaranteed = System.Array.Empty<LootEntry>();
            table.Weighted = System.Array.Empty<LootEntry>();

            ContentReport report = content.Validate();

            // An empty table is legal, just pointless, so it must not fail the build.
            Assert.True(report.IsClean, Describe(report));
            Assert.True(report.WarningCount > 0);
        }

        // ------------------------------ archetype faults ---------------------------

        [Fact]
        public void ArchetypeWithOutOfRangeAttackIndex_IsReported()
        {
            Shipped content = Shipped.Build();

            content.Archetypes[0].AttackAbilityIndex = 99;

            ContentReport report = content.Validate();

            Assert.False(report.IsClean);
            Assert.True(Mentions(report, "ability index 99"), Describe(report));
        }

        [Fact]
        public void ArchetypeWithNoAbilities_IsReported()
        {
            Shipped content = Shipped.Build();

            content.Archetypes[0].Abilities = new List<AbilityDefinition>();

            ContentReport report = content.Validate();

            Assert.False(report.IsClean);
            Assert.True(Mentions(report, "no abilities"), Describe(report));
        }

        [Fact]
        public void ArchetypePointingAtMissingLootTable_IsReported()
        {
            Shipped content = Shipped.Build();

            content.Archetypes[0].LootTableId = "loot-that-does-not-exist";

            ContentReport report = content.Validate();

            Assert.False(report.IsClean);
            Assert.True(Mentions(report, "loot-that-does-not-exist"), Describe(report));
        }

        [Fact]
        public void ArchetypeWithNoHealth_IsReported()
        {
            Shipped content = Shipped.Build();

            content.Archetypes[0].MaxHealth = 0f;

            ContentReport report = content.Validate();

            Assert.False(report.IsClean);
            Assert.True(Mentions(report, "spawn dead"), Describe(report));
        }

        [Fact]
        public void ArchetypeWithShortTint_IsAWarningNotAnError()
        {
            Shipped content = Shipped.Build();

            content.Archetypes[0].TintRgb = new[] { 1f };

            ContentReport report = content.Validate();

            // A malformed colour must not stop the build; the game falls back to grey.
            Assert.True(report.IsClean, Describe(report));
            Assert.True(report.WarningCount > 0);
        }

        // -------------------------------- quest faults -----------------------------

        [Fact]
        public void QuestTargetingAnUnknownCreature_IsReported()
        {
            Shipped content = Shipped.Build();

            content.Quests[0].Objectives = new[]
            {
                new ObjectiveDefinition
                {
                    Id = "kill-something-absent",
                    Kind = ObjectiveKind.Kill,
                    TargetId = "creature-that-does-not-exist",
                    RequiredCount = 1
                }
            };

            ContentReport report = content.Validate();

            Assert.False(report.IsClean);
            Assert.True(Mentions(report, "creature-that-does-not-exist"), Describe(report));
        }

        [Fact]
        public void CollectObjectiveNamingAMissingItem_IsReported()
        {
            Shipped content = Shipped.Build();

            content.Quests[0].Objectives = new[]
            {
                new ObjectiveDefinition
                {
                    Id = "collect-absent",
                    Kind = ObjectiveKind.Collect,
                    TargetId = "item-that-does-not-exist",
                    RequiredCount = 3
                }
            };

            ContentReport report = content.Validate();

            Assert.False(report.IsClean);
            Assert.True(Mentions(report, "item-that-does-not-exist"), Describe(report));
        }

        [Fact]
        public void ReachObjectiveNamingAMissingRegion_IsReported()
        {
            Shipped content = Shipped.Build();

            content.Quests[0].Objectives = new[]
            {
                new ObjectiveDefinition
                {
                    Id = "reach-absent",
                    Kind = ObjectiveKind.Reach,
                    TargetId = "region-that-does-not-exist",
                    RequiredCount = 1
                }
            };

            ContentReport report = content.Validate();

            Assert.False(report.IsClean);
            Assert.True(Mentions(report, "region-that-does-not-exist"), Describe(report));
        }

        [Fact]
        public void QuestWithNoObjectives_IsReported()
        {
            Shipped content = Shipped.Build();

            content.Quests[0].Objectives = System.Array.Empty<ObjectiveDefinition>();

            ContentReport report = content.Validate();

            Assert.False(report.IsClean);
            Assert.True(Mentions(report, "no objectives"), Describe(report));
        }

        [Fact]
        public void QuestRequiringItself_IsReported()
        {
            Shipped content = Shipped.Build();

            QuestDefinition quest = content.Quests[0];
            quest.PrerequisiteQuestIds = new[] { quest.Id };

            ContentReport report = content.Validate();

            Assert.False(report.IsClean);
            Assert.True(Mentions(report, "itself"), Describe(report));
        }

        [Fact]
        public void QuestPrerequisiteCycle_IsReported()
        {
            Shipped content = Shipped.Build();

            QuestDefinition first = content.Quests[0];
            QuestDefinition second = content.Quests[1];

            first.PrerequisiteQuestIds = new[] { second.Id };
            second.PrerequisiteQuestIds = new[] { first.Id };

            ContentReport report = content.Validate();

            Assert.False(report.IsClean);
            Assert.True(Mentions(report, "cycle"), Describe(report));
        }

        [Fact]
        public void QuestRewardingAMissingItem_IsReported()
        {
            Shipped content = Shipped.Build();

            content.Quests[0].Rewards = new QuestReward
            {
                Experience = 10,
                Items = new[] { new ItemStack("reward-that-does-not-exist", 1) }
            };

            ContentReport report = content.Validate();

            Assert.False(report.IsClean);
            Assert.True(Mentions(report, "reward-that-does-not-exist"), Describe(report));
        }

        [Fact]
        public void QuestInAMissingChapter_IsReported()
        {
            Shipped content = Shipped.Build();

            content.Quests[0].ChapterId = "chapter-that-does-not-exist";

            ContentReport report = content.Validate();

            Assert.False(report.IsClean);
            Assert.True(Mentions(report, "chapter-that-does-not-exist"), Describe(report));
        }

        // ------------------------------- chapter faults ----------------------------

        [Fact]
        public void ChapterListingAMissingQuest_IsReported()
        {
            Shipped content = Shipped.Build();

            content.Chapters[0].QuestIds = new[] { "quest-that-does-not-exist" };

            ContentReport report = content.Validate();

            Assert.False(report.IsClean);
            Assert.True(Mentions(report, "quest-that-does-not-exist"), Describe(report));
        }

        [Fact]
        public void ChapterRequiringAMissingChapter_IsReported()
        {
            Shipped content = Shipped.Build();

            content.Chapters[0].RequiredChapterIds = new[] { "chapter-that-does-not-exist" };

            ContentReport report = content.Validate();

            Assert.False(report.IsClean);
            Assert.True(Mentions(report, "chapter-that-does-not-exist"), Describe(report));
        }

        // ------------------------------- region faults -----------------------------

        [Fact]
        public void RegionConnectingToNowhere_IsReported()
        {
            Shipped content = Shipped.Build();

            content.Regions.Add(new RegionDefinition
            {
                Id = "orphan-region",
                DisplayName = "Orphan",
                Connections = new[] { "region-that-does-not-exist" }
            });

            ContentReport report = content.Validate();

            Assert.False(report.IsClean);
            Assert.True(Mentions(report, "region-that-does-not-exist"), Describe(report));
        }

        [Fact]
        public void RegionWithNoWayIn_IsReportedAsUnreachable()
        {
            Shipped content = Shipped.Build();

            // Correctly formed and fully populated, but nothing links to it. Content
            // no one can ever reach is still a bug.
            content.Regions.Add(new RegionDefinition
            {
                Id = "island-region",
                DisplayName = "Island",
                Kind = RegionKind.Ruins
            });

            ContentReport report = content.Validate();

            Assert.False(report.IsClean);
            Assert.True(Mentions(report, "island-region"), Describe(report));
        }

        [Fact]
        public void RegionRequiringAMissingChapter_IsReported()
        {
            Shipped content = Shipped.Build();

            content.Regions[0].RequiredChapterId = "chapter-that-does-not-exist";

            ContentReport report = content.Validate();

            Assert.False(report.IsClean);
            Assert.True(Mentions(report, "chapter-that-does-not-exist"), Describe(report));
        }

        [Fact]
        public void RegionPointingAtMissingLootTable_IsReported()
        {
            Shipped content = Shipped.Build();

            content.Regions[0].LootTableId = "loot-that-does-not-exist";

            ContentReport report = content.Validate();

            Assert.False(report.IsClean);
            Assert.True(Mentions(report, "loot-that-does-not-exist"), Describe(report));
        }

        // ------------------------------ ability faults -----------------------------

        [Fact]
        public void PlayerWithNoAbilities_IsReported()
        {
            Shipped content = Shipped.Build();

            content.Abilities.Clear();

            ContentReport report = content.Validate();

            Assert.False(report.IsClean);
            Assert.True(Mentions(report, "no abilities"), Describe(report));
        }

        [Fact]
        public void MeleeAbilityWithNoRange_IsReported()
        {
            Shipped content = Shipped.Build();

            AbilityDefinition ability = content.Abilities[0];
            ability.Range = 0f;

            ContentReport report = content.Validate();

            Assert.False(report.IsClean);
            Assert.True(Mentions(report, "never reach"), Describe(report));
        }

        // ------------------------------ empty content ------------------------------

        [Fact]
        public void EmptyContentSet_DoesNotThrow()
        {
            // Called before content exists, the validator must report rather than
            // crash, or a mistake in content authoring becomes a broken tool.
            ContentReport report = ContentValidator.Validate(
                null, null, null, null, null, null, null);

            Assert.False(report.IsClean);
            Assert.True(Mentions(report, "no abilities"), Describe(report));
        }

        private static LootTable FirstLootTable(Dictionary<string, LootTable> tables)
        {
            foreach (KeyValuePair<string, LootTable> entry in tables)
            {
                return entry.Value;
            }

            throw new Xunit.Sdk.XunitException("Shipped content has no loot tables.");
        }

        /// <summary>Renders every problem, so a failure states what was actually wrong.</summary>
        private static string Describe(ContentReport report)
        {
            var text = new System.Text.StringBuilder();
            text.Append(report.Summary());

            for (int i = 0; i < report.Problems.Count; i++)
            {
                text.Append('\n');
                text.Append("  ");
                text.Append(report.Problems[i].ToString());
            }

            return text.ToString();
        }
    }
}
