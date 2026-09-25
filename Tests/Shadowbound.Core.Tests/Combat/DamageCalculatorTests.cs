using Shadowbound.Core.Combat;
using Shadowbound.Core.Randomness;
using Xunit;

namespace Shadowbound.Core.Tests.Combat
{
    public class DamageCalculatorTests
    {
        private static DamageRequest Request(
            float amount,
            DamageType type = DamageType.Physical,
            float armorPen = 0f,
            float critChance = 0f,
            float critMultiplier = 1.5f,
            int level = 1,
            float variance = 0f)
        {
            return new DamageRequest(type, amount, armorPen, critChance, critMultiplier, level, variance);
        }

        [Fact]
        public void ResolveDeterministic_WithNoDefence_AppliesFullDamage()
        {
            DamageResult result = DamageCalculator.ResolveDeterministic(
                Request(100f), 0f, 0f, 1f, 1f);

            Assert.Equal(100f, result.Applied, 3);
            Assert.Equal(0f, result.ReductionFraction, 3);
        }

        [Fact]
        public void ResolveDeterministic_WithZeroDamage_ReturnsNone()
        {
            DamageResult result = DamageCalculator.ResolveDeterministic(
                Request(0f), 50f, 0.5f, 1f, 1f);

            Assert.True(result.IsZero);
        }

        [Fact]
        public void MitigationFraction_AtArmorConstant_IsJustUnderHalf()
        {
            // armor == ArmorConstant against a level 1 attacker.
            float mitigation = DamageCalculator.MitigationFraction(DamageCalculator.ArmorConstant, 0f, 1);

            // 100 / (100 + 100 * 1.08) = 0.4808
            Assert.Equal(0.4808f, mitigation, 3);
        }

        [Fact]
        public void MitigationFraction_NeverExceedsTheCap()
        {
            float mitigation = DamageCalculator.MitigationFraction(1000000f, 0f, 1);

            Assert.Equal(DamageCalculator.MaxMitigation, mitigation, 5);
        }

        [Fact]
        public void MitigationFraction_AtOrBelowZeroArmor_IsZero()
        {
            Assert.Equal(0f, DamageCalculator.MitigationFraction(0f, 0f, 1));
            Assert.Equal(0f, DamageCalculator.MitigationFraction(10f, 10f, 1));
        }

        [Fact]
        public void MitigationFraction_FallsAsAttackerLevelRises()
        {
            // This is the knob that makes the same armor worth less later on.
            float low = DamageCalculator.MitigationFraction(200f, 0f, 1);
            float high = DamageCalculator.MitigationFraction(200f, 0f, 50);

            Assert.True(high < low, "Higher attacker level should reduce armor's value.");
        }

        /// <summary>
        /// Damage dealt with the given penetration, relative to dealing none.
        /// Comparing ratios rather than raw mitigation is the only fair way to
        /// compare penetration, because the mitigation curve is non-linear.
        /// </summary>
        private static float PenetrationGain(float armor, float flatPen, float percentPen)
        {
            var withPen = new DamageRequest(
                DamageType.Physical, 100f,
                armorPenetration: flatPen,
                armorPenetrationPercent: percentPen);
            var withoutPen = new DamageRequest(DamageType.Physical, 100f);

            float with = DamageCalculator.ResolveDeterministic(withPen, armor, 0f, 1f, 1f).Applied;
            float without = DamageCalculator.ResolveDeterministic(withoutPen, armor, 0f, 1f, 1f).Applied;

            return with / without;
        }

        [Fact]
        public void FlatArmorPenetration_IsWorthMoreAgainstLightArmor()
        {
            // Counter-intuitive but correct: mitigation uses a diminishing-returns
            // curve, so its slope is steepest at low armor. Removing a fixed 50
            // armor therefore destroys more mitigation on a lightly armoured
            // target than on a heavily armoured one.
            float lightGain = PenetrationGain(100f, 50f, 0f);
            float heavyGain = PenetrationGain(400f, 50f, 0f);

            Assert.True(lightGain > heavyGain,
                "Flat penetration should pay off most against light armor.");
            Assert.True(lightGain > 1f);
        }

        [Fact]
        public void PercentArmorPenetration_IsWorthMoreAgainstHeavyArmor()
        {
            // The complement of the test above, and the reason both stats exist:
            // a percentage cut scales with the target's armor, so it is the
            // answer to heavily armoured elites and bosses.
            float lightGain = PenetrationGain(100f, 0f, 0.5f);
            float heavyGain = PenetrationGain(400f, 0f, 0.5f);

            Assert.True(heavyGain > lightGain,
                "Percentage penetration should pay off most against heavy armor.");
        }

        [Fact]
        public void PercentPenetration_IsAppliedBeforeFlatPenetration()
        {
            // armor 400, cut 50% -> 200, then remove 100 flat -> 100.
            // Same effective armor as a naked 100-armor target.
            float combined = DamageCalculator.MitigationFraction(400f, 100f, 0.5f, 1);
            float equivalent = DamageCalculator.MitigationFraction(100f, 0f, 0f, 1);

            Assert.Equal(equivalent, combined, 4);
        }

        [Fact]
        public void PercentPenetration_IsClampedToOne()
        {
            // A value above 1 must not invert the target's armor into a bonus.
            float clamped = DamageCalculator.MitigationFraction(500f, 0f, 25f, 1);

            Assert.Equal(0f, clamped, 5);
        }

        [Fact]
        public void FullPenetration_RemovesMitigationEntirely()
        {
            Assert.Equal(0f, DamageCalculator.MitigationFraction(100f, 100f, 1), 5);
            Assert.Equal(0f, DamageCalculator.MitigationFraction(100f, 0f, 1f, 1), 5);
        }

        [Fact]
        public void Resistance_ReducesAppliedDamage()
        {
            DamageResult result = DamageCalculator.ResolveDeterministic(
                Request(100f, DamageType.Ember), 0f, 0.5f, 1f, 1f);

            Assert.Equal(50f, result.Applied, 3);
        }

        [Fact]
        public void NegativeResistance_IncreasesDamageUpToDouble()
        {
            DamageResult result = DamageCalculator.ResolveDeterministic(
                Request(100f), 0f, -1f, 1f, 1f);

            Assert.Equal(200f, result.Applied, 3);
        }

        [Fact]
        public void Resistance_CannotExceedFullImmunity()
        {
            // A naive implementation with resistance 5 would heal the target.
            var resistance = new ResistanceSet();
            resistance.Set(DamageType.Shadow, 5f);

            DamageResult result = DamageCalculator.ResolveDeterministic(
                Request(100f, DamageType.Shadow), 0f, resistance.Get(DamageType.Shadow), 1f, 1f);

            Assert.True(result.Applied > 0f);
            Assert.Equal(10f, result.Applied, 3);
        }

        [Fact]
        public void AttackerAndDefenderMultipliers_AreBothHonoured()
        {
            DamageResult result = DamageCalculator.ResolveDeterministic(
                Request(100f), 0f, 0f, attackerDamageMultiplier: 1.5f, defenderDamageTakenMultiplier: 0.5f);

            Assert.Equal(75f, result.Applied, 3);
        }

        [Fact]
        public void CriticalHit_MultipliesBeforeMitigation()
        {
            var rng = new DeterministicRng(1337);

            DamageResult result = DamageCalculator.Resolve(
                Request(100f, critChance: 1f, critMultiplier: 2f),
                defenderArmor: 100f,
                defenderResistance: 0f,
                attackerDamageMultiplier: 1f,
                defenderDamageTakenMultiplier: 1f,
                rng: rng);

            Assert.True(result.Critical);
            Assert.Equal(200f, result.Raw, 3);

            // Mitigation must apply to the crit value, not the base.
            float mitigation = DamageCalculator.MitigationFraction(100f, 0f, 1);
            Assert.Equal(200f * (1f - mitigation), result.Applied, 3);
        }

        [Fact]
        public void ChanceBasedCrit_DoesNotAlwaysCrit()
        {
            var rng = new DeterministicRng(4242);
            int crits = 0;

            for (int i = 0; i < 1000; i++)
            {
                DamageResult result = DamageCalculator.Resolve(
                    Request(100f, critChance: 0.25f), 0f, 0f, 1f, 1f, rng);

                if (result.Critical)
                {
                    crits++;
                }
            }

            // Expect ~250. Wide bounds: this asserts the roll is wired up, not
            // that the distribution is exact.
            Assert.InRange(crits, 180, 320);
        }

        [Fact]
        public void SameSeed_ProducesIdenticalDamageSequence()
        {
            var a = new DeterministicRng(99);
            var b = new DeterministicRng(99);

            for (int i = 0; i < 50; i++)
            {
                DamageResult left = DamageCalculator.Resolve(
                    Request(100f, critChance: 0.3f, variance: 0.2f), 50f, 0.1f, 1.2f, 0.9f, a);
                DamageResult right = DamageCalculator.Resolve(
                    Request(100f, critChance: 0.3f, variance: 0.2f), 50f, 0.1f, 1.2f, 0.9f, b);

                Assert.Equal(left.Applied, right.Applied, 5);
                Assert.Equal(left.Critical, right.Critical);
            }
        }

        [Fact]
        public void Variance_StaysWithinTheRequestedBand()
        {
            var rng = new DeterministicRng(7);

            for (int i = 0; i < 200; i++)
            {
                DamageResult result = DamageCalculator.Resolve(
                    Request(100f, variance: 0.2f), 0f, 0f, 1f, 1f, rng);

                Assert.InRange(result.Applied, 80f, 120f);
            }
        }

        [Fact]
        public void VarianceAlwaysProducesPositiveDamage()
        {
            // A variance band of 1.0 would go negative without the clamp.
            var rng = new DeterministicRng(11);
            var request = new DamageRequest(DamageType.Physical, 10f, 0f, 0f, 1.5f, 1, 5f);

            Assert.Equal(0.9f, request.Variance, 3);

            for (int i = 0; i < 100; i++)
            {
                DamageResult result = DamageCalculator.Resolve(request, 0f, 0f, 1f, 1f, rng);
                Assert.True(result.Applied > 0f);
            }
        }

        [Fact]
        public void ExpectedDamage_WithGuaranteedCrit_EqualsAResolvedCrit()
        {
            // A guaranteed crit for 100 base at 2x must be worth exactly the
            // damage of a plain 200 hit. ResolveDeterministic deliberately
            // ignores crits, so the crit is expressed as a doubled base value.
            DamageResult criticalHit = DamageCalculator.ResolveDeterministic(
                new DamageRequest(DamageType.Physical, 200f), 0f, 0f, 1f, 1f);

            float expected = DamageCalculator.ExpectedDamage(
                new DamageRequest(DamageType.Physical, 100f, 0f, 1f, 2f), 0f, 0f, 1f, 1f);

            Assert.Equal(200f, criticalHit.Applied, 3);
            Assert.Equal(200f, expected, 3);
        }

        [Fact]
        public void CritAdvantage_IsInvariantToArmorAndResistance()
        {
            // Mitigation and resistance are both multiplicative factors, so they
            // scale a crit and a normal hit by exactly the same amount. A crit
            // is therefore worth its full multiplier regardless of how well
            // defended the target is, which keeps crit and armor as independent
            // build axes rather than making armor a soft counter to crit.
            //
            // This is asserted rather than assumed because it is a property of
            // the formulae, not of the call order: if mitigation ever becomes a
            // flat subtraction, this test will fail and force the design to be
            // reconsidered deliberately.
            var crit = new DamageRequest(DamageType.Physical, 100f, 0f, 1f, 2f);
            var plain = new DamageRequest(DamageType.Physical, 100f, 0f, 0f, 2f);

            float unarmoured = DamageCalculator.ExpectedDamage(crit, 0f, 0f, 1f, 1f)
                / DamageCalculator.ExpectedDamage(plain, 0f, 0f, 1f, 1f);

            float armoured = DamageCalculator.ExpectedDamage(crit, 300f, 0f, 1f, 1f)
                / DamageCalculator.ExpectedDamage(plain, 300f, 0f, 1f, 1f);

            float resistant = DamageCalculator.ExpectedDamage(crit, 100f, 0.6f, 1f, 1f)
                / DamageCalculator.ExpectedDamage(plain, 100f, 0.6f, 1f, 1f);

            Assert.Equal(2f, unarmoured, 3);
            Assert.Equal(2f, armoured, 3);
            Assert.Equal(2f, resistant, 3);
        }

        [Fact]
        public void ExpectedDamage_CarriesPenetrationIntoTheCritCalculation()
        {
            // Guards against the crit branch silently dropping penetration.
            float expected = DamageCalculator.ExpectedDamage(
                new DamageRequest(DamageType.Physical, 100f, 300f, 1f, 2f), 300f, 0f, 1f, 1f);

            Assert.Equal(200f, expected, 3);
        }

        [Fact]
        public void ExpectedDamage_BlendsCritChanceLinearly()
        {
            float none = DamageCalculator.ExpectedDamage(
                new DamageRequest(DamageType.Physical, 100f, 0f, 0f, 2f, 1, 0f), 0f, 0f, 1f, 1f);
            float half = DamageCalculator.ExpectedDamage(
                new DamageRequest(DamageType.Physical, 100f, 0f, 0.5f, 2f, 1, 0f), 0f, 0f, 1f, 1f);
            float always = DamageCalculator.ExpectedDamage(
                new DamageRequest(DamageType.Physical, 100f, 0f, 1f, 2f, 1, 0f), 0f, 0f, 1f, 1f);

            Assert.Equal(100f, none, 3);
            Assert.Equal(150f, half, 3);
            Assert.Equal(200f, always, 3);
        }

        [Fact]
        public void ReductionFraction_ReportsCombinedDefence()
        {
            DamageResult result = DamageCalculator.ResolveDeterministic(
                Request(100f), 0f, 0.5f, 1f, 1f);

            Assert.Equal(0.5f, result.ReductionFraction, 3);
        }
    }
}
