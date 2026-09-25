using System.Collections.Generic;
using Shadowbound.Core.Combat;
using Shadowbound.Core.Items;
using Shadowbound.Core.Stats;

namespace Shadowbound.Core.Tests.Support
{
    internal static class ItemTestData
    {
        public static ItemDatabase Database(params ItemDefinition[] items)
        {
            var database = new ItemDatabase();
            database.RegisterRange(items);
            return database;
        }

        public static ItemDefinition Material(string id, int maxStack = 99)
        {
            return new ItemDefinition
            {
                Id = id,
                DisplayName = id,
                Kind = ItemKind.Material,
                MaxStack = maxStack
            };
        }

        public static ItemDefinition Weapon(string id, float attack, int requiredLevel = 1, int maxStack = 1)
        {
            return new ItemDefinition
            {
                Id = id,
                DisplayName = id,
                Kind = ItemKind.Weapon,
                MaxStack = maxStack,
                RequiredLevel = requiredLevel,
                Modifiers = new[] { StatModifier.Flat(StatId.AttackPower, attack) }
            };
        }

        public static ItemDefinition Armor(string id, float armor, int requiredLevel = 1)
        {
            return new ItemDefinition
            {
                Id = id,
                DisplayName = id,
                Kind = ItemKind.Armor,
                MaxStack = 1,
                RequiredLevel = requiredLevel,
                Modifiers = new[] { StatModifier.Flat(StatId.Armor, armor) }
            };
        }

        public static ItemDefinition Relic(string id, float umbra)
        {
            return new ItemDefinition
            {
                Id = id,
                DisplayName = id,
                Kind = ItemKind.Relic,
                MaxStack = 1,
                Modifiers = new[] { StatModifier.Flat(StatId.ShadowPower, umbra) }
            };
        }

        public static ItemDefinition Potion(string id, float heal)
        {
            return new ItemDefinition
            {
                Id = id,
                DisplayName = id,
                Kind = ItemKind.Consumable,
                MaxStack = 10,
                Effects = new[] { new ItemEffect(EffectKind.RestoreHealth, heal) }
            };
        }

        public static ItemDefinition QuestItem(string id)
        {
            return new ItemDefinition
            {
                Id = id,
                DisplayName = id,
                Kind = ItemKind.Quest,
                MaxStack = 1,
                IsBound = true
            };
        }

        public static List<LootEntry> Entries(params LootEntry[] entries)
        {
            return new List<LootEntry>(entries);
        }
    }
}
