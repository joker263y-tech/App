#include "SbTest.h"

#include "SbStats.h"

using ShadowboundCore::ModifierOp;
using ShadowboundCore::StatId;
using ShadowboundCore::StatIds;
using ShadowboundCore::StatModifier;
using ShadowboundCore::StatSet;

TEST(Stats_DegenerateDefaultsAreSeeded)
{
	StatSet stats;

	// Zero would be a degenerate value rather than a neutral one: a
	// CooldownRate of 0 freezes every cooldown, a CritMultiplier of 0 makes
	// crits deal nothing. The port keeps the C# seed values.
	CHECK_NEAR(stats.Base(StatId::CooldownRate), 1.f, 1e-6f);
	CHECK_NEAR(stats.Base(StatId::CritMultiplier), 1.5f, 1e-6f);
	CHECK_NEAR(stats.Base(StatId::MaxHealth), 0.f, 1e-6f);
}

TEST(Stats_ResolutionOrderIsFlatThenPercentThenMultiply)
{
	StatSet stats;
	stats.SetBase(StatId::AttackPower, 100.f);
	stats.AddModifier(StatModifier::MakeFlat(StatId::AttackPower, 20.f));
	stats.AddModifier(StatModifier::MakePercent(StatId::AttackPower, 0.10f));
	stats.AddModifier(StatModifier::MakeMultiply(StatId::AttackPower, 0.50f));

	// ((100 + 20) * (1 + 0.10)) * (1 + 0.50) = 198
	CHECK_NEAR(stats.Get(StatId::AttackPower), 198.f, 1e-3f);
}

TEST(Stats_ResolvedValueClampsAtZero)
{
	StatSet stats;
	stats.SetBase(StatId::MaxHealth, -10.f);
	CHECK_NEAR(stats.Get(StatId::MaxHealth), 0.f, 1e-6f);
}

TEST(Stats_ModifiersAreRemovedBySource)
{
	StatSet stats;
	stats.SetBase(StatId::Armor, 10.f);

	const int sourceA = 1;
	const int sourceB = 2;

	stats.AddModifier(StatModifier::MakeFlat(StatId::Armor, 5.f, &sourceA));
	stats.AddModifier(StatModifier::MakeFlat(StatId::Armor, 100.f, &sourceB));
	CHECK_EQ(stats.ModifierCount(), 2);
	CHECK_NEAR(stats.Get(StatId::Armor), 115.f, 1e-3f);

	// Removing one source must not strip the other's contribution.
	const int removed = stats.RemoveModifiersFrom(&sourceA);
	CHECK_EQ(removed, 1);
	CHECK_NEAR(stats.Get(StatId::Armor), 110.f, 1e-3f);
}

TEST(Stats_NoOpModifiersAreIgnored)
{
	StatSet stats;
	stats.AddModifier(StatModifier(StatId::Armor, ModifierOp::Flat, 0.f, nullptr));
	CHECK_EQ(stats.ModifierCount(), 0);
}

TEST(Stats_CacheFollowsStackChanges)
{
	StatSet stats;
	stats.SetBase(StatId::MoveSpeed, 5.f);
	CHECK_NEAR(stats.Get(StatId::MoveSpeed), 5.f, 1e-6f);

	stats.AddModifier(StatModifier::MakeFlat(StatId::MoveSpeed, 1.5f));
	CHECK_NEAR(stats.Get(StatId::MoveSpeed), 6.5f, 1e-6f);

	stats.ClearModifiers();
	CHECK_NEAR(stats.Get(StatId::MoveSpeed), 5.f, 1e-6f);
}

TEST(Stats_MetadataMatchesTheOriginalLabels)
{
	CHECK_EQ(std::string(StatIds::Name(StatId::Armor)), std::string("Ward"));
	CHECK_EQ(std::string(StatIds::Name(StatId::ShadowPower)), std::string("Umbra"));
	CHECK(StatIds::IsFraction(StatId::CritChance));
	CHECK(!StatIds::IsFraction(StatId::AttackPower));
	CHECK_EQ(StatIds::Count, 12);
}
