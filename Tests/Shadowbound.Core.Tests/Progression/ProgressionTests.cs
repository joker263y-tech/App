using System.Collections.Generic;
using Shadowbound.Core.Combat;
using Shadowbound.Core.Progression;
using Shadowbound.Core.Stats;
using Shadowbound.Core.Tests.Support;
using Xunit;

namespace Shadowbound.Core.Tests.Progression
{
    public class ExperienceCurveTests
    {
        [Fact]
        public void LevelForZeroExperience_IsOne()
        {
            var curve = new ExperienceCurve();

            Assert.Equal(1, curve.LevelForExperience(0));
            Assert.Equal(1, curve.LevelForExperience(-500));
        }

        [Fact]
        public void FirstLevelCost_EqualsTheBaseExperience()
        {
            var curve = new ExperienceCurve(30, 120f, 1.55f);

            Assert.Equal(120, curve.ExperienceForNextLevel(1));
        }

        [Fact]
        public void EachLevelCostsMoreThanTheLast()
        {
            // A curve that flattened or inverted would make later levels easier
            // than earlier ones, which would break the whole progression pacing.
            var curve = new ExperienceCurve();

            for (int level = 1; level < curve.LevelCap - 1; level++)
            {
                Assert.True(curve.ExperienceForNextLevel(level + 1) > curve.ExperienceForNextLevel(level));
            }
        }

        [Fact]
        public void TotalExperienceAtLevel_RoundTripsBackToThatLevel()
        {
            var curve = new ExperienceCurve();

            for (int level = 2; level <= curve.LevelCap; level++)
            {
                int total = curve.TotalExperienceAtLevel(level);
                Assert.Equal(level, curve.LevelForExperience(total));
            }
        }

        [Fact]
        public void OneExperienceShortOfALevel_IsStillBelowIt()
        {
            var curve = new ExperienceCurve();

            for (int level = 2; level <= 10; level++)
            {
                int total = curve.TotalExperienceAtLevel(level);
                Assert.Equal(level - 1, curve.LevelForExperience(total - 1));
            }
        }

        [Fact]
        public void LevelIsCappedHoweverMuchExperienceIsGranted()
        {
            var curve = new ExperienceCurve(5, 100f, 1.5f);

            Assert.Equal(5, curve.LevelForExperience(int.MaxValue));
        }

        [Fact]
        public void AtTheCap_NoFurtherExperienceIsRequested()
        {
            var curve = new ExperienceCurve(5, 100f, 1.5f);

            Assert.Equal(0, curve.ExperienceForNextLevel(5));
            Assert.Equal(0, curve.ExperienceForNextLevel(50));
        }

        [Fact]
        public void TotalExperienceAtLevelOne_IsZero()
        {
            Assert.Equal(0, new ExperienceCurve().TotalExperienceAtLevel(1));
            Assert.Equal(0, new ExperienceCurve().TotalExperienceAtLevel(0));
        }

        [Fact]
        public void Curve_RejectsDegenerateParameters()
        {
            var curve = new ExperienceCurve(0, -5f, 0f);

            Assert.Equal(1, curve.LevelCap);
            Assert.True(curve.BaseExperience > 0f);
            Assert.True(curve.Exponent > 0f);
        }
    }

    public class ProgressionSystemTests
    {
        private static ProgressionSystem MakeSystem(
            out Combatant combatant,
            out ExperienceCurve curve,
            float maxHealth = 100f,
            float attackPower = 100f)
        {
            combatant = CombatantFactory.Create(
                "hero",
                Faction.Player,
                maxHealth: maxHealth,
                attackPower: attackPower);

            curve = new ExperienceCurve();

            var growth = new[]
            {
                new StatGrowth(StatId.MaxHealth, 20f),
                new StatGrowth(StatId.AttackPower, 5f),
                new StatGrowth(StatId.ShadowPower, 4f)
            };

            return new ProgressionSystem(combatant, curve, growth);
        }

        [Fact]
        public void ANewCharacter_IsLevelOneWithNoExperience()
        {
            ProgressionSystem system = MakeSystem(out _, out _);

            Assert.Equal(1, system.Level);
            Assert.Equal(0, system.TotalExperience);
            Assert.Equal(0, system.UnspentAttributePoints);
            Assert.Equal(0f, system.LevelProgress, 3);
        }

        [Fact]
        public void InsufficientExperience_DoesNotLevelUp()
        {
            ProgressionSystem system = MakeSystem(out _, out ExperienceCurve curve);

            int gained = system.AddExperience(curve.ExperienceForNextLevel(1) - 1);

            Assert.Equal(0, gained);
            Assert.Equal(1, system.Level);
        }

        [Fact]
        public void NonPositiveExperience_IsIgnored()
        {
            ProgressionSystem system = MakeSystem(out _, out _);

            Assert.Equal(0, system.AddExperience(0));
            Assert.Equal(0, system.AddExperience(-100));
            Assert.Equal(0, system.TotalExperience);
        }

        [Fact]
        public void ReachingTheThreshold_LevelsUpAndAppliesGrowth()
        {
            ProgressionSystem system = MakeSystem(out Combatant combatant, out ExperienceCurve curve);

            int gained = system.AddExperience(curve.ExperienceForNextLevel(1));

            Assert.Equal(1, gained);
            Assert.Equal(2, system.Level);
            Assert.Equal(2, combatant.Level);
            Assert.Equal(120f, combatant.Vitals.MaxHealth, 3);
            Assert.Equal(105f, combatant.Stats.Get(StatId.AttackPower), 3);
        }

        [Fact]
        public void LevellingUp_GrantsTheHealthItsNewMaximumIsWorth()
        {
            // Otherwise gaining a level would raise the ceiling while leaving the
            // current value alone, and the player would appear to lose health at
            // the exact moment they were rewarded.
            ProgressionSystem system = MakeSystem(out Combatant combatant, out ExperienceCurve curve);

            system.AddExperience(curve.ExperienceForNextLevel(1));

            Assert.Equal(120f, combatant.Vitals.Health, 3);
        }

        [Fact]
        public void LevellingUp_HealsOnlyTheDeltaWhenAlreadyWounded()
        {
            ProgressionSystem system = MakeSystem(out Combatant combatant, out ExperienceCurve curve);
            combatant.Vitals.ApplyDamage(50f, null);

            system.AddExperience(curve.ExperienceForNextLevel(1));

            // 50 health, plus the 20 the new maximum is worth.
            Assert.Equal(70f, combatant.Vitals.Health, 3);
        }

        [Fact]
        public void AGrantCanCrossSeveralLevelsAtOnce()
        {
            ProgressionSystem system = MakeSystem(out _, out ExperienceCurve curve);

            int gained = system.AddExperience(curve.TotalExperienceAtLevel(4));

            Assert.Equal(3, gained);
            Assert.Equal(4, system.Level);
        }

        [Fact]
        public void LeveledUp_FiresOnceForEachLevelGained()
        {
            ProgressionSystem system = MakeSystem(out _, out ExperienceCurve curve);
            var seen = new List<int>();
            system.LeveledUp += seen.Add;

            system.AddExperience(curve.TotalExperienceAtLevel(4));

            Assert.Equal(new[] { 2, 3, 4 }, seen);
        }

        [Fact]
        public void EachLevel_GrantsOneAttributePoint()
        {
            ProgressionSystem system = MakeSystem(out _, out ExperienceCurve curve);

            system.AddExperience(curve.TotalExperienceAtLevel(4));

            Assert.Equal(3, system.UnspentAttributePoints);
        }

        [Fact]
        public void SpendingAnAttributePoint_RaisesTheStatAndConsumesThePoint()
        {
            ProgressionSystem system = MakeSystem(out Combatant combatant, out ExperienceCurve curve);
            system.AddExperience(curve.ExperienceForNextLevel(1));

            float before = combatant.Stats.Get(StatId.Armor);
            bool spent = system.TrySpendAttributePoint(StatId.Armor, 7f);

            Assert.True(spent);
            Assert.Equal(before + 7f, combatant.Stats.Get(StatId.Armor), 3);
            Assert.Equal(0, system.UnspentAttributePoints);
        }

        [Fact]
        public void SpendingWithoutPoints_IsRefused()
        {
            ProgressionSystem system = MakeSystem(out Combatant combatant, out _);

            float before = combatant.Stats.Get(StatId.Armor);
            bool spent = system.TrySpendAttributePoint(StatId.Armor, 7f);

            Assert.False(spent);
            Assert.Equal(before, combatant.Stats.Get(StatId.Armor), 3);
        }

        [Fact]
        public void SpendingANonPositiveAmount_IsRefused()
        {
            ProgressionSystem system = MakeSystem(out _, out ExperienceCurve curve);
            system.AddExperience(curve.ExperienceForNextLevel(1));

            Assert.False(system.TrySpendAttributePoint(StatId.Armor, 0f));
            Assert.Equal(1, system.UnspentAttributePoints);
        }

        [Fact]
        public void LevelProgress_TracksTheCurrentLevel()
        {
            ProgressionSystem system = MakeSystem(out _, out ExperienceCurve curve);
            int needed = curve.ExperienceForNextLevel(1);

            system.AddExperience(needed / 2);

            Assert.Equal(0.5f, system.LevelProgress, 2);
            Assert.Equal(needed / 2, system.ExperienceIntoLevel);
        }

        [Fact]
        public void AtTheLevelCap_ProgressReportsComplete()
        {
            ProgressionSystem system = MakeSystem(out _, out ExperienceCurve curve);

            system.AddExperience(int.MaxValue);

            Assert.True(system.IsAtLevelCap);
            Assert.Equal(1f, system.LevelProgress, 3);
            Assert.Equal(0, system.ExperienceForNextLevel);
        }

        [Fact]
        public void LoadFrom_ReappliesGrowthForTheSavedLevel()
        {
            // Growth is recomputed rather than trusted from the save, so a change
            // to the growth table applies to existing characters too.
            ProgressionSystem system = MakeSystem(out Combatant combatant, out ExperienceCurve curve);

            system.LoadFrom(curve.TotalExperienceAtLevel(5), unspentPoints: 2);

            Assert.Equal(5, system.Level);
            Assert.Equal(2, system.UnspentAttributePoints);
            Assert.Equal(180f, combatant.Vitals.MaxHealth, 3);
            Assert.Equal(120f, combatant.Stats.Get(StatId.AttackPower), 3);
            Assert.Equal(5, combatant.Level);
        }

        [Fact]
        public void LoadFrom_WithZeroExperience_LeavesTheCharacterAtLevelOne()
        {
            ProgressionSystem system = MakeSystem(out Combatant combatant, out _);

            system.LoadFrom(0, 0);

            Assert.Equal(1, system.Level);
            Assert.Equal(100f, combatant.Vitals.MaxHealth, 3);
        }
    }
}
