using System.Collections.Generic;
using Shadowbound.Core.Items;
using Shadowbound.Core.Tests.Support;
using Xunit;

namespace Shadowbound.Core.Tests.Items
{
    public class InventoryTests
    {
        private static Inventory MakeInventory(
            out ItemDatabase database,
            int capacity = 40,
            int materialStack = 99)
        {
            database = ItemTestData.Database(
                ItemTestData.Material("ash", materialStack),
                ItemTestData.Material("bone"),
                ItemTestData.Weapon("blade", 10f),
                ItemTestData.QuestItem("sigil"));

            return new Inventory(database, capacity);
        }

        [Fact]
        public void Add_StoresItemsAndReportsTheAmountAccepted()
        {
            Inventory inventory = MakeInventory(out _);

            int accepted = inventory.Add("ash", 5);

            Assert.Equal(5, accepted);
            Assert.Equal(5, inventory.Count("ash"));
            Assert.Equal(1, inventory.UsedSlots);
        }

        [Fact]
        public void Add_RespectsThePerItemStackCap()
        {
            // Overfilling must report the excess rather than silently destroy it,
            // so a ground pickup can leave the remainder behind.
            Inventory inventory = MakeInventory(out _, materialStack: 10);

            int accepted = inventory.Add("ash", 25);

            Assert.Equal(10, accepted);
            Assert.Equal(10, inventory.Count("ash"));
        }

        [Fact]
        public void Add_ToAnExistingStack_OnlyAcceptsUpToTheRemainingRoom()
        {
            Inventory inventory = MakeInventory(out _, materialStack: 10);
            inventory.Add("ash", 8);

            int accepted = inventory.Add("ash", 5);

            Assert.Equal(2, accepted);
            Assert.Equal(10, inventory.Count("ash"));
        }

        [Fact]
        public void Add_OfAnUnknownItem_IsRefused()
        {
            Inventory inventory = MakeInventory(out _);

            Assert.Equal(0, inventory.Add("does-not-exist", 5));
            Assert.Equal(0, inventory.UsedSlots);
        }

        [Fact]
        public void Add_WhenEverySlotIsUsed_IsRefused()
        {
            Inventory inventory = MakeInventory(out _, capacity: 2);
            inventory.Add("ash", 1);
            inventory.Add("bone", 1);

            int accepted = inventory.Add("blade", 1);

            Assert.Equal(0, accepted);
            Assert.True(inventory.IsFull);
            // An existing stack must still accept more even when slots are full.
            Assert.Equal(1, inventory.Add("ash", 1));
        }

        [Fact]
        public void Add_OfANonPositiveQuantity_DoesNothing()
        {
            Inventory inventory = MakeInventory(out _);

            Assert.Equal(0, inventory.Add("ash", 0));
            Assert.Equal(0, inventory.Add("ash", -5));
            Assert.Equal(0, inventory.UsedSlots);
        }

        [Fact]
        public void Remove_ReportsWhatWasActuallyRemoved()
        {
            Inventory inventory = MakeInventory(out _);
            inventory.Add("ash", 10);

            Assert.Equal(4, inventory.Remove("ash", 4));
            Assert.Equal(6, inventory.Count("ash"));
        }

        [Fact]
        public void Remove_MoreThanHeld_RemovesEverythingAndReportsTheShortfall()
        {
            Inventory inventory = MakeInventory(out _);
            inventory.Add("ash", 3);

            Assert.Equal(3, inventory.Remove("ash", 10));
            Assert.Equal(0, inventory.Count("ash"));
        }

        [Fact]
        public void Remove_EmptiesTheSlotWhenTheStackReachesZero()
        {
            Inventory inventory = MakeInventory(out _);
            inventory.Add("ash", 3);

            inventory.Remove("ash", 3);

            Assert.Equal(0, inventory.UsedSlots);
        }

        [Fact]
        public void TryConsume_IsAllOrNothing()
        {
            Inventory inventory = MakeInventory(out _);
            inventory.Add("ash", 3);

            Assert.False(inventory.TryConsume("ash", 5));
            // The failed attempt must not have partially drained the stack.
            Assert.Equal(3, inventory.Count("ash"));

            Assert.True(inventory.TryConsume("ash", 3));
            Assert.Equal(0, inventory.Count("ash"));
        }

        [Fact]
        public void Has_TreatsANonPositiveQuantityAsSatisfied()
        {
            Inventory inventory = MakeInventory(out _);

            Assert.True(inventory.Has("ash", 0));
            Assert.False(inventory.Has("ash", 1));
        }

        [Fact]
        public void RemoveAll_RefusesToDiscardBoundQuestItems()
        {
            Inventory inventory = MakeInventory(out _);
            inventory.Add("sigil", 1);

            Assert.Equal(0, inventory.RemoveAll("sigil"));
            Assert.Equal(1, inventory.Count("sigil"));
        }

        [Fact]
        public void Changed_FiresWithTheNewTotal()
        {
            Inventory inventory = MakeInventory(out _);
            var observed = new List<KeyValuePair<string, int>>();
            inventory.Changed += (id, total) => observed.Add(new KeyValuePair<string, int>(id, total));

            inventory.Add("ash", 5);
            inventory.Remove("ash", 2);

            Assert.Equal(2, observed.Count);
            Assert.Equal(5, observed[0].Value);
            Assert.Equal(3, observed[1].Value);
        }

        [Fact]
        public void ToStacks_AndLoadFrom_RoundTrip()
        {
            Inventory inventory = MakeInventory(out _);
            inventory.Add("ash", 7);
            inventory.Add("blade", 1);

            List<ItemStack> saved = inventory.ToStacks();

            Inventory restored = new Inventory(ItemTestData.Database(
                ItemTestData.Material("ash"),
                ItemTestData.Weapon("blade", 10f)));

            restored.LoadFrom(saved, out int skipped);

            Assert.Equal(0, skipped);
            Assert.Equal(7, restored.Count("ash"));
            Assert.Equal(1, restored.Count("blade"));
        }

        [Fact]
        public void LoadFrom_SkipsItemsTheDatabaseNoLongerKnows()
        {
            // Removing content must leave old saves loadable rather than fatal.
            Inventory inventory = MakeInventory(out _);
            var saved = new List<ItemStack>
            {
                new ItemStack("ash", 4),
                new ItemStack("removed-in-a-patch", 2)
            };

            inventory.LoadFrom(saved, out int skipped);

            Assert.Equal(1, skipped);
            Assert.Equal(4, inventory.Count("ash"));
        }

        [Fact]
        public void Clear_EmptiesEverything()
        {
            Inventory inventory = MakeInventory(out _);
            inventory.Add("ash", 5);
            inventory.Add("bone", 5);

            inventory.Clear();

            Assert.Equal(0, inventory.UsedSlots);
            Assert.Equal(0, inventory.Count("ash"));
        }

        [Fact]
        public void Database_RejectsDefinitionsWithoutAnId()
        {
            var database = new ItemDatabase();

            Assert.Throws<System.ArgumentException>(() => database.Register(new ItemDefinition { Id = "" }));
        }

        [Fact]
        public void Database_TreatsMaxStackBelowOneAsOne()
        {
            var database = new ItemDatabase();
            database.Register(new ItemDefinition { Id = "odd", MaxStack = 0 });

            Assert.Equal(1, database.Get("odd").MaxStack);
        }
    }
}
