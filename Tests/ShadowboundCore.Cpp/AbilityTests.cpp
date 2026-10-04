#include "SbTest.h"

#include "SbAttackResolver.h"
#include "SbCombatant.h"
#include "SbRng.h"

using namespace ShadowboundCore;

namespace
{
std::shared_ptr<AbilityDefinition> MakeMelee(const char* id = "test-melee")
{
	auto ability = std::make_shared<AbilityDefinition>();
	ability->Id = id;
	ability->Kind = AbilityKind::Melee;
	ability->DamageType = EDamageType::Physical;
	ability->WindupSeconds = 0.2f;
	ability->RecoverySeconds = 0.2f;
	ability->CooldownSeconds = 1.f;
	ability->Range = 3.f;
	ability->ConeHalfAngleDegrees = 90.f;
	ability->DamageMultiplier = 1.f;
	ability->StaminaCost = 0.f;
	return ability;
}

std::unique_ptr<Combatant> MakeFighter(const char* id, Faction faction, float attack = 20.f)
{
	auto combatant = std::make_unique<Combatant>(id, faction, 1);
	combatant->Stats().SetBase(StatId::MaxHealth, 100.f);
	combatant->Stats().SetBase(StatId::MaxStamina, 100.f);
	combatant->Stats().SetBase(StatId::AttackPower, attack);
	combatant->Stats().SetBase(StatId::MoveSpeed, 5.f);
	combatant->Vitals().ResetToFull();
	return combatant;
}
} // namespace

TEST(Abilities_CanActivateReportsThePreciseReason)
{
	auto player = MakeFighter("player", Faction::Player);
	auto enemy = MakeFighter("enemy", Faction::Hostile);

	auto ability = MakeMelee();
	ability->StaminaCost = 20.f;
	std::vector<std::shared_ptr<AbilityDefinition>> kit{ ability };

	AbilityController controller(*player, kit);

	CHECK_EQ(static_cast<int>(controller.CanActivate(0)),
		static_cast<int>(AbilityFailure::None));
	CHECK_EQ(static_cast<int>(controller.CanActivate(7)),
		static_cast<int>(AbilityFailure::UnknownAbility));

	// Busy while winding up beats every other reason.
	AbilityFailure failure;
	CHECK(controller.TryActivate(0, failure));
	CHECK_EQ(static_cast<int>(controller.CanActivate(0)),
		static_cast<int>(AbilityFailure::Busy));

	// The cast finishes - and this tick reports the landing, exactly once.
	CHECK_EQ(controller.Tick(0.5f), 0);
	CHECK(!controller.IsBusy());
	CHECK(controller.CooldownRemaining(0) > 0.f);
	CHECK_EQ(static_cast<int>(controller.CanActivate(0)),
		static_cast<int>(AbilityFailure::OnCooldown));

	// Stunned beats everything else.
	controller.ResetCooldowns();
	player->Statuses().Apply(StatusEffect::Modifier(StatusKind::Staggered, 1.f, 1.f, nullptr));
	CHECK_EQ(static_cast<int>(controller.CanActivate(0)),
		static_cast<int>(AbilityFailure::Stunned));
	player->Statuses().Clear();

	// Dead refuses everything.
	player->Vitals().ApplyDamage(1000.f, nullptr);
	CHECK_EQ(static_cast<int>(controller.CanActivate(0)),
		static_cast<int>(AbilityFailure::Dead));
	(void)enemy;
}

TEST(Abilities_StaminaIsSpentAtActivationNotAtImpact)
{
	auto player = MakeFighter("player", Faction::Player);

	auto ability = MakeMelee();
	ability->StaminaCost = 30.f;
	ability->WindupSeconds = 0.5f;
	std::vector<std::shared_ptr<AbilityDefinition>> kit{ ability };

	AbilityController controller(*player, kit);

	AbilityFailure failure;
	CHECK(controller.TryActivate(0, failure));
	// Paid before the blow lands, so dying mid-windup cannot dodge the cost.
	CHECK_NEAR(player->Vitals().Stamina(), 70.f, 1e-4f);

	// A failed activation (pool below the cost) leaves the pool untouched.
	AbilityController fresh(*player, kit);
	player->Stats().SetBase(StatId::MaxStamina, 10.f);
	player->Vitals().ResetToFull();
	CHECK_NEAR(player->Vitals().Stamina(), 10.f, 1e-4f);
	CHECK(!fresh.TryActivate(0, failure));
	CHECK_EQ(static_cast<int>(failure), static_cast<int>(AbilityFailure::NotEnoughStamina));
	CHECK_NEAR(player->Vitals().Stamina(), 10.f, 1e-4f);
}

TEST(Abilities_WindupLandsExactlyOnceThenRecovers)
{
	auto player = MakeFighter("player", Faction::Player);
	auto ability = MakeMelee();
	ability->WindupSeconds = 0.2f;
	ability->RecoverySeconds = 0.2f;
	std::vector<std::shared_ptr<AbilityDefinition>> kit{ ability };

	AbilityController controller(*player, kit);
	AbilityFailure failure;
	CHECK(controller.TryActivate(0, failure));
	CHECK(controller.IsBusy());
	CHECK_EQ(static_cast<int>(controller.Phase()), static_cast<int>(CastPhase::Windup));

	// Halfway through the windup: nothing has landed yet.
	CHECK_EQ(controller.Tick(0.1f), -1);

	// The next tick crosses the windup boundary: the blow lands once.
	CHECK_EQ(controller.Tick(0.15f), 0);
	CHECK_EQ(static_cast<int>(controller.Phase()), static_cast<int>(CastPhase::Recovery));

	// And never a second time for one activation.
	CHECK_EQ(controller.Tick(0.3f), -1);
	CHECK(!controller.IsBusy());
	CHECK_EQ(static_cast<int>(controller.Phase()), static_cast<int>(CastPhase::Ready));
}

TEST(Abilities_InterruptCancelsTheWindupWithoutLanding)
{
	auto player = MakeFighter("player", Faction::Player);
	auto ability = MakeMelee();
	ability->WindupSeconds = 0.5f;
	std::vector<std::shared_ptr<AbilityDefinition>> kit{ ability };

	AbilityController controller(*player, kit);
	AbilityFailure failure;
	CHECK(controller.TryActivate(0, failure));
	CHECK(controller.Interrupt());
	CHECK(!controller.IsBusy());

	// Time passes with no cast in flight: nothing lands.
	CHECK_EQ(controller.Tick(1.f), -1);
	// Interrupting an idle controller reports nothing to interrupt.
	CHECK(!controller.Interrupt());
}

TEST(Abilities_ZeroWindupLandsOnTheNextTickNotAtActivation)
{
	auto player = MakeFighter("player", Faction::Player);
	auto ability = MakeMelee();
	ability->WindupSeconds = 0.f;
	ability->RecoverySeconds = 0.1f;
	std::vector<std::shared_ptr<AbilityDefinition>> kit{ ability };

	AbilityController controller(*player, kit);
	AbilityFailure failure;
	CHECK(controller.TryActivate(0, failure));
	// Tick clears the landed flag first, so activation itself cannot report
	// a landing - an instant ability would otherwise silently never hit.
	CHECK_EQ(controller.Tick(0.05f), 0);
}

TEST(Abilities_CooldownRateScalesRecovery)
{
	auto player = MakeFighter("player", Faction::Player);
	auto ability = MakeMelee();
	ability->CooldownSeconds = 4.f;
	ability->WindupSeconds = 0.f;
	ability->RecoverySeconds = 0.f;
	std::vector<std::shared_ptr<AbilityDefinition>> kit{ ability };

	AbilityController controller(*player, kit);
	AbilityFailure failure;
	CHECK(controller.TryActivate(0, failure));
	CHECK(controller.CooldownRemaining(0) > 3.9f);

	// Haste (CooldownRate 2) halves the wait.
	player->Stats().SetBase(StatId::CooldownRate, 2.f);
	controller.Tick(1.f);
	CHECK(controller.CooldownRemaining(0) < 2.1f);
	CHECK(controller.CooldownRemaining(0) > 1.9f);
}

TEST(Abilities_RootDuringWindupUsesMoveSpeedAllowance)
{
	auto player = MakeFighter("player", Faction::Player);
	auto ability = MakeMelee();
	ability->WindupSeconds = 0.3f;
	ability->MoveSpeedDuringWindup = 0.4f;
	std::vector<std::shared_ptr<AbilityDefinition>> kit{ ability };

	AbilityController controller(*player, kit);

	// Ready: full speed.
	CHECK_NEAR(controller.MoveSpeedMultiplier(), 1.f, 1e-6f);

	AbilityFailure failure;
	CHECK(controller.TryActivate(0, failure));
	// Winding up: only the ability's allowance.
	CHECK_NEAR(controller.MoveSpeedMultiplier(), 0.4f, 1e-6f);
}

TEST(Resolver_TargetSelectionRespectsRangeConeAndLoyalty)
{
	auto player = MakeFighter("player", Faction::Player);
	auto nearEnemy = MakeFighter("near", Faction::Hostile);
	auto farEnemy = MakeFighter("far", Faction::Hostile);
	auto ally = MakeFighter("ally", Faction::Player);

	nearEnemy->SetPosition(Float3(0.f, 0.f, 2.f));
	farEnemy->SetPosition(Float3(0.f, 0.f, 30.f));   // out of range
	ally->SetPosition(Float3(0.f, 0.f, 1.5f));       // not hostile
	player->SetPosition(Float3::Zero);
	player->SetFacing(0.f); // facing +Z

	auto ability = MakeMelee();
	ability->Range = 5.f;
	ability->ConeHalfAngleDegrees = 60.f;
	ability->MaxTargets = 4;

	std::vector<Combatant*> candidates{ player.get(), nearEnemy.get(), farEnemy.get(), ally.get() };
	std::vector<Combatant*> hits;

	AttackResolver::SelectTargets(*player, *ability, candidates, hits);
	CHECK_EQ(static_cast<int>(hits.size()), 1);
	CHECK(hits[0] == nearEnemy.get());

	// A target behind the attacker is outside the cone.
	nearEnemy->SetPosition(Float3(0.f, 0.f, -2.f));
	AttackResolver::SelectTargets(*player, *ability, candidates, hits);
	CHECK_EQ(static_cast<int>(hits.size()), 0);
}

TEST(Resolver_MaxTargetsKeepsTheNearestFirst)
{
	auto player = MakeFighter("player", Faction::Player);
	player->SetPosition(Float3::Zero);
	player->SetFacing(0.f);

	auto a = MakeFighter("a", Faction::Hostile);
	auto b = MakeFighter("b", Faction::Hostile);
	auto c = MakeFighter("c", Faction::Hostile);
	a->SetPosition(Float3(0.f, 0.f, 1.f));
	b->SetPosition(Float3(0.f, 0.f, 4.f));
	c->SetPosition(Float3(0.f, 0.f, 2.f));

	auto ability = MakeMelee();
	ability->Range = 10.f;
	ability->ConeHalfAngleDegrees = 90.f;
	ability->MaxTargets = 2;

	std::vector<Combatant*> candidates{ a.get(), b.get(), c.get() };
	std::vector<Combatant*> hits;

	AttackResolver::SelectTargets(*player, *ability, candidates, hits);
	CHECK_EQ(static_cast<int>(hits.size()), 2);
	// Nearest-first ordering: the far one was replaced when a nearer
	// candidate arrived.
	CHECK(hits[0] == a.get());
	CHECK(hits[1] == c.get());
}

TEST(Resolver_StrikeAppliesArmourAndKnockback)
{
	auto player = MakeFighter("player", Faction::Player, /*attack*/ 50.f);
	player->SetPosition(Float3::Zero);

	auto enemy = MakeFighter("enemy", Faction::Hostile);
	enemy->SetPosition(Float3(0.f, 0.f, 2.f));
	enemy->Stats().SetBase(StatId::Armor, 100.f); // 100 armour vs level 1

	auto ability = MakeMelee();
	ability->DamageMultiplier = 1.f;
	ability->KnockbackSpeed = 6.f;
	ability->Variance = 0.f; // exact assertion, so no spread

	DeterministicRng rng(3ULL);
	const DamageResult result = AttackResolver::Strike(*player, *ability, *enemy, &rng);

	// 100 armour against a level-1 attacker mitigates 100/(100+108) - the
	// documented diminishing-returns curve.
	const float expected = 50.f * (108.f / 208.f);
	CHECK_NEAR(result.Applied, expected, 1e-2f);
	CHECK_NEAR(enemy->Vitals().Health(), 100.f - result.Applied, 1e-3f);

	// Knockback is an impulse, not a teleport: it is recorded here and
	// integrated by the simulation's movement phase on the next step.
	CHECK_NEAR(enemy->KnockbackSpeed(), 6.f, 1e-4f);
	CHECK(enemy->KnockbackDirection().Z > 0.9f); // pushed away from the attacker
}

TEST(Resolver_SelfStaggerIsPaidOncePerSwingNotPerTarget)
{
	auto player = MakeFighter("player", Faction::Player);
	player->SetPosition(Float3::Zero);
	player->SetFacing(0.f);

	auto e1 = MakeFighter("e1", Faction::Hostile);
	auto e2 = MakeFighter("e2", Faction::Hostile);
	e1->SetPosition(Float3(0.f, 0.f, 1.f));
	e2->SetPosition(Float3(0.f, 0.f, 2.f));

	auto ability = MakeMelee();
	ability->Kind = AbilityKind::Cleave;
	ability->Range = 10.f;
	ability->ConeHalfAngleDegrees = 160.f;
	ability->MaxTargets = 4;
	ability->SelfStaggerSeconds = 0.5f;

	DeterministicRng rng(4ULL);
	std::vector<Combatant*> candidates{ e1.get(), e2.get() };
	std::vector<Combatant*> hits;

	const int count = AttackResolver::Resolve(*player, *ability, candidates, &rng, hits);
	CHECK_EQ(count, 2);
	CHECK(player->IsStunned());
}

TEST(Resolver_DashMovesAlongFacingAndDamagesNothing)
{
	auto player = MakeFighter("player", Faction::Player);
	player->SetPosition(Float3::Zero);
	player->SetFacing(90.f); // facing +X

	auto enemy = MakeFighter("enemy", Faction::Hostile);
	enemy->SetPosition(Float3(5.f, 0.f, 0.f));

	auto ability = MakeMelee();
	ability->Kind = AbilityKind::Dash;
	ability->DashDistance = 4.f;
	ability->DamageMultiplier = 0.f;

	DeterministicRng rng(5ULL);
	std::vector<Combatant*> candidates{ enemy.get() };
	std::vector<Combatant*> hits;

	const int count = AttackResolver::Resolve(*player, *ability, candidates, &rng, hits);
	CHECK_EQ(count, 0);
	CHECK_NEAR(player->Position().X, 4.f, 1e-4f);
	CHECK_NEAR(enemy->Vitals().Health(), 100.f, 1e-4f);
}

TEST(Resolver_OnHitStatusChanceIsRespected)
{
	auto player = MakeFighter("player", Faction::Player);
	auto enemy = MakeFighter("enemy", Faction::Hostile);

	enemy->SetPosition(Float3(0.f, 0.f, 1.f)); // inside the melee cone
	auto always = MakeMelee();
	always->OnHitStatuses.emplace_back(StatusKind::Marked, 0.2f, 5.f, 0.f, 1, 1.f);

	auto never = MakeMelee();
	never->OnHitStatuses.emplace_back(StatusKind::Marked, 0.2f, 5.f, 0.f, 1, 0.f);

	DeterministicRng rng(6ULL);
	std::vector<Combatant*> candidates{ enemy.get() };
	std::vector<Combatant*> hits;

	(void)enemy;

	AttackResolver::Resolve(*player, *always, candidates, &rng, hits);
	CHECK(enemy->Statuses().Has(StatusKind::Marked));

	enemy->Statuses().Clear();
	AttackResolver::Resolve(*player, *never, candidates, &rng, hits);
	CHECK(!enemy->Statuses().Has(StatusKind::Marked));
}
