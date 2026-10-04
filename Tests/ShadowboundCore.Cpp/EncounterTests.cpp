#include "SbTest.h"

#include "SbContent.h"
#include "SbEncounter.h"
#include "SbRng.h"

using namespace ShadowboundCore;
using namespace ShadowboundCore::Content;

namespace
{
/// A scripted driver standing in for the player input layer, exactly as the
/// C# suite's ScriptedDrivers do: close on the nearest hostile and swing
/// ability 0 whenever the combat controller allows it.
class FScriptedDriver : public ICombatantDriver
{
public:
	CombatIntent Decide(float /*deltaTime*/, Combatant& self,
		EncounterSimulation& world) override
	{
		CombatIntent intent;
		intent.ActivateAbility = true;
		intent.AbilityIndex = 0;

		Combatant* target = world.FindNearestHostile(&self, 1e30f);
		if (target != nullptr)
		{
			const float distance = Float3::DistanceXZ(self.Position(), target->Position());
			if (distance > 2.2f) // hold inside Ember Edge's 2.6 reach
			{
				intent.MoveDirection = (target->Position() - self.Position()).FlattenedXZ();
				intent.SpeedScale = 1.f;
			}
		}

		return intent;
	}
};

struct FEncounterPair
{
	std::unique_ptr<Combatant> Player;
	std::unique_ptr<Combatant> Enemy;
	std::unique_ptr<EncounterSimulation> Sim;
	FScriptedDriver Driver;

	explicit FEncounterPair(std::uint64_t seed, float enemyZ = 6.f)
	{
		Player = CreatePlayer();
		const EnemyArchetype* walker = FindArchetype(ArchetypeHollowWalker);
		Enemy = walker->Create("grey-walk/hollow-walker-0", Float3(0.f, 0.f, enemyZ), 1);

		Sim = std::make_unique<EncounterSimulation>(DeterministicRng(seed),
			WorldBounds::Square(38.f));
		Sim->AddDriven(Player.get(), BuildPlayerAbilities(), &Driver, true);
		Sim->AddEnemy(Enemy.get(), walker->Abilities, walker->Brain, walker->AttackAbilityIndex);
	}
};
} // namespace

TEST(Encounter_StepsRunInAFixedOrderAndAdvanceTime)
{
	FEncounterPair pair(12345ULL);
	pair.Sim->FixedDeltaTime = 1.f / 60.f;

	const int steps = pair.Sim->Update(1.f);
	CHECK(steps >= 6); // capped by MaxStepsPerUpdate
	CHECK_EQ(pair.Sim->StepCount(), steps);
	CHECK_NEAR(pair.Sim->Time(), static_cast<float>(steps) / 60.f, 1e-4f);

	// Zero and negative deltas take no steps at all.
	CHECK_EQ(pair.Sim->Update(0.f), 0);
	CHECK_EQ(pair.Sim->Update(-1.f), 0);
}

TEST(Encounter_SameSeedSameInputsSameOutcome)
{
	FEncounterPair a(777ULL);
	FEncounterPair b(777ULL);

	for (int i = 0; i < 300; i++)
	{
		a.Sim->Update(1.f / 60.f);
		b.Sim->Update(1.f / 60.f);
	}

	CHECK_NEAR(a.Enemy->Position().X, b.Enemy->Position().X, 1e-6f);
	CHECK_NEAR(a.Enemy->Position().Z, b.Enemy->Position().Z, 1e-6f);
	CHECK_NEAR(a.Enemy->Vitals().Health(), b.Enemy->Vitals().Health(), 1e-6f);
	CHECK_NEAR(a.Player->Vitals().Health(), b.Player->Vitals().Health(), 1e-6f);
}

TEST(Encounter_PlayerDriverAndEnemyBrainShareOneMovementPath)
{
	FEncounterPair pair(4242ULL);

	// The scripted player closes from the origin; the enemy starts at +Z and
	// reacts. Both must be moving through the same integration code.
	const Float3 playerStart = pair.Player->Position();
	const Float3 enemyStart = pair.Enemy->Position();

	pair.Sim->Advance(1.5f);

	CHECK(pair.Player->Position().Z > playerStart.Z);
	// The enemy noticed the player (proximity or cone) and reacted - either
	// by repositioning or by committing to an attack.
	CHECK(pair.Enemy->Position() != enemyStart || pair.Enemy->Vitals().Health() < 90.f);
}

TEST(Encounter_CombatRunsToCompletionAndAnnouncesDeathOnce)
{
	FEncounterPair pair(20250925ULL);

	int diedCount = 0;
	pair.Enemy->Died.Bind(&diedCount, [](void* ctx, Combatant&)
		{
			*static_cast<int*>(ctx) += 1;
		});

	int hostileKills = 0;
	pair.Sim->Died.Bind(&hostileKills, [](void* ctx, Combatant* victim, Participant*)
		{
			if (victim != nullptr && victim->GetFaction() == Faction::Hostile)
			{
				*static_cast<int*>(ctx) += 1;
			}
		});

	// The player is scripted to keep swinging; the walker fights back. Ten
	// seconds of a level-1 Warden against a level-1 Hollow Walker ends it.
	pair.Sim->Advance(20.f);

	CHECK(!pair.Enemy->IsAlive());
	CHECK_EQ(diedCount, 1);
	CHECK_EQ(hostileKills, 1);
	CHECK_EQ(pair.Sim->HostilesRemaining(), 0);
}

TEST(Encounter_CombatIsDeterministicAcrossFullFights)
{
	FEncounterPair a(31337ULL);
	FEncounterPair b(31337ULL);

	a.Sim->Advance(8.f);
	b.Sim->Advance(8.f);

	CHECK_NEAR(a.Enemy->Vitals().Health(), b.Enemy->Vitals().Health(), 1e-6f);
	CHECK_NEAR(a.Player->Vitals().Health(), b.Player->Vitals().Health(), 1e-6f);
	CHECK_NEAR(a.Enemy->Position().Z, b.Enemy->Position().Z, 1e-6f);
	CHECK(a.Sim->StepCount() == b.Sim->StepCount());
}

TEST(Encounter_PositionsStayInsideTheBounds)
{
	FEncounterPair pair(555ULL);

	for (int i = 0; i < 600; i++)
	{
		pair.Sim->Update(1.f / 60.f);
		const Float3 playerPos = pair.Player->Position();
		CHECK(playerPos.X >= -38.f && playerPos.X <= 38.f);
		CHECK(playerPos.Z >= -38.f && playerPos.Z <= 38.f);
	}
}

TEST(Encounter_DamagePropagatesAggroToNearbyAllies)
{
	FEncounterPair pair(999ULL, /*enemyZ*/ 20.f);

	// Put a second walker next to the first: hitting one must alert the other.
	const EnemyArchetype* walker = FindArchetype(ArchetypeHollowWalker);
	auto bystander = walker->Create("grey-walk/hollow-walker-1", Float3(3.f, 0.f, 8.f), 1);
	pair.Sim->AddEnemy(bystander.get(), walker->Abilities, walker->Brain, walker->AttackAbilityIndex);

	Participant* bystanderParticipant = pair.Sim->Find("grey-walk/hollow-walker-1");
	CHECK(bystanderParticipant != nullptr);

	// Neither has seen anything yet: the bystander is behind the player's
	// cone and outside every aggro radius.
	CHECK_EQ(static_cast<int>(bystanderParticipant->Brain->State()),
		static_cast<int>(AiState::Idle));

	// Hurt the other one from across the arena: allies within the radius are told.
	// Damage goes through the vitals pool, exactly like any other hit path.
	pair.Enemy->Vitals().ApplyDamage(5.f, nullptr);

	// The alert lands on the brain's next tick, not synchronously with the hit.
	pair.Sim->Advance(1.f / 60.f);

	CHECK_EQ(static_cast<int>(bystanderParticipant->Brain->State()),
		static_cast<int>(AiState::Alert));
}

TEST(Encounter_ResetRevivesEveryone)
{
	FEncounterPair pair(2025ULL);
	pair.Sim->Advance(20.f);
	CHECK(!pair.Enemy->IsAlive());

	pair.Sim->Reset();

	CHECK(pair.Enemy->IsAlive());
	CHECK(pair.Player->IsAlive());
	CHECK_NEAR(pair.Enemy->Vitals().Health(), pair.Enemy->Vitals().MaxHealth(), 1e-3f);
	CHECK_EQ(pair.Sim->HostilesRemaining(), 1);

	// Cooldowns are cleared too: the retry starts from zero.
	Participant* playerParticipant = pair.Sim->Find(pair.Player->Id());
	CHECK(playerParticipant != nullptr);
	CHECK(!playerParticipant->Abilities.IsBusy());
}

TEST(Encounter_DestroyingTheSimulationUnbindsFromSurvivingCombatants)
{
	// The player combatant outlives encounters (a new encounter is built when
	// the region changes), so a destroyed encounter must release its handlers.
	auto player = CreatePlayer();
	FScriptedDriver driver;

	{
		EncounterSimulation sim(DeterministicRng(7ULL), WorldBounds::Square(38.f));
		sim.AddDriven(player.get(), BuildPlayerAbilities(), &driver, true);
		CHECK(player->Damaged.Count() > 0);

		Combatant bystander("b", Faction::Hostile);
		(void)bystander;
	}

	// With the encounter gone, no stale handlers remain on the survivor.
	CHECK_EQ(player->Damaged.Count(), 0);
	CHECK_EQ(player->Died.Count(), 0);
}

TEST(Content_PlayerStatsMatchTheAuthoredNumbers)
{
	auto player = CreatePlayer();

	CHECK_EQ(player->Id(), std::string("warden"));
	CHECK_EQ(player->DisplayName, std::string("Warden"));
	CHECK(player->IsPersistent);
	CHECK(player->IsAlive());

	CHECK_NEAR(player->Stats().Get(StatId::MaxHealth), 320.f, 1e-4f);
	CHECK_NEAR(player->Stats().Get(StatId::MaxStamina), 100.f, 1e-4f);
	CHECK_NEAR(player->Stats().Get(StatId::AttackPower), 18.f, 1e-4f);
	CHECK_NEAR(player->Stats().Get(StatId::ShadowPower), 16.f, 1e-4f);
	CHECK_NEAR(player->Stats().Get(StatId::MoveSpeed), 5.5f, 1e-4f);
	CHECK_NEAR(player->Stats().Get(StatId::CritChance), 0.05f, 1e-6f);
	CHECK_NEAR(player->Vitals().Health(), 320.f, 1e-3f);
}

TEST(Content_PlayerKitIsTheFiveAuthoredAbilities)
{
	const auto kit = BuildPlayerAbilities();
	CHECK_EQ(static_cast<int>(kit.size()), 5);

	CHECK_EQ(kit[0]->Id, std::string("ember-edge"));
	CHECK_EQ(kit[1]->Id, std::string("umbra-lance"));
	CHECK_EQ(kit[2]->Id, std::string("ashstep"));
	CHECK_EQ(kit[3]->Id, std::string("sunder"));
	CHECK_EQ(kit[4]->Id, std::string("ward-of-embers"));

	// Umbra Lance is the build decision: it scales off Shadow Power.
	CHECK(kit[1]->UsesShadowPower);
	// Ashstep is mobility - it damages nothing by design.
	CHECK(!kit[2]->DealsDamage());
	// Sunder costs the caster a stagger on a whiff.
	CHECK_NEAR(kit[3]->SelfStaggerSeconds, 0.35f, 1e-6f);
	CHECK_EQ(kit[3]->MaxTargets, 4);
	// Ward of Embers applies effects to the caster only.
	CHECK_EQ(static_cast<int>(kit[4]->Kind), static_cast<int>(AbilityKind::Self));
	CHECK_EQ(static_cast<int>(kit[4]->OnHitStatuses.size()), 2);
}

TEST(Content_CreatureArchetypesKeepTheirAuthoredIdentity)
{
	const EnemyArchetype* walker = FindArchetype(ArchetypeHollowWalker);
	const EnemyArchetype* hound = FindArchetype(ArchetypeCinderHound);
	const EnemyArchetype* sentinel = FindArchetype(ArchetypeAshenSentinel);

	CHECK(walker != nullptr);
	CHECK(hound != nullptr);
	CHECK(sentinel != nullptr);
	CHECK(FindArchetype("does-not-exist") == nullptr);

	CHECK_EQ(walker->DisplayName, std::string("Hollow Walker"));
	CHECK_NEAR(walker->MaxHealth, 90.f, 1e-4f);
	CHECK_NEAR(walker->MoveSpeed, 3.6f, 1e-4f);
	CHECK_EQ(walker->ExperienceReward, 35);

	// The Cinder Hound burns and hates frost - resistances are per school.
	CHECK_NEAR(hound->Resistances.Get(EDamageType::Ember), 0.6f, 1e-6f);
	CHECK_NEAR(hound->Resistances.Get(EDamageType::Frost), -0.25f, 1e-6f);

	CHECK(sentinel->IsBoss);
	CHECK_NEAR(sentinel->MaxHealth, 1500.f, 1e-4f);
}

TEST(Content_HigherLevelPlacementsScaleTheirStats)
{
	const EnemyArchetype* walker = FindArchetype(ArchetypeHollowWalker);

	// Same archetype placed above its baseline: +18% health per level, the
	// rule that keeps a late-game zone from reusing the opening numbers.
	auto level3 = walker->Create("test-walker", Float3::Zero, 3);
	auto base = walker->Create("test-walker-base", Float3::Zero, 1);

	CHECK_NEAR(base->Vitals().MaxHealth(), 90.f, 1e-3f);
	CHECK_NEAR(level3->Vitals().MaxHealth(), 90.f * 1.36f, 1e-2f);
	CHECK_NEAR(level3->Stats().Get(StatId::AttackPower), 14.f * 1.24f, 1e-2f);
	CHECK_EQ(level3->Level, 3);
	CHECK(level3->IsHostileTo(base.get()) == false); // two hostiles: not hostile to each other
}
