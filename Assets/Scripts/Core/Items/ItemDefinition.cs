using System;
using System.Collections.Generic;
using Shadowbound.Core.Combat;
using Shadowbound.Core.Stats;

namespace Shadowbound.Core.Items
{
    public enum ItemKind
    {
        /// <summary>Crafting and upgrade fodder. No direct use.</summary>
        Material = 0,

        /// <summary>Consumed for an immediate effect.</summary>
        Consumable = 1,

        /// <summary>Occupies the weapon slot.</summary>
        Weapon = 2,

        /// <summary>Occupies the armour slot.</summary>
        Armor = 3,

        /// <summary>Occupies a relic or ward slot.</summary>
        Relic = 4,

        /// <summary>Story item. Cannot be dropped or sold.</summary>
        Quest = 5
    }

    /// <summary>Drop-tier label, used for colour coding and loot weighting.</summary>
    public enum ItemRarity
    {
        Common = 0,
        Uncommon = 1,
        Rare = 2,

        /// <summary>Drawn from the Umbra. The game's top tier of ordinary gear.</summary>
        Umbral = 3,

        /// <summary>Unique story relics.</summary>
        Mythic = 4
    }

    public enum EffectKind
    {
        RestoreHealth = 0,
        RestoreStamina = 1,
        ApplyStatus = 2,
        GrantExperience = 3
    }

    /// <summary>One thing a consumable does when used.</summary>
    public readonly struct ItemEffect
    {
        public readonly EffectKind Kind;
        public readonly float Amount;
        public readonly StatusKind Status;
        public readonly float Duration;

        public ItemEffect(EffectKind kind, float amount)
        {
            Kind = kind;
            Amount = amount;
            Status = StatusKind.Warded;
            Duration = 0f;
        }

        public ItemEffect(StatusKind status, float magnitude, float duration)
        {
            Kind = EffectKind.ApplyStatus;
            Amount = magnitude;
            Status = status;
            Duration = duration;
        }
    }

    /// <summary>Why a consumable could not be used, for the message the player sees.</summary>
    public enum ConsumableFailure
    {
        None = 0,
        UnknownItem = 1,
        NotConsumable = 2,
        NotHeld = 3
    }

    /// <summary>
    /// Authored definition of an item. Shared by every copy of that item, so
    /// nothing mutable belongs here. The modifier templates carry no source; an
    /// equipped instance rebinds them to its own token.
    /// </summary>
    public sealed class ItemDefinition
    {
        public string Id = "unnamed";
        public string DisplayName = "";
        public string Description = "";
        public ItemKind Kind = ItemKind.Material;
        public ItemRarity Rarity = ItemRarity.Common;

        /// <summary>Most of this item that fits in a single inventory slot.</summary>
        public int MaxStack = 1;

        /// <summary>Character level required to equip. Zero for anything non-equippable.</summary>
        public int RequiredLevel;

        /// <summary>Stat changes granted while equipped.</summary>
        public StatModifier[] Modifiers = Array.Empty<StatModifier>();

        /// <summary>Effects applied on use. Only meaningful for consumables.</summary>
        public ItemEffect[] Effects = Array.Empty<ItemEffect>();

        /// <summary>Item cannot be discarded or sold. Story items.</summary>
        public bool IsBound;

        public bool IsEquippable
        {
            get { return Kind == ItemKind.Weapon || Kind == ItemKind.Armor || Kind == ItemKind.Relic; }
        }

        public bool IsConsumable
        {
            get { return Kind == ItemKind.Consumable; }
        }

        /// <summary>The equipment slot this item occupies, or null when it is not equippable.</summary>
        public EquipSlot? Slot
        {
            get
            {
                switch (Kind)
                {
                    case ItemKind.Weapon: return EquipSlot.Weapon;
                    case ItemKind.Armor: return EquipSlot.Armor;
                    case ItemKind.Relic: return EquipSlot.Relic;
                    default: return null;
                }
            }
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(DisplayName) ? Id : DisplayName;
        }
    }

    /// <summary>
    /// Lookup for every item in the game.
    ///
    /// Holds definitions, not player state, so it is safe to share. It resolves
    /// ids to definitions for the inventory, loot and equipment systems, which is
    /// what allows save files to store ids rather than duplicating item data.
    /// </summary>
    public sealed class ItemDatabase
    {
        private readonly Dictionary<string, ItemDefinition> _items;

        public ItemDatabase()
        {
            _items = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
        }

        public int Count
        {
            get { return _items.Count; }
        }

        public IEnumerable<ItemDefinition> All
        {
            get { return _items.Values; }
        }

        /// <summary>Registers a definition. Re-registering an id replaces it, which keeps reloading content idempotent.</summary>
        public void Register(ItemDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.Id))
            {
                throw new ArgumentException("Item definition requires a non-empty id.", nameof(definition));
            }

            if (definition.MaxStack < 1)
            {
                definition.MaxStack = 1;
            }

            _items[definition.Id] = definition;
        }

        public void RegisterRange(IEnumerable<ItemDefinition> definitions)
        {
            if (definitions == null)
            {
                return;
            }

            foreach (ItemDefinition definition in definitions)
            {
                Register(definition);
            }
        }

        public bool TryGet(string id, out ItemDefinition definition)
        {
            definition = null;
            return !string.IsNullOrEmpty(id) && _items.TryGetValue(id, out definition);
        }

        /// <summary>Returns the definition, or null when the id is unknown.</summary>
        public ItemDefinition Get(string id)
        {
            return TryGet(id, out ItemDefinition definition) ? definition : null;
        }

        public bool Contains(string id)
        {
            return !string.IsNullOrEmpty(id) && _items.ContainsKey(id);
        }
    }
}
