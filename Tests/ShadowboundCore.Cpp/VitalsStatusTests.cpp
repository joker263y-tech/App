#include "SbTest.h"

#include "SbCombatant.h"
#include "SbStatusEffects.h"
#include "SbVitals.h"

using ShadowboundCore::Combatant;
using ShadowboundCore::EDamageType;
using ShadowboundCore::Faction;
using ShadowboundCore::ResistanceSet;
using ShadowboundCore::StatId;
using ShadowboundCore::StatSet;
using ShadowboundCore::StatusEffect;
using ShadowboundCore::StatusEffectSystem;
using ShadowboundCore::StatusKind;
using ShadowboundCore::StatusStackRule;
using ShadowboundCore::VitalsPool;

namespace
{
struct FVitalsFixture
{
	StatSet Stats;
	ResistanceSet Resistances;
	VitalsPool Vit;

	FVitalsFixture()
		: Vit(Stats, Resistances)
	{
		Stats.SetBase(StatId::MaxHealth, 100.f);
		Stats.SetBase(StatId::MaxStamina, 50.f);
		Stats.SetBase(StatId::HealthRegen, 2.f);
		Stats.SetBase(StatId::StaminaRegen, 5.f);
		Vit.ResetToFull();
	}
};
} // namespace

TEST(Vitals_DamageHealAndDeathAreExact)
{
	FVitalsFixture fixture;

	// Probe listeners, standing in for the presentation layer subscribing to
	// the core's events (the same pattern CombatantView used).
	struct FProbe
	{
		float LastAmount = 0.f;
		const Combatant* LastSource = nullptr;
		int DamagedCount = 0;
		bool Died = false;
	} probe;

	fixture.Vit.Damaged.Bind(&probe, [](void* ctx, float amount, const Combatant* source)
		{
			FProbe* p = static_cast<FProbe*>(ctx);
			p->LastAmount = amount;
			p->LastSource = source;
			p->DamagedCount++;
		});
	fixture.Vit.Died.Bind(&probe, [](void* ctx)
		{
			static_cast<FProbe*>(ctx)->Died = true;
		});

	const float applied = fixture.Vit.ApplyDamage(30.f, nullptr);
	CHECK_NEAR(applied, 30.f, 1e-4f);
	CHECK_NEAR(fixture.Vit.Health(), 70.f, 1e-4f);
	CHECK_NEAR(probe.LastAmount, 30.f, 1e-4f);
	CHECK_EQ(probe.DamagedCount, 1);

	// Overkill only removes what is there, and reports the real amount.
	const float overkill = fixture.Vit.ApplyDamage(1000.f, nullptr);
	CHECK_NEAR(overkill, 70.f, 1e-4f);
	CHECK(!fixture.Vit.IsAlive());
	CHECK(probe.Died);

	// A corpse takes no further damage: overkill cannot be re-counted.
	CHECK_NEAR(fixture.Vit.ApplyDamage(10.f, nullptr), 0.f, 1e-6f);
	CHECK_EQ(probe.DamagedCount, 2);

	// The dead cannot be healed back.
	CHECK_NEAR(fixture.Vit.Heal(50.f), 0.f, 1e-6f);
}

TEST(Vitals_HealClampsToMaximum)
{
	FVitalsFixture fixture;
	fixture.Vit.ApplyDamage(10.f, nullptr);

	const float restored = fixture.Vit.Heal(50.f);
	CHECK_NEAR(restored, 10.f, 1e-4f);
	CHECK_NEAR(fixture.Vit.Health(), 100.f, 1e-4f);
	CHECK_NEAR(fixture.Vit.Heal(10.f), 0.f, 1e-6f); // already full
}

TEST(Vitals_RegenFillsButNeverResurrects)
{
	FVitalsFixture fixture;
	fixture.Vit.ApplyDamage(20.f, nullptr);

	fixture.Vit.Tick(1.f);
	CHECK_NEAR(fixture.Vit.Health(), 82.f, 1e-3f); // 80 + regen 2

	// Death is terminal: a regen stat must not climb a corpse back to life.
	fixture.Vit.ApplyDamage(1000.f, nullptr);
	fixture.Vit.Tick(10.f);
	CHECK_NEAR(fixture.Vit.Health(), 0.f, 1e-6f);
}

TEST(Vitals_StaminaSpendingIsAllOrNothing)
{
	FVitalsFixture fixture;

	bool depleted = false;
	fixture.Vit.StaminaDepleted.Bind(&depleted, [](void* ctx)
		{
			*static_cast<bool*>(ctx) = true;
		});

	CHECK(fixture.Vit.TrySpendStamina(30.f));
	CHECK_NEAR(fixture.Vit.Stamina(), 20.f, 1e-4f);

	// A failed check leaves the pool untouched.
	CHECK(!fixture.Vit.TrySpendStamina(25.f));
	CHECK_NEAR(fixture.Vit.Stamina(), 20.f, 1e-4f);
	CHECK(depleted);

	CHECK(fixture.Vit.TrySpendStamina(0.f)); // free actions always succeed
}

TEST(Statuses_DamageOverTimeSumsAcrossStacks)
{
	FVitalsFixture fixture;
	StatusEffectSystem statuses(fixture.Stats, fixture.Vit);

	StatusEffect bleed = StatusEffect::Dot(StatusKind::Bleeding, EDamageType::Physical,
		5.f, 10.f, 1.f, nullptr, /*maxStacks*/ 3);
	statuses.Apply(bleed);
	statuses.Apply(bleed); // stacks to 2

	CHECK_EQ(statuses.StackCount(StatusKind::Bleeding), 2);

	// Two stacks of 5 per tick: after one second, exactly 10 health is gone.
	statuses.Tick(1.f);
	CHECK_NEAR(fixture.Vit.Health(), 90.f, 1e-3f);
}

TEST(Statuses_ModifiersTakeTheStrongestInstance)
{
	FVitalsFixture fixture;
	StatusEffectSystem statuses(fixture.Stats, fixture.Vit);

	statuses.Apply(StatusEffect::Modifier(StatusKind::Chilled, 0.3f, 5.f, nullptr));
	statuses.Apply(StatusEffect::Modifier(StatusKind::Chilled, 0.5f, 2.f, nullptr));

	// Strongest wins - not the sum, or a group of enemies would freeze the
	// player permanently.
	CHECK_NEAR(statuses.MoveSpeedMultiplier(), 0.5f, 1e-4f);
	// Chilled halves recovery in proportion to its magnitude.
	CHECK_NEAR(statuses.CooldownRateMultiplier(), 0.75f, 1e-4f);
}

TEST(Statuses_WardedAndMarkedCompose)
{
	FVitalsFixture fixture;
	StatusEffectSystem statuses(fixture.Stats, fixture.Vit);

	statuses.Apply(StatusEffect::Modifier(StatusKind::Warded, 0.4f, 5.f, nullptr));
	statuses.Apply(StatusEffect::Modifier(StatusKind::Marked, 0.25f, 5.f, nullptr));

	// (1 - 0.40) * (1 + 0.25) = 0.75
	CHECK_NEAR(statuses.DamageTakenMultiplier(), 0.75f, 1e-4f);
}

TEST(Statuses_DurationIsScaledByResolve)
{
	FVitalsFixture fixture;
	fixture.Stats.SetBase(StatId::StatusResistance, 0.5f); // 50% Resolve

	StatusEffectSystem statuses(fixture.Stats, fixture.Vit);
	statuses.Apply(StatusEffect::Modifier(StatusKind::Empowered, 0.2f, 8.f, nullptr));

	StatusEffect applied;
	CHECK(statuses.TryGet(StatusKind::Empowered, applied));
	CHECK_NEAR(applied.Duration, 4.f, 1e-4f); // halved by Resolve
}

TEST(Statuses_ExpireFiresAndClears)
{
	FVitalsFixture fixture;
	StatusEffectSystem statuses(fixture.Stats, fixture.Vit);

	int expiredCount = 0;
	statuses.Expired.Bind(&expiredCount, [](void* ctx, const StatusEffect&)
		{
			*static_cast<int*>(ctx) += 1;
		});

	statuses.Apply(StatusEffect::Modifier(StatusKind::Staggered, 1.f, 0.5f, nullptr));
	CHECK(statuses.IsStunned());

	statuses.Tick(1.f);
	CHECK(!statuses.IsStunned());
	CHECK_EQ(expiredCount, 1);
	CHECK_EQ(statuses.ActiveCount(), 0);
}

TEST(Statuses_StaggerBlocksActions)
{
	FVitalsFixture fixture;
	StatusEffectSystem statuses(fixture.Stats, fixture.Vit);

	statuses.Apply(StatusEffect::Modifier(StatusKind::Staggered, 1.f, 0.4f, nullptr));
	CHECK(statuses.IsStunned());
}
