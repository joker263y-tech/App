#include "SbTest.h"

#include "SbDamage.h"
#include "SbRng.h"

using ShadowboundCore::DamageCalculator;
using ShadowboundCore::DamageRequest;
using ShadowboundCore::DamageResult;
using ShadowboundCore::DeterministicRng;
using ShadowboundCore::EDamageType;
using ShadowboundCore::ResistanceSet;

TEST(Damage_NoDefenceMeansNoLoss)
{
	const DamageRequest request(EDamageType::Physical, 100.f);
	const DamageResult result = DamageCalculator::ResolveDeterministic(request, 0.f, 0.f, 1.f, 1.f);
	CHECK_NEAR(result.Raw, 100.f, 1e-4f);
	CHECK_NEAR(result.Mitigated, 100.f, 1e-4f);
	CHECK_NEAR(result.Applied, 100.f, 1e-4f);
	CHECK(!result.Critical);
	CHECK_NEAR(result.ReductionFraction, 0.f, 1e-6f);
}

TEST(Damage_MitigationCurveGrowsWithArmorAndCaps)
{
	// No armour means no mitigation.
	CHECK_NEAR(DamageCalculator::MitigationFraction(0.f, 0.f, 1), 0.f, 1e-6f);

	// Penetration that eats all armour leaves nothing to mitigate with.
	CHECK_NEAR(DamageCalculator::MitigationFraction(50.f, 50.f, 1), 0.f, 1e-6f);

	// The curve is diminishing: armour equal to the level-scaled constant
	// lands near (but below) 50%, exactly as the formula specifies.
	const float atConstant = DamageCalculator::MitigationFraction(108.f, 0.f, 1);
	CHECK_NEAR(atConstant, 108.f / (108.f + 108.f), 1e-4f);

	// The hard cap keeps armour alone from making a target unkillable.
	CHECK_NEAR(DamageCalculator::MitigationFraction(1000000.f, 0.f, 1),
		DamageCalculator::MaxMitigation, 1e-6f);
}

TEST(Damage_HigherAttackerLevelDevaluesFlatArmour)
{
	const float vsLevel1 = DamageCalculator::MitigationFraction(100.f, 0.f, 1);
	const float vsLevel10 = DamageCalculator::MitigationFraction(100.f, 0.f, 10);
	CHECK(vsLevel10 < vsLevel1);
}

TEST(Damage_ResistanceIsClampedBetweenDoubleDamageAndNinetyPercent)
{
	CHECK_NEAR(ResistanceSet::ClampResistance(-5.f), -1.f, 1e-6f);
	CHECK_NEAR(ResistanceSet::ClampResistance(5.f), 0.9f, 1e-6f);

	const DamageRequest request(EDamageType::Ember, 100.f);
	const DamageResult doubled = DamageCalculator::ResolveDeterministic(request, 0.f, -1.f, 1.f, 1.f);
	CHECK_NEAR(doubled.Applied, 200.f, 1e-3f);

	const DamageResult resisted = DamageCalculator::ResolveDeterministic(request, 0.f, 0.9f, 1.f, 1.f);
	CHECK_NEAR(resisted.Applied, 10.f, 1e-3f);
}

TEST(Damage_AttackerAndDefenderMultipliersCompose)
{
	const DamageRequest request(EDamageType::Physical, 100.f);
	const DamageResult result = DamageCalculator::ResolveDeterministic(request, 0.f, 0.f, 1.5f, 0.5f);
	CHECK_NEAR(result.Applied, 75.f, 1e-3f);
}

TEST(Damage_CritMultipliesTheAttackersIntent)
{
	DeterministicRng rng(11ULL);
	const DamageRequest request(EDamageType::Physical, 100.f, 0.f, /*critChance*/ 1.f,
		/*critMultiplier*/ 2.f, 1, /*variance*/ 0.f);

	const DamageResult result = DamageCalculator::Resolve(request, 0.f, 0.f, 1.f, 1.f, &rng);
	CHECK(result.Critical);
	CHECK_NEAR(result.Raw, 200.f, 1e-3f);
}

TEST(Damage_ZeroOrNegativeAmountResolvesToNone)
{
	const DamageRequest negative(EDamageType::Physical, -10.f);
	const DamageResult result = DamageCalculator::ResolveDeterministic(negative, 0.f, 0.f, 1.f, 1.f);
	CHECK(result.IsZero());

	CHECK(DamageResult::None.IsZero());
}

TEST(Damage_RequestConstructorClampsItsInputs)
{
	const DamageRequest request(EDamageType::Shadow, 10.f, 0.f, 0.f, 0.5f, 0, 5.f, 3.f);
	CHECK_NEAR(request.CritMultiplier, 1.f, 1e-6f);   // never below 1
	CHECK_EQ(request.AttackerLevel, 1);               // never below 1
	CHECK_NEAR(request.Variance, 0.9f, 1e-6f);        // capped
	CHECK_NEAR(request.ArmorPenetrationPercent, 1.f, 1e-6f); // capped
}

TEST(Damage_ExpectedDamageWeightsCritChance)
{
	const DamageRequest request(EDamageType::Physical, 100.f, 0.f, /*critChance*/ 0.5f,
		/*critMultiplier*/ 2.f, 1, 0.f);

	const float plain = DamageCalculator::ResolveDeterministic(request, 0.f, 0.f, 1.f, 1.f).Applied;
	const float expected = DamageCalculator::ExpectedDamage(request, 0.f, 0.f, 1.f, 1.f);

	CHECK_NEAR(plain, 100.f, 1e-3f);
	CHECK_NEAR(expected, 150.f, 1e-2f); // 100*0.5 + 200*0.5
}
