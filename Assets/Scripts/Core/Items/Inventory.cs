using System;
using System.Collections.Generic;

namespace Shadowbound.Core.Items
{
    /// <summary>One stack of items, as stored in a save file and returned by loot rollers.</summary>
    public struct ItemStack
    {
        public string ItemId;
        public int Quantity;

        public ItemStack(string itemId, int quantity)
        {
            ItemId = itemId;
            Quantity = quantity < 0 ? 0 : quantity;
        }

        public bool IsEmpty
        {
            get { return string.IsNullOrEmpty(ItemId) || Quantity <= 0; }
        }

        public override string ToString()
        {
            return ItemId + " x" + Quantity;
        }
    }

    /// <summary>
    /// The player's carried items.
    ///
    /// Deliberately one slot per distinct item type rather than a grid of
    /// independently-stacked slots. That means a slot holds up to the item's
    /// MaxStack, and adding past that returns the excess rather than silently
    /// dropping it. Materials carry a large MaxStack so the limit is invisible
    /// in normal play, while equipment is MaxStack 1 so hoarding is bounded by
    /// slot count.
    ///
    /// The model is chosen because it makes "did the pickup succeed" a single
    /// unambiguous number the caller can act on, which is what a loot pickup,
    /// a quest turn-in and a save file all need.
    /// </summary>
    public sealed class Inventory
    {
        private readonly Dictionary<string, int> _quantities;
        private readonly ItemDatabase _database;

        public Inventory(ItemDatabase database, int capacitySlots = 40)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            CapacitySlots = capacitySlots < 1 ? 1 : capacitySlots;
            _quantities = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        /// <summary>Maximum number of distinct item types carried.</summary>
        public int CapacitySlots { get; set; }

        public int UsedSlots
        {
            get { return _quantities.Count; }
        }

        public int FreeSlots
        {
            get { return CapacitySlots - _quantities.Count; }
        }

        public bool IsFull
        {
            get { return FreeSlots <= 0; }
        }

        /// <summary>Raised with the item id and its new total, or 0 when fully removed.</summary>
        public event Action<string, int> Changed;

        public IEnumerable<KeyValuePair<string, int>> Entries
        {
            get { return _quantities; }
        }

        public int Count(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return 0;
            }

            return _quantities.TryGetValue(itemId, out int quantity) ? quantity : 0;
        }

        public bool Has(string itemId, int quantity = 1)
        {
            return quantity <= 0 || Count(itemId) >= quantity;
        }

        /// <summary>
        /// Adds items and returns how many were actually accepted, which may be
        /// less than requested when the stack cap or the slot cap is reached.
        /// Returning the accepted amount lets a pickup leave the remainder on the
        /// ground instead of destroying it.
        /// </summary>
        public int Add(string itemId, int quantity)
        {
            if (quantity <= 0 || string.IsNullOrEmpty(itemId))
            {
                return 0;
            }

            if (!_database.TryGet(itemId, out ItemDefinition definition))
            {
                return 0;
            }

            if (_quantities.TryGetValue(itemId, out int existing))
            {
                int room = definition.MaxStack - existing;
                if (room <= 0)
                {
                    return 0;
                }

                int accepted = quantity < room ? quantity : room;
                int updated = existing + accepted;
                _quantities[itemId] = updated;
                Changed?.Invoke(itemId, updated);
                return accepted;
            }

            if (IsFull)
            {
                return 0;
            }

            int first = quantity < definition.MaxStack ? quantity : definition.MaxStack;
            _quantities[itemId] = first;
            Changed?.Invoke(itemId, first);
            return first;
        }

        /// <summary>
        /// Removes items and returns how many were actually removed. Removing
        /// more than is held removes what is there and reports the shortfall
        /// through the return value.
        /// </summary>
        public int Remove(string itemId, int quantity)
        {
            if (quantity <= 0 || string.IsNullOrEmpty(itemId))
            {
                return 0;
            }

            if (!_quantities.TryGetValue(itemId, out int existing))
            {
                return 0;
            }

            int removed = quantity < existing ? quantity : existing;
            int remaining = existing - removed;

            if (remaining <= 0)
            {
                _quantities.Remove(itemId);
                Changed?.Invoke(itemId, 0);
            }
            else
            {
                _quantities[itemId] = remaining;
                Changed?.Invoke(itemId, remaining);
            }

            return removed;
        }

        /// <summary>Checks the whole cost is present and only then removes it. All or nothing.</summary>
        public bool TryConsume(string itemId, int quantity = 1)
        {
            if (!Has(itemId, quantity))
            {
                return false;
            }

            return Remove(itemId, quantity) == quantity;
        }

        /// <summary>Removes everything of one item type. Returns 0 for bound story items.</summary>
        public int RemoveAll(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return 0;
            }

            if (_database.TryGet(itemId, out ItemDefinition definition) && definition.IsBound)
            {
                return 0;
            }

            return Remove(itemId, Count(itemId));
        }

        public void Clear()
        {
            if (_quantities.Count == 0)
            {
                return;
            }

            var removed = new List<string>(_quantities.Keys);
            _quantities.Clear();

            for (int i = 0; i < removed.Count; i++)
            {
                Changed?.Invoke(removed[i], 0);
            }
        }

        /// <summary>Snapshot for saving. Order is not guaranteed to be stable across runs.</summary>
        public List<ItemStack> ToStacks()
        {
            var stacks = new List<ItemStack>(_quantities.Count);
            foreach (KeyValuePair<string, int> entry in _quantities)
            {
                stacks.Add(new ItemStack(entry.Key, entry.Value));
            }

            return stacks;
        }

        /// <summary>
        /// Restores a saved snapshot, skipping ids the database no longer knows
        /// about. Dropping unknown items keeps a save loadable after content is
        /// removed, rather than throwing and losing the whole save.
        /// </summary>
        public void LoadFrom(IEnumerable<ItemStack> stacks, out int skipped)
        {
            skipped = 0;
            Clear();

            if (stacks == null)
            {
                return;
            }

            foreach (ItemStack stack in stacks)
            {
                if (stack.IsEmpty)
                {
                    continue;
                }

                if (!_database.Contains(stack.ItemId))
                {
                    skipped++;
                    continue;
                }

                if (_quantities.ContainsKey(stack.ItemId))
                {
                    skipped++;
                    continue;
                }

                if (IsFull)
                {
                    skipped++;
                    continue;
                }

                _quantities[stack.ItemId] = stack.Quantity;
                Changed?.Invoke(stack.ItemId, stack.Quantity);
            }
        }
    }
}
