using System;
using System.Collections.Generic;
using Shadowbound.Core.Items;
using Shadowbound.Core.Randomness;
using Shadowbound.Core.Tests.Support;
using Xunit;

namespace Shadowbound.Core.Tests.Items
{
    public class LootTableTests
    {
        private static LootTable TwoItemTable(float commonWeight = 3f, float uncommonWeight = 1f)
        {
            return new LootTable
            {
                Id = "test",
                Guaranteed = Array.Empty<LootEntry>(),
                Weighted = new[]
                {
                    new LootEntry("common", commonWeight),
                    new LootEntry("uncommon", uncommonWeight)
                },
                MinRolls = 1,
                MaxRolls = 1
            };
        }

        [Fact]
        public void GuaranteedEntries_AlwaysDropInTheirExactQuantity()
        {
            var table = new LootTable
            {
                Guaranteed = new[] { new LootEntry("ash", 1f, 3, 3) },
                Weighted = Array.Empty<LootEntry>(),
                MinRolls = 0,
                MaxRolls = 0
            };

            var results = new List<ItemStack>();
            int count = table.Roll(new DeterministicRng(1), characterLevel: 1, luck: 0f, results: results);

            Assert.Equal(1, count);
            Assert.Equal("ash", results[0].ItemId);
            Assert.Equal(3, results[0].Quantity);
        }

        [Fact]
        public void GuaranteedEntries_AreSubjectToLevelGating()
        {
            var table = new LootTable
            {
                Guaranteed = new[] { new LootEntry("sigil", 1f, 1, 1) { MinimumLevel = 10 } },
                Weighted = Array.Empty<LootEntry>(),
                MinRolls = 0,
                MaxRolls = 0
            };

            var results = new List<ItemStack>();

            Assert.Equal(0, table.Roll(new DeterministicRng(1), 5, 0f, results));
            Assert.Equal(1, table.Roll(new DeterministicRng(1), 10, 0f, results));
        }

        [Fact]
        public void NoDropChance_SuppressesWeightedDropsButNotGuaranteedOnes()
        {
            LootTable table = TwoItemTable();
            table.NoDropChance = 1f;
            table.Guaranteed = new[] { new LootEntry("ash", 1f, 1, 1) };

            var results = new List<ItemStack>();
            table.Roll(new DeterministicRng(1), 1, 0f, results);

            Assert.Single(results);
            Assert.Equal("ash", results[0].ItemId);
        }

        [Fact]
        public void WeightedDistribution_MatchesTheAuthoredWeights()
        {
            LootTable table = TwoItemTable(commonWeight: 3f, uncommonWeight: 1f);
            var rng = new DeterministicRng(2024);
            var results = new List<ItemStack>();

            int common = 0;
            int uncommon = 0;

            for (int i = 0; i < 4000; i++)
            {
                table.Roll(rng, 1, 0f, results);
                if (results[0].ItemId == "common")
                {
                    common++;
                }
                else
                {
                    uncommon++;
                }
            }

            // Expected 75% / 25%. Bounds are wide because this asserts the
            // weighting is wired up, not that the generator is perfect.
            Assert.InRange(common, 2800, 3200);
            Assert.InRange(uncommon, 800, 1200);
        }

        [Fact]
        public void ZeroWeightEntries_AreNeverDropped()
        {
            LootTable table = TwoItemTable(0f, 0f);
            var results = new List<ItemStack>();

            int count = table.Roll(new DeterministicRng(1), 1, 0f, results);

            Assert.Equal(0, count);
            Assert.Empty(results);
        }

        [Fact]
        public void LevelGatedEntries_AreExcludedFromTheWeightedPool()
        {
            var table = new LootTable
            {
                Weighted = new[] { new LootEntry("relic", 1f) { MinimumLevel = 20 } },
                MinRolls = 1,
                MaxRolls = 1
            };

            var results = new List<ItemStack>();

            Assert.Equal(0, table.Roll(new DeterministicRng(1), characterLevel: 5, luck: 0f, results: results));
            Assert.Equal(1, table.Roll(new DeterministicRng(1), characterLevel: 20, luck: 0f, results: results));
        }

        [Fact]
        public void DuplicateDrops_MergeIntoOneStack()
        {
            var table = new LootTable
            {
                Guaranteed = new[] { new LootEntry("ash", 1f, 1, 1) },
                Weighted = new[] { new LootEntry("ash", 1f, 1, 1) },
                MinRolls = 1,
                MaxRolls = 1
            };

            var results = new List<ItemStack>();
            table.Roll(new DeterministicRng(1), 1, 0f, results);

            Assert.Single(results);
            Assert.Equal(2, results[0].Quantity);
        }

        [Fact]
        public void SameSeed_ProducesTheSameLoot()
        {
            LootTable table = TwoItemTable();
            table.MinRolls = 1;
            table.MaxRolls = 3;

            var first = new List<ItemStack>();
            var second = new List<ItemStack>();
            var rngA = new DeterministicRng(777);
            var rngB = new DeterministicRng(777);

            for (int i = 0; i < 200; i++)
            {
                table.Roll(rngA, 1, 0.5f, first);
                table.Roll(rngB, 1, 0.5f, second);

                Assert.Equal(first.Count, second.Count);
                for (int j = 0; j < first.Count; j++)
                {
                    Assert.Equal(first[j].ItemId, second[j].ItemId);
                    Assert.Equal(first[j].Quantity, second[j].Quantity);
                }
            }
        }

        [Fact]
        public void Roll_ClearsTheBufferBeforeFillingIt()
        {
            LootTable table = TwoItemTable();
            var results = new List<ItemStack> { new ItemStack("stale", 99) };

            table.Roll(new DeterministicRng(1), 1, 0f, results);

            for (int i = 0; i < results.Count; i++)
            {
                Assert.NotEqual("stale", results[i].ItemId);
            }
        }

        [Fact]
        public void RollCount_StaysWithinTheAuthoredRange()
        {
            var table = new LootTable { MinRolls = 2, MaxRolls = 4 };
            var rng = new DeterministicRng(55);

            for (int i = 0; i < 500; i++)
            {
                Assert.InRange(table.ResolveRollCount(rng, 0f), 2, 4);
            }
        }

        [Fact]
        public void Luck_GrantsExtraRolls()
        {
            var table = new LootTable
            {
                MinRolls = 1,
                MaxRolls = 1,
                ExtraRollChancePerLuck = 0.5f,
                MaxExtraRolls = 2
            };

            // A luck of 10 makes the extra-roll chance certain, so it caps out.
            Assert.Equal(3, table.ResolveRollCount(new DeterministicRng(1), 10f));
            Assert.Equal(1, table.ResolveRollCount(new DeterministicRng(1), 0f));
        }

        [Fact]
        public void ExtraRolls_AreCappedByMaxExtraRolls()
        {
            var table = new LootTable
            {
                MinRolls = 0,
                MaxRolls = 0,
                ExtraRollChancePerLuck = 1f,
                MaxExtraRolls = 5
            };

            Assert.Equal(5, table.ResolveRollCount(new DeterministicRng(1), 100f));
        }

        [Fact]
        public void Roll_WithNoGenerator_ProducesNothing()
        {
            LootTable table = TwoItemTable();
            var results = new List<ItemStack>();

            Assert.Equal(0, table.Roll(null, 1, 0f, results));
        }

        [Fact]
        public void Roll_WithANullBuffer_DoesNotThrow()
        {
            Assert.Equal(0, TwoItemTable().Roll(new DeterministicRng(1), 1, 0f, null));
        }

        // ------------------------------- weighted pick -----------------------------

        [Fact]
        public void PickWeighted_WithAnEmptyPool_ReturnsMinusOne()
        {
            Assert.Equal(-1, LootTable.PickWeighted(new List<LootEntry>(), new DeterministicRng(1)));
            Assert.Equal(-1, LootTable.PickWeighted(null, new DeterministicRng(1)));
        }

        [Fact]
        public void PickWeighted_WithAllZeroWeights_ReturnsMinusOne()
        {
            List<LootEntry> pool = ItemTestData.Entries(new LootEntry("a", 0f), new LootEntry("b", 0f));

            Assert.Equal(-1, LootTable.PickWeighted(pool, new DeterministicRng(1)));
        }

        [Fact]
        public void PickWeighted_WithASinglePositiveEntry_AlwaysReturnsIt()
        {
            List<LootEntry> pool = ItemTestData.Entries(new LootEntry("a", 0f), new LootEntry("b", 5f));
            var rng = new DeterministicRng(9);

            for (int i = 0; i < 100; i++)
            {
                Assert.Equal(1, LootTable.PickWeighted(pool, rng));
            }
        }

        [Fact]
        public void PickWeighted_NeverReturnsAnEntryOutsideThePool()
        {
            List<LootEntry> pool = ItemTestData.Entries(new LootEntry("a", 1f), new LootEntry("b", 1f));
            var rng = new DeterministicRng(3);

            for (int i = 0; i < 500; i++)
            {
                Assert.InRange(LootTable.PickWeighted(pool, rng), 0, 1);
            }
        }

        [Fact]
        public void Roll_DrawsWithoutReplacementSoARareCannotRepeatInOneDrop()
        {
            // A single roll set should not hand out the same rare twice, which
            // would make "one roll" meaningless.
            var table = new LootTable
            {
                Weighted = new[]
                {
                    new LootEntry("rare", 1f, 1, 1),
                    new LootEntry("common", 1f, 1, 1)
                },
                MinRolls = 2,
                MaxRolls = 2
            };

            var results = new List<ItemStack>();
            table.Roll(new DeterministicRng(1), 1, 0f, results);

            Assert.Equal(2, results.Count);
        }
    }
}
