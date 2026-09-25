using Shadowbound.Core.Combat;
using Shadowbound.Core.Items;
using Shadowbound.Core.Stats;
using Shadowbound.Core.Tests.Support;
using Xunit;

namespace Shadowbound.Core.Tests.Items
{
    public class EquipmentLoadoutTests
    {
        private static EquipmentLoadout MakeLoadout(
            out Combatant combatant,
            out ItemDatabase database,
            int level = 1)
        {
            combatant = CombatantFactory.Create(
                "hero",
                Faction.Player,
                maxHealth: 200f,
                attackPower: 100f,
                armor: 0f,
                level: level);

            database = ItemTestData.Database(
                ItemTestData.Weapon("blade", 50f),
                ItemTestData.Weapon("umbral-blade", 120f, requiredLevel: 10),
                ItemTestData.Armor("plate", 80f),
                ItemTestData.Relic("ember", 30f),
                ItemTestData.Material("ash"));

            return new EquipmentLoadout(combatant, database);
        }

        [Fact]
        public void EquippingAWeapon_AppliesItsModifiers()
        {
            EquipmentLoadout loadout = MakeLoadout(out Combatant combatant, out ItemDatabase database);

            loadout.TryEquip(EquipSlot.Weapon, database.Get("blade"), out _, out _);

            Assert.Equal(150f, combatant.Stats.Get(StatId.AttackPower), 3);
        }

        [Fact]
        public void Unequipping_RemovesOnlyThatSlotsModifiers()
        {
            EquipmentLoadout loadout = MakeLoadout(out Combatant combatant, out ItemDatabase database);
            loadout.TryEquip(EquipSlot.Weapon, database.Get("blade"), out _, out _);
            loadout.TryEquip(EquipSlot.Relic, database.Get("ember"), out _, out _);

            loadout.TryUnequip(EquipSlot.Weapon, out _);

            Assert.Equal(100f, combatant.Stats.Get(StatId.AttackPower), 3);
            // The relic must be untouched.
            Assert.Equal(30f, combatant.Stats.Get(StatId.ShadowPower), 3);
            Assert.Single(loadout.ToStacks());
        }

        [Fact]
        public void Unequipping_DoesNotDisturbModifiersFromOtherSources()
        {
            // This is the whole point of source tokens on modifiers: removing a
            // weapon must not strip a buff.
            EquipmentLoadout loadout = MakeLoadout(out Combatant combatant, out ItemDatabase database);
            loadout.TryEquip(EquipSlot.Weapon, database.Get("blade"), out _, out _);

            var buff = new object();
            combatant.Stats.AddModifier(StatModifier.Percent(StatId.AttackPower, 0.5f, buff));

            // (100 + 50) * 1.5
            Assert.Equal(225f, combatant.Stats.Get(StatId.AttackPower), 3);

            loadout.TryUnequip(EquipSlot.Weapon, out _);

            // Buff alone remains: 100 * 1.5
            Assert.Equal(150f, combatant.Stats.Get(StatId.AttackPower), 3);
        }

        [Fact]
        public void SwappingWeapons_DoesNotStackTheOldWeaponsModifiers()
        {
            EquipmentLoadout loadout = MakeLoadout(out Combatant combatant, out ItemDatabase database, level: 10);
            loadout.TryEquip(EquipSlot.Weapon, database.Get("blade"), out _, out _);

            loadout.TryEquip(EquipSlot.Weapon, database.Get("umbral-blade"), out _, out _);

            // 100 + 120, not 100 + 50 + 120.
            Assert.Equal(220f, combatant.Stats.Get(StatId.AttackPower), 3);
        }

        [Fact]
        public void SwappingWeapons_ReturnsTheReplacedItem()
        {
            EquipmentLoadout loadout = MakeLoadout(out _, out ItemDatabase database, level: 10);
            loadout.TryEquip(EquipSlot.Weapon, database.Get("blade"), out _, out _);

            loadout.TryEquip(EquipSlot.Weapon, database.Get("umbral-blade"), out ItemDefinition replaced, out _);

            Assert.NotNull(replaced);
            Assert.Equal("blade", replaced.Id);
        }

        [Fact]
        public void CanEquip_RefusesAnItemForADifferentSlot()
        {
            EquipmentLoadout loadout = MakeLoadout(out _, out ItemDatabase database);

            Assert.Equal(EquipFailure.WrongSlot, loadout.CanEquip(EquipSlot.Weapon, database.Get("plate")));
        }

        [Fact]
        public void CanEquip_RefusesANonEquippableItem()
        {
            EquipmentLoadout loadout = MakeLoadout(out _, out ItemDatabase database);

            Assert.Equal(EquipFailure.NotEquippable, loadout.CanEquip(EquipSlot.Weapon, database.Get("ash")));
        }

        [Fact]
        public void CanEquip_EnforcesTheLevelRequirement()
        {
            EquipmentLoadout loadout = MakeLoadout(out _, out ItemDatabase database, level: 5);

            Assert.Equal(EquipFailure.LevelTooLow, loadout.CanEquip(EquipSlot.Weapon, database.Get("umbral-blade")));
        }

        [Fact]
        public void TryEquip_WhenRefused_ChangesNothing()
        {
            EquipmentLoadout loadout = MakeLoadout(out Combatant combatant, out ItemDatabase database);

            bool equipped = loadout.TryEquip(
                EquipSlot.Weapon,
                database.Get("umbral-blade"),
                out ItemDefinition replaced,
                out EquipFailure failure);

            Assert.False(equipped);
            Assert.Equal(EquipFailure.LevelTooLow, failure);
            Assert.Null(replaced);
            Assert.Equal(100f, combatant.Stats.Get(StatId.AttackPower), 3);
            Assert.True(loadout.IsEmpty(EquipSlot.Weapon));
        }

        [Fact]
        public void TryUnequip_AnEmptySlot_ReportsNothing()
        {
            EquipmentLoadout loadout = MakeLoadout(out _, out _);

            Assert.False(loadout.TryUnequip(EquipSlot.Weapon, out ItemDefinition removed));
            Assert.Null(removed);
        }

        [Fact]
        public void ClearAll_RemovesEverySlotsModifiers()
        {
            EquipmentLoadout loadout = MakeLoadout(out Combatant combatant, out ItemDatabase database);
            loadout.TryEquip(EquipSlot.Weapon, database.Get("blade"), out _, out _);
            loadout.TryEquip(EquipSlot.Armor, database.Get("plate"), out _, out _);

            loadout.ClearAll();

            Assert.Equal(100f, combatant.Stats.Get(StatId.AttackPower), 3);
            Assert.Equal(0f, combatant.Stats.Get(StatId.Armor), 3);
        }

        [Fact]
        public void LoadFrom_RestoresASavedLoadout()
        {
            EquipmentLoadout loadout = MakeLoadout(out Combatant combatant, out ItemDatabase database, level: 10);
            loadout.TryEquip(EquipSlot.Weapon, database.Get("umbral-blade"), out _, out _);
            loadout.TryEquip(EquipSlot.Armor, database.Get("plate"), out _, out _);

            var saved = loadout.ToStacks();
            loadout.ClearAll();
            Assert.Equal(100f, combatant.Stats.Get(StatId.AttackPower), 3);

            loadout.LoadFrom(saved);

            Assert.Equal(220f, combatant.Stats.Get(StatId.AttackPower), 3);
            Assert.Equal(80f, combatant.Stats.Get(StatId.Armor), 3);
        }

        [Fact]
        public void LoadFrom_IgnoresUnknownItemIds()
        {
            EquipmentLoadout loadout = MakeLoadout(out Combatant combatant, out ItemDatabase database);

            loadout.LoadFrom(new[]
            {
                new ItemStack("blade", 1),
                new ItemStack("deleted-item", 1)
            });

            Assert.Equal(150f, combatant.Stats.Get(StatId.AttackPower), 3);
            Assert.Equal("blade", loadout.GetEquipped(EquipSlot.Weapon));
        }

        [Fact]
        public void Changed_FiresWithTheSlotAndNewItem()
        {
            EquipmentLoadout loadout = MakeLoadout(out _, out ItemDatabase database);
            EquipSlot? observedSlot = null;
            string observedItem = "unset";
            loadout.Changed += (slot, item) =>
            {
                observedSlot = slot;
                observedItem = item;
            };

            loadout.TryEquip(EquipSlot.Armor, database.Get("plate"), out _, out _);

            Assert.Equal(EquipSlot.Armor, observedSlot);
            Assert.Equal("plate", observedItem);
        }

        [Fact]
        public void EveryEquippableItemKind_MapsToExactlyOneSlot()
        {
            // A mismatch here would let an item be equipped into a slot it does
            // not belong to, or become impossible to equip at all.
            Assert.Equal(EquipSlot.Weapon, ItemTestData.Weapon("w", 1f).Slot);
            Assert.Equal(EquipSlot.Armor, ItemTestData.Armor("a", 1f).Slot);
            Assert.Equal(EquipSlot.Relic, ItemTestData.Relic("r", 1f).Slot);
            Assert.Null(ItemTestData.Material("m").Slot);

            for (int i = 0; i < EquipSlots.All.Length; i++)
            {
                Assert.Equal(i, (int)EquipSlots.All[i]);
            }
        }
    }
}
