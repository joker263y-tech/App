using Shadowbound.Core.Stats;
using Xunit;

namespace Shadowbound.Core.Tests.Stats
{
    public class StatSetTests
    {
        [Fact]
        public void Get_WithNoModifiers_ReturnsBaseValue()
        {
            var stats = new StatSet(StatId.AttackPower, 25f);

            Assert.Equal(25f, stats.Get(StatId.AttackPower));
        }

        [Fact]
        public void Get_AppliesFlatBeforePercentages()
        {
            var stats = new StatSet(StatId.AttackPower, 100f);

            stats.AddModifier(StatModifier.Flat(StatId.AttackPower, 50f));
            stats.AddModifier(StatModifier.Percent(StatId.AttackPower, 0.5f));

            // (100 + 50) * 1.5, not (100 * 1.5) + 50.
            Assert.Equal(225f, stats.Get(StatId.AttackPower), 3);
        }

        [Fact]
        public void Get_CompoundsMultiplicativeModifiers()
        {
            var stats = new StatSet(StatId.AttackPower, 100f);

            stats.AddModifier(StatModifier.Multiply(StatId.AttackPower, 0.5f));
            stats.AddModifier(StatModifier.Multiply(StatId.AttackPower, 0.5f));

            // 100 * 1.5 * 1.5, distinct from a single additive +100%.
            Assert.Equal(225f, stats.Get(StatId.AttackPower), 3);
        }

        [Fact]
        public void Get_SumsAdditivePercentagesBeforeApplyingThem()
        {
            var stats = new StatSet(StatId.AttackPower, 100f);

            stats.AddModifier(StatModifier.Percent(StatId.AttackPower, 0.25f));
            stats.AddModifier(StatModifier.Percent(StatId.AttackPower, 0.25f));

            Assert.Equal(150f, stats.Get(StatId.AttackPower), 3);
        }

        [Fact]
        public void Get_ClampsAtZeroWhenDebuffsExceedBase()
        {
            var stats = new StatSet(StatId.MoveSpeed, 5f);

            stats.AddModifier(StatModifier.Flat(StatId.MoveSpeed, -100f));

            Assert.Equal(0f, stats.Get(StatId.MoveSpeed));
        }

        [Fact]
        public void Get_ReflectsModifiersAddedAfterABaseRead()
        {
            // Guards the dirty-flag cache: a stale cached value here would make
            // equipment swaps silently do nothing.
            var stats = new StatSet(StatId.Armor, 10f);
            float first = stats.Get(StatId.Armor);

            stats.AddModifier(StatModifier.Flat(StatId.Armor, 40f));

            Assert.Equal(10f, first);
            Assert.Equal(50f, stats.Get(StatId.Armor));
        }

        [Fact]
        public void RemoveModifiersFrom_RemovesOnlyThatSourcesContributions()
        {
            var stats = new StatSet(StatId.Armor, 10f);
            var sword = new object();
            var potion = new object();

            stats.AddModifier(StatModifier.Flat(StatId.Armor, 20f, sword));
            stats.AddModifier(StatModifier.Flat(StatId.Armor, 5f, potion));

            int removed = stats.RemoveModifiersFrom(sword);

            Assert.Equal(1, removed);
            Assert.Equal(15f, stats.Get(StatId.Armor), 3);
        }

        [Fact]
        public void RemoveModifiersFrom_WithUnknownSource_RemovesNothing()
        {
            var stats = new StatSet(StatId.Armor, 10f);
            stats.AddModifier(StatModifier.Flat(StatId.Armor, 20f, new object()));

            int removed = stats.RemoveModifiersFrom(new object());

            Assert.Equal(0, removed);
            Assert.Equal(30f, stats.Get(StatId.Armor), 3);
        }

        [Fact]
        public void SetBase_RecomputesWithoutLosingModifiers()
        {
            var stats = new StatSet(StatId.MaxHealth, 100f);
            stats.AddModifier(StatModifier.Flat(StatId.MaxHealth, 50f));

            stats.SetBase(StatId.MaxHealth, 200f);

            Assert.Equal(250f, stats.Get(StatId.MaxHealth), 3);
        }

        [Fact]
        public void ClearModifiers_LeavesBaseValuesIntact()
        {
            var stats = new StatSet(StatId.MaxHealth, 100f);
            stats.AddModifier(StatModifier.Flat(StatId.MaxHealth, 50f));

            stats.ClearModifiers();

            Assert.Equal(100f, stats.Get(StatId.MaxHealth), 3);
        }

        [Fact]
        public void StatIds_CountMatchesTheEnumeration()
        {
            // A mismatch would silently drop modifiers for the last stat.
            Assert.Equal(StatIds.Count, StatIds.All.Length);
            Assert.Equal(StatIds.Count - 1, (int)StatIds.All[StatIds.Count - 1]);
        }
    }
}
