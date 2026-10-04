using System;
using System.Collections.Generic;
using Shadowbound.Core.Combat;
using Shadowbound.Core.Stats;

namespace Shadowbound.Core.Items
{
    /// <summary>Equipment positions on the character.</summary>
    public enum EquipSlot
    {
        Weapon = 0,
        Armor = 1,
        Relic = 2,

        /// <summary>A second trinket position, unlocked partway through the story.</summary>
        Ward = 3
    }

    public static class EquipSlots
    {
        public const int Count = 4;

        public static readonly EquipSlot[] All =
        {
            EquipSlot.Weapon,
            EquipSlot.Armor,
            EquipSlot.Relic,
            EquipSlot.Ward
        };

        public static string Name(EquipSlot slot)
        {
            switch (slot)
            {
                case EquipSlot.Weapon: return "Weapon";
                case EquipSlot.Armor: return "Armor";
                case EquipSlot.Relic: return "Relic";
                case EquipSlot.Ward: return "Ward";
                default: return slot.ToString();
            }
        }
    }

    /// <summary>Why an equip attempt failed.</summary>
    public enum EquipFailure
    {
        None = 0,
        UnknownItem = 1,
        NotEquippable = 2,
        WrongSlot = 3,
        LevelTooLow = 4,
        SlotLocked = 5
    }

    /// <summary>
    /// What the character currently has equipped.
    ///
    /// Each slot owns a unique token that is used as the source of every stat
    /// modifier it contributes. Equipping and unequipping therefore remove
    /// exactly their own contributions through
    /// <see cref="StatSet.RemoveModifiersFrom"/>, and cannot disturb a modifier
    /// granted by anything else. Swapping a weapon while a strength buff is
    /// active leaves the buff untouched.
    /// </summary>
    public sealed class EquipmentLoadout
    {
        private readonly Combatant _owner;
        private readonly ItemDatabase _database;
        private readonly string[] _equipped;
        private readonly object[] _tokens;
        private readonly bool[] _unlocked;

        public EquipmentLoadout(Combatant owner, ItemDatabase database)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _database = database ?? throw new ArgumentNullException(nameof(database));

            _equipped = new string[EquipSlots.Count];
            _tokens = new object[EquipSlots.Count];
            _unlocked = new bool[EquipSlots.Count];

            for (int i = 0; i < EquipSlots.Count; i++)
            {
                _tokens[i] = new object();

                // All slots start available. The Ward slot is unlocked by story
                // progression rather than withheld by default, so a fresh
                // character is not missing a core feature.
                _unlocked[i] = true;
            }
        }

        /// <summary>Raised with the slot and the newly equipped item id, or null when emptied.</summary>
        public event Action<EquipSlot, string> Changed;

        public string GetEquipped(EquipSlot slot)
        {
            return _equipped[(int)slot];
        }

        public bool IsEmpty(EquipSlot slot)
        {
            return string.IsNullOrEmpty(_equipped[(int)slot]);
        }

        public bool IsUnlocked(EquipSlot slot)
        {
            return _unlocked[(int)slot];
        }

        public void SetUnlocked(EquipSlot slot, bool unlocked)
        {
            _unlocked[(int)slot] = unlocked;
        }

        public EquipFailure CanEquip(EquipSlot slot, ItemDefinition item)
        {
            if (item == null)
            {
                return EquipFailure.UnknownItem;
            }

            if (!_unlocked[(int)slot])
            {
                return EquipFailure.SlotLocked;
            }

            if (!item.IsEquippable || item.Slot == null)
            {
                return EquipFailure.NotEquippable;
            }

            if (item.Slot.Value != slot)
            {
                return EquipFailure.WrongSlot;
            }

            return _owner.Level < item.RequiredLevel ? EquipFailure.LevelTooLow : EquipFailure.None;
        }

        /// <summary>
        /// Equips an item, returning the previously equipped item so the caller
        /// can put it back in the inventory. Refuses and changes nothing when the
        /// item cannot be equipped.
        /// </summary>
        public bool TryEquip(EquipSlot slot, ItemDefinition item, out ItemDefinition replaced, out EquipFailure failure)
        {
            replaced = null;
            failure = CanEquip(slot, item);

            if (failure != EquipFailure.None)
            {
                return false;
            }

            int index = (int)slot;

            if (!string.IsNullOrEmpty(_equipped[index]))
            {
                _database.TryGet(_equipped[index], out replaced);
                RemoveModifiers(slot);
            }

            _equipped[index] = item.Id;
            ApplyModifiers(slot, item);

            Changed?.Invoke(slot, item.Id);
            return true;
        }

        /// <summary>Empties a slot and returns the item that was in it.</summary>
        public bool TryUnequip(EquipSlot slot, out ItemDefinition removed)
        {
            removed = null;
            int index = (int)slot;

            if (string.IsNullOrEmpty(_equipped[index]))
            {
                return false;
            }

            _database.TryGet(_equipped[index], out removed);
            _equipped[index] = null;
            RemoveModifiers(slot);

            Changed?.Invoke(slot, null);
            return true;
        }

        /// <summary>
        /// Clears every slot. Used when loading a save before the saved loadout
        /// is applied, so stale modifiers from a previous character cannot linger.
        /// </summary>
        public void ClearAll()
        {
            for (int i = 0; i < EquipSlots.Count; i++)
            {
                var slot = (EquipSlot)i;
                if (!string.IsNullOrEmpty(_equipped[i]))
                {
                    _equipped[i] = null;
                    RemoveModifiers(slot);
                    Changed?.Invoke(slot, null);
                }
            }
        }

        /// <summary>Restores a saved loadout by item id, skipping anything invalid.</summary>
        public void LoadFrom(IReadOnlyList<ItemStack> entries)
        {
            ClearAll();

            if (entries == null)
            {
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                ItemStack entry = entries[i];
                if (entry.IsEmpty || !_database.TryGet(entry.ItemId, out ItemDefinition item))
                {
                    continue;
                }

                EquipSlot? slot = item.Slot;
                if (slot == null)
                {
                    continue;
                }

                // Level requirements are deliberately not re-checked on load: a
                // save should not lose gear because of an unrelated balance
                // change to a level requirement.
                int index = (int)slot.Value;
                if (!string.IsNullOrEmpty(_equipped[index]))
                {
                    continue;
                }

                _equipped[index] = item.Id;
                ApplyModifiers(slot.Value, item);
                Changed?.Invoke(slot.Value, item.Id);
            }
        }

        /// <summary>Snapshot for saving.</summary>
        public List<ItemStack> ToStacks()
        {
            var stacks = new List<ItemStack>(EquipSlots.Count);

            for (int i = 0; i < EquipSlots.Count; i++)
            {
                if (!string.IsNullOrEmpty(_equipped[i]))
                {
                    stacks.Add(new ItemStack(_equipped[i], 1));
                }
            }

            return stacks;
        }

        private void ApplyModifiers(EquipSlot slot, ItemDefinition item)
        {
            StatModifier[] modifiers = item.Modifiers;
            if (modifiers == null || modifiers.Length == 0)
            {
                return;
            }

            object token = _tokens[(int)slot];
            for (int i = 0; i < modifiers.Length; i++)
            {
                _owner.Stats.AddModifier(modifiers[i].WithSource(token));
            }
        }

        private void RemoveModifiers(EquipSlot slot)
        {
            _owner.Stats.RemoveModifiersFrom(_tokens[(int)slot]);
        }
    }
}
