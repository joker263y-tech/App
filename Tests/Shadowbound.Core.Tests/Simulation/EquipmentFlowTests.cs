using Shadowbound.Core.Combat;
using Shadowbound.Core.Items;
using Shadowbound.Core.Numerics;
using Shadowbound.Core.Progression;
using Shadowbound.Core.Randomness;
using Shadowbound.Core.Simulation;
using Shadowbound.Core.Stats;
using Shadowbound.Core.Tests.Support;
using Xunit;

namespace Shadowbound.Core.Tests.Simulation
{
    /// <summary>
    /// Tests moving items between the bag and the character.
    ///
    /// The equipment system existed and was fully tested in isolation, but nothing at
    /// runtime ever called it - so every weapon and relic the game awarded sat in the
    /// bag forever and changed nothing. These tests cover the step that was missing:
    /// the swap itself, and in particular that it cannot lose an item when the bag is
    /// full.
    /// </summary>
    public class EquipmentFlowTests
    {
        private const string StarterBlade = "starter-blade";
        private const string FoundBlade = "found-blade";
        private const string Plate = "plate";

        private static GameSession MakeSession(int inventorySlots = 40)
        {
            var items = new ItemDatabase();
            items.Register(ItemTestData.Weapon(StarterBlade, 10f));
            items.Register(ItemTestData.Weapon(FoundBlade, 45f));
            items.Register(ItemTestData.Armor(Plate, 20f));
            items.Register(ItemTestData.Material("ash", 999));
            items.Register(ItemTestData.Relic("relic", 12f));

            Combatant player = CombatantFactory.Create(
                "hero",
                Faction.Player,
                maxHealth: 400f,
                maxStamina: 200f,
                attackPower: 100f);

            player.FaceImmediately(new Float3(0f, 0f, 1f));

            var session = new GameSession(
                player,
                items,
                new ExperienceCurve(),
                GameContentGrowth(),
                new DeterministicRng(7),
                WorldBounds.Square(40f));

            session.Inventory.CapacitySlots = inventorySlots;

            return session;
        }

        private static StatGrowth[] GameContentGrowth()
        {
            return new[]
            {
                new StatGrowth(StatId.MaxHealth, 20f),
                new StatGrowth(StatId.AttackPower, 5f)
            };
        }

        private static float AttackPowerOf(GameSession session)
        {
            return session.Player.Stats.Get(StatId.AttackPower);
        }

        // --------------------------------- equipping -------------------------------

        [Fact]
        public void EquippingFromTheBag_AppliesTheItemsStats()
        {
            GameSession session = MakeSession();

            Assert.Equal(0, session.Inventory.Count(FoundBlade));
            session.GrantItem(FoundBlade, 1);

            float before = AttackPowerOf(session);

            Assert.True(session.TryEquipFromInventory(FoundBlade, out EquipFailure failure));
            Assert.Equal(EquipFailure.None, failure);

            // The weapon's flat Attack Power is now part of the character's.
            Assert.Equal(before + 45f, AttackPowerOf(session), 3);

            // It left the bag and is now worn.
            Assert.Equal(0, session.Inventory.Count(FoundBlade));
            Assert.Equal(FoundBlade, session.Equipment.GetEquipped(EquipSlot.Weapon));
        }

        [Fact]
        public void EquippingASecondWeapon_ReplacesTheFirstAndKeepsIt()
        {
            GameSession session = MakeSession();

            session.GrantItem(StarterBlade, 1);
            session.GrantItem(FoundBlade, 1);

            Assert.True(session.TryEquipFromInventory(StarterBlade, out _));
            float withStarter = AttackPowerOf(session);

            Assert.True(session.TryEquipFromInventory(FoundBlade, out _));

            // Upgrading replaces rather than stacks, and the old weapon is not lost.
            Assert.Equal(FoundBlade, session.Equipment.GetEquipped(EquipSlot.Weapon));
            Assert.Equal(1, session.Inventory.Count(StarterBlade));
            Assert.Equal(0, session.Inventory.Count(FoundBlade));
            Assert.Equal(withStarter - 10f + 45f, AttackPowerOf(session), 3);
        }

        [Fact]
        public void EquippingIntoAFullBag_StillKeepsTheReplacedItem()
        {
            // The important case. Two slots: the upgrade and a stack of ash. Swapping
            // the weapon has to put the old one somewhere, and there is no room unless
            // the new item is removed from the bag first.
            GameSession session = MakeSession(inventorySlots: 2);

            session.GrantItem(StarterBlade, 1);
            session.GrantItem("ash", 5);

            Assert.True(session.TryEquipFromInventory(StarterBlade, out _));

            // Now bag = {ash}, and there is exactly one free slot.
            Assert.Equal(1, session.Inventory.UsedSlots);

            session.GrantItem(FoundBlade, 1);

            // Bag is full again: ash and the new blade.
            Assert.Equal(2, session.Inventory.UsedSlots);
            Assert.True(session.Inventory.IsFull);

            Assert.True(session.TryEquipFromInventory(FoundBlade, out _));

            Assert.Equal(FoundBlade, session.Equipment.GetEquipped(EquipSlot.Weapon));

            // Nothing was destroyed: the starter blade came back to the bag.
            Assert.Equal(1, session.Inventory.Count(StarterBlade));
            Assert.Equal(5, session.Inventory.Count("ash"));
        }

        [Fact]
        public void EquippingAnItemThatIsNotHeld_Fails()
        {
            GameSession session = MakeSession();

            Assert.False(session.TryEquipFromInventory(FoundBlade, out _));
            Assert.True(session.Equipment.IsEmpty(EquipSlot.Weapon));
        }

        [Fact]
        public void EquippingAMaterial_FailsWithNotEquippable()
        {
            GameSession session = MakeSession();

            session.GrantItem("ash", 5);

            Assert.False(session.TryEquipFromInventory("ash", out EquipFailure failure));
            Assert.Equal(EquipFailure.NotEquippable, failure);

            // A rejected equip must not consume the item.
            Assert.Equal(5, session.Inventory.Count("ash"));
        }

        [Fact]
        public void EquippingAnUnknownItem_Fails()
        {
            GameSession session = MakeSession();

            Assert.False(session.TryEquipFromInventory("not-a-real-item", out EquipFailure failure));
            Assert.Equal(EquipFailure.UnknownItem, failure);
        }

        [Fact]
        public void EquippingAnItemAboveTheCharactersLevel_FailsAndKeepsIt()
        {
            GameSession session = MakeSession();

            session.Items.Register(ItemTestData.Weapon("late-game-blade", 200f, requiredLevel: 40));
            session.GrantItem("late-game-blade", 1);

            Assert.False(session.TryEquipFromInventory("late-game-blade", out EquipFailure failure));
            Assert.Equal(EquipFailure.LevelTooLow, failure);

            // Still in the bag, ready for when the level is reached.
            Assert.Equal(1, session.Inventory.Count("late-game-blade"));
        }

        [Fact]
        public void EquippingFillsDifferentSlotsIndependently()
        {
            GameSession session = MakeSession();

            session.GrantItem(FoundBlade, 1);
            session.GrantItem(Plate, 1);
            session.GrantItem("relic", 1);

            Assert.True(session.TryEquipFromInventory(FoundBlade, out _));
            Assert.True(session.TryEquipFromInventory(Plate, out _));
            Assert.True(session.TryEquipFromInventory("relic", out _));

            Assert.Equal(FoundBlade, session.Equipment.GetEquipped(EquipSlot.Weapon));
            Assert.Equal(Plate, session.Equipment.GetEquipped(EquipSlot.Armor));
            Assert.Equal("relic", session.Equipment.GetEquipped(EquipSlot.Relic));

            // All three bonuses are live at once.
            Assert.Equal(100f + 45f, AttackPowerOf(session), 3);
            Assert.Equal(20f, session.Player.Stats.Get(StatId.Armor), 3);
            Assert.Equal(12f, session.Player.Stats.Get(StatId.ShadowPower), 3);
        }

        // -------------------------------- unequipping ------------------------------

        [Fact]
        public void Unequipping_ReturnsTheItemAndRemovesItsStats()
        {
            GameSession session = MakeSession();

            session.GrantItem(FoundBlade, 1);
            session.TryEquipFromInventory(FoundBlade, out _);

            Assert.True(session.TryUnequipToInventory(EquipSlot.Weapon));

            Assert.Equal(100f, AttackPowerOf(session), 3);
            Assert.Equal(1, session.Inventory.Count(FoundBlade));
            Assert.True(session.Equipment.IsEmpty(EquipSlot.Weapon));
        }

        [Fact]
        public void UnequippingAnEmptySlot_Fails()
        {
            GameSession session = MakeSession();

            Assert.False(session.TryUnequipToInventory(EquipSlot.Weapon));
        }

        [Fact]
        public void UnequippingIntoAFullBag_KeepsTheItemEquipped()
        {
            // One slot, holding the weapon being worn. There is nowhere for it to go,
            // so it must stay on rather than being silently destroyed.
            GameSession session = MakeSession(inventorySlots: 2);

            session.GrantItem(FoundBlade, 1);
            session.GrantItem("ash", 5);
            session.TryEquipFromInventory(FoundBlade, out _);

            // Bag = {ash}. Fill the last slot so nothing can be stowed.
            session.Inventory.CapacitySlots = 1;

            Assert.False(session.TryUnequipToInventory(EquipSlot.Weapon));

            Assert.Equal(FoundBlade, session.Equipment.GetEquipped(EquipSlot.Weapon));
            Assert.Equal(0, session.Inventory.Count(FoundBlade));
            Assert.Equal(100f + 45f, AttackPowerOf(session), 3);
        }

        // -------------------------------- persistence ------------------------------

        [Fact]
        public void EquippedItems_SurviveASaveAndLoad()
        {
            GameSession session = MakeSession();

            session.GrantItem(FoundBlade, 1);
            session.GrantItem(Plate, 1);
            session.TryEquipFromInventory(FoundBlade, out _);
            session.TryEquipFromInventory(Plate, out _);

            Shadowbound.Core.Serialization.SaveGame save = session.CreateSave();

            GameSession restored = MakeSession();
            restored.ApplySave(save);

            Assert.Equal(FoundBlade, restored.Equipment.GetEquipped(EquipSlot.Weapon));
            Assert.Equal(Plate, restored.Equipment.GetEquipped(EquipSlot.Armor));

            // The stats must be live again after loading, not merely recorded.
            Assert.Equal(100f + 45f, AttackPowerOf(restored), 3);
            Assert.Equal(20f, restored.Player.Stats.Get(StatId.Armor), 3);
        }

        [Fact]
        public void LoadingTwentyTimes_DoesNotStackEquipmentStats()
        {
            // Applying a loadout repeatedly must not accumulate modifiers, or a player
            // who reloads would grow stronger every time.
            GameSession session = MakeSession();

            session.GrantItem(FoundBlade, 1);
            session.TryEquipFromInventory(FoundBlade, out _);

            Shadowbound.Core.Serialization.SaveGame save = session.CreateSave();

            GameSession restored = MakeSession();

            for (int i = 0; i < 20; i++)
            {
                restored.ApplySave(save);
            }

            Assert.Equal(100f + 45f, AttackPowerOf(restored), 3);
        }
    }
}
