using System;
using System.Collections.Generic;
using Shadowbound.Core.Randomness;

namespace Shadowbound.Core.Items
{
    /// <summary>One possible drop in a table.</summary>
    public sealed class LootEntry
    {
        public string ItemId = "";
        public float Weight = 1f;
        public int MinQuantity = 1;
        public int MaxQuantity = 1;

        /// <summary>Rolls below this character level are ignored. Zero means always eligible.</summary>
        public int MinimumLevel;

        public LootEntry()
        {
        }

        public LootEntry(string itemId, float weight, int minQuantity = 1, int maxQuantity = 1)
        {
            ItemId = itemId;
            Weight = weight;
            MinQuantity = minQuantity;
            MaxQuantity = maxQuantity;
        }
    }

    /// <summary>
    /// A weighted drop table.
    ///
    /// Guaranteed entries always drop, and the weighted entries supply the
    /// variable part. This mirrors how encounter rewards are usually authored:
    /// the quest requires three hides, so three hides are guaranteed, and the
    /// interest comes from what else falls out alongside them.
    ///
    /// Rolls are driven entirely by a supplied generator, so a table produces a
    /// reproducible result for a given seed. That is what makes loot testable and
    /// what allows a save file to be replayed.
    /// </summary>
    public sealed class LootTable
    {
        public string Id = "";

        /// <summary>Always awarded, subject to level eligibility.</summary>
        public LootEntry[] Guaranteed = Array.Empty<LootEntry>();

        /// <summary>Chosen by weight, without replacement, up to <see cref="MaxRolls"/>.</summary>
        public LootEntry[] Weighted = Array.Empty<LootEntry>();

        public int MinRolls = 1;
        public int MaxRolls = 1;

        /// <summary>Chance that the weighted rolls yield nothing at all.</summary>
        public float NoDropChance;

        /// <summary>
        /// Chance per point of luck of gaining one extra weighted roll. A luck of
        /// 0.5 therefore gives a 50% chance of one additional roll.
        /// </summary>
        public float ExtraRollChancePerLuck = 0.5f;

        public int MaxExtraRolls = 2;

        /// <summary>Number of weighted rolls awarded for a given luck value.</summary>
        public int ResolveRollCount(DeterministicRng rng, float luck)
        {
            int rolls = 0;

            int span = MaxRolls - MinRolls;
            if (span > 0)
            {
                rolls = MinRolls + (rng == null ? 0 : rng.Range(0, span + 1));
            }
            else
            {
                rolls = MinRolls < 0 ? 0 : MinRolls;
            }

            if (luck > 0f && rng != null && ExtraRollChancePerLuck > 0f)
            {
                for (int i = 0; i < MaxExtraRolls; i++)
                {
                    if (rng.Chance(luck * ExtraRollChancePerLuck))
                    {
                        rolls++;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            return rolls;
        }

        public bool ResolveNoDrop(DeterministicRng rng)
        {
            return NoDropChance > 0f && rng != null && rng.Chance(NoDropChance);
        }

        /// <summary>
        /// Rolls the table into <paramref name="results"/>, which is cleared
        /// first. Duplicate items are merged into a single stack so the caller
        /// receives a tidy list. Returns the number of stacks produced.
        /// </summary>
        public int Roll(DeterministicRng rng, int characterLevel, float luck, List<ItemStack> results)
        {
            if (results == null)
            {
                return 0;
            }

            results.Clear();

            if (rng == null)
            {
                return 0;
            }

            for (int i = 0; i < Guaranteed.Length; i++)
            {
                LootEntry entry = Guaranteed[i];
                if (!IsEligible(entry, characterLevel))
                {
                    continue;
                }

                int quantity = RollQuantity(rng, entry);
                if (quantity > 0)
                {
                    Merge(results, entry.ItemId, quantity);
                }
            }

            if (ResolveNoDrop(rng))
            {
                return results.Count;
            }

            int rolls = ResolveRollCount(rng, luck);
            if (rolls <= 0)
            {
                return results.Count;
            }

            // Weighted entries are drawn without replacement so that a single
            // roll set cannot hand out the same rare twice, which would make
            // "one roll" meaningless. Quantities of the same item still stack.
            var pool = new List<LootEntry>(Weighted.Length);
            CollectEligible(Weighted, characterLevel, pool);

            for (int i = 0; i < rolls && pool.Count > 0; i++)
            {
                int index = PickWeighted(pool, rng);
                if (index < 0)
                {
                    break;
                }

                LootEntry chosen = pool[index];
                Merge(results, chosen.ItemId, RollQuantity(rng, chosen));
                pool.RemoveAt(index);
            }

            return results.Count;
        }

        /// <summary>
        /// Picks an index from a weighted pool, or -1 when every weight is zero.
        ///
        /// Uses a cumulative sum over the full weight so that each entry's chance
        /// is exactly proportional to its weight. An integer-modulo shortcut
        /// would bias the low end of the table, which is where rare items usually
        /// sit.
        /// </summary>
        public static int PickWeighted(IReadOnlyList<LootEntry> pool, DeterministicRng rng)
        {
            if (pool == null || pool.Count == 0)
            {
                return -1;
            }

            float total = 0f;
            for (int i = 0; i < pool.Count; i++)
            {
                float weight = pool[i].Weight;
                if (weight > 0f)
                {
                    total += weight;
                }
            }

            if (total <= 0f)
            {
                return -1;
            }

            float roll = rng.Range(0f, total);
            float cumulative = 0f;

            for (int i = 0; i < pool.Count; i++)
            {
                float weight = pool[i].Weight;
                if (weight <= 0f)
                {
                    continue;
                }

                cumulative += weight;
                if (roll < cumulative)
                {
                    return i;
                }
            }

            // Floating point can leave the last weight just short of the roll.
            for (int i = pool.Count - 1; i >= 0; i--)
            {
                if (pool[i].Weight > 0f)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsEligible(LootEntry entry, int characterLevel)
        {
            if (entry == null || string.IsNullOrEmpty(entry.ItemId) || entry.Weight < 0f)
            {
                return false;
            }

            return characterLevel >= entry.MinimumLevel;
        }

        private static void CollectEligible(LootEntry[] entries, int characterLevel, List<LootEntry> into)
        {
            into.Clear();

            for (int i = 0; i < entries.Length; i++)
            {
                if (IsEligible(entries[i], characterLevel) && entries[i].Weight > 0f)
                {
                    into.Add(entries[i]);
                }
            }
        }

        private static int RollQuantity(DeterministicRng rng, LootEntry entry)
        {
            int min = entry.MinQuantity < 1 ? 1 : entry.MinQuantity;
            int max = entry.MaxQuantity < min ? min : entry.MaxQuantity;
            return max == min ? min : rng.Range(min, max + 1);
        }

        private static void Merge(List<ItemStack> results, string itemId, int quantity)
        {
            if (quantity <= 0)
            {
                return;
            }

            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].ItemId == itemId)
                {
                    ItemStack existing = results[i];
                    existing.Quantity += quantity;
                    results[i] = existing;
                    return;
                }
            }

            results.Add(new ItemStack(itemId, quantity));
        }
    }
}
