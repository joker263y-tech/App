#include "SbTest.h"

#include "SbEnemyBrain.h"
#include "SbPerception.h"

using ShadowboundCore::AiContext;
using ShadowboundCore::AiIntent;
using ShadowboundCore::AiState;
using ShadowboundCore::DeterministicRng;
using ShadowboundCore::EnemyBrain;
using ShadowboundCore::EnemyBrainSettings;
using ShadowboundCore::Float3;
using ShadowboundCore::PerceptionModel;
using ShadowboundCore::PerceptionQuery;

namespace
{
EnemyBrainSettings TestSettings()
{
	EnemyBrainSettings settings;
	settings.ViewDistance = 15.f;
	settings.ViewHalfAngleDegrees = 70.f;
	settings.ProximityRadius = 3.f;
	settings.ReactionTime = 0.45f;
	settings.AttackRange = 2.6f;
	settings.PreferredRange = 2.f;
	settings.LoseSightGrace = 4.f;
	settings.IdleDuration = 3.f;
	return settings;
}

AiContext MakeContext(const Float3& selfPosition, const Float3& forward, bool hasTarget,
	const Float3& targetPosition, bool alive, bool los, bool attackReady)
{
	return AiContext(selfPosition, forward, selfPosition /*home*/, hasTarget, targetPosition,
		alive, los, alive, /*isStunned*/ false, attackReady, 5.f);
}
} // namespace

TEST(Perception_DeadTargetsAreNeverPerceived)
{
	const PerceptionQuery query(Float3::Zero, Float3(0.f, 0.f, 1.f), Float3(0.f, 0.f, 1.f),
		15.f, 70.f, 3.f, /*hasTarget*/ true, /*targetAlive*/ false, /*los*/ true);
	CHECK(!PerceptionModel::CanPerceive(query));
}

TEST(Perception_ProximityIgnoresFacingAndSight)
{
	// Target directly behind, no line of sight - but inside grappling range.
	const PerceptionQuery query(Float3::Zero, Float3(0.f, 0.f, 1.f), Float3(0.f, 0.f, -2.f),
		15.f, 70.f, 3.f, true, true, false);
	CHECK(PerceptionModel::CanPerceive(query));
}

TEST(Perception_BeyondArmReachNeedsConeRangeAndSight)
{
	const Float3 forward(0.f, 0.f, 1.f);

	// In the cone, in range, with sight: perceived.
	const PerceptionQuery seen(Float3::Zero, forward, Float3(0.f, 0.f, 8.f),
		15.f, 70.f, 3.f, true, true, true);
	CHECK(PerceptionModel::CanPerceive(seen));

	// Same position but sight blocked by a pillar.
	const PerceptionQuery blocked(Float3::Zero, forward, Float3(0.f, 0.f, 8.f),
		15.f, 70.f, 3.f, true, true, false);
	CHECK(!PerceptionModel::CanPerceive(blocked));

	// Behind the vision cone.
	const PerceptionQuery behind(Float3::Zero, forward, Float3(0.f, 0.f, -8.f),
		15.f, 70.f, 3.f, true, true, true);
	CHECK(!PerceptionModel::CanPerceive(behind));

	// Outside the view distance.
	const PerceptionQuery distant(Float3::Zero, forward, Float3(0.f, 0.f, 40.f),
		15.f, 70.f, 3.f, true, true, true);
	CHECK(!PerceptionModel::CanPerceive(distant));
}

TEST(Brain_IdleAlertsOnSightAndTurnsToLook)
{
	EnemyBrain brain(TestSettings(), DeterministicRng(1ULL));

	const AiContext sees = MakeContext(Float3::Zero, Float3(0.f, 0.f, 1.f), true,
		Float3(0.f, 0.f, 6.f), true, true, true);
	const AiIntent intent = brain.Tick(0.1f, sees);

	CHECK_EQ(static_cast<int>(brain.State()), static_cast<int>(AiState::Alert));
	CHECK(intent.FaceTarget);
}

TEST(Brain_AlertBecomesChaseOnlyAfterReactionTime)
{
	EnemyBrain brain(TestSettings(), DeterministicRng(1ULL));

	const AiContext sees = MakeContext(Float3::Zero, Float3(0.f, 0.f, 1.f), true,
		Float3(0.f, 0.f, 6.f), true, true, true);

	brain.Tick(0.1f, sees);
	CHECK_EQ(static_cast<int>(brain.State()), static_cast<int>(AiState::Alert));

	// Still inside the reaction delay.
	brain.Tick(0.1f, sees);
	CHECK_EQ(static_cast<int>(brain.State()), static_cast<int>(AiState::Alert));

	// Past it: commit to the chase.
	brain.Tick(0.5f, sees);
	CHECK_EQ(static_cast<int>(brain.State()), static_cast<int>(AiState::Chase));
}

TEST(Brain_ChaseAttacksOnlyWhenInRangeAndReady)
{
	EnemyBrainSettings settings = TestSettings();
	EnemyBrain brain(settings, DeterministicRng(1ULL));

	const AiContext close = MakeContext(Float3::Zero, Float3(0.f, 0.f, 1.f), true,
		Float3(0.f, 0.f, 2.f), true, true, /*attackReady*/ false);

	// Drive to Chase state first.
	const AiContext sees = MakeContext(Float3::Zero, Float3(0.f, 0.f, 1.f), true,
		Float3(0.f, 0.f, 6.f), true, true, true);
	brain.Tick(0.1f, sees);
	brain.Tick(0.6f, sees);
	CHECK_EQ(static_cast<int>(brain.State()), static_cast<int>(AiState::Chase));

	// In range, but the ability is on cooldown: no attack, and it keeps
	// adjusting distance instead.
	AiIntent intent = brain.Tick(0.1f, close);
	CHECK(!intent.WantsToAttack);

	// Now the cooldown is clear: it commits.
	intent = brain.Tick(0.1f, MakeContext(Float3::Zero, Float3(0.f, 0.f, 1.f), true,
		Float3(0.f, 0.f, 2.f), true, true, /*attackReady*/ true));
	CHECK(intent.WantsToAttack);
	CHECK_EQ(static_cast<int>(brain.State()), static_cast<int>(AiState::Attack));
}

TEST(Brain_LosingSightEventuallyGivesUp)
{
	EnemyBrainSettings settings = TestSettings();
	EnemyBrain brain(settings, DeterministicRng(1ULL));

	const AiContext sees = MakeContext(Float3::Zero, Float3(0.f, 0.f, 1.f), true,
		Float3(0.f, 0.f, 6.f), true, true, true);
	brain.Tick(0.1f, sees);
	brain.Tick(0.6f, sees);
	CHECK_EQ(static_cast<int>(brain.State()), static_cast<int>(AiState::Chase));

	// Sight lost, target still "present" but unseen and far.
	const AiContext lost = MakeContext(Float3::Zero, Float3(0.f, 0.f, 1.f), true,
		Float3(0.f, 0.f, 60.f), true, /*los*/ false, false);

	// Within the grace period it keeps chasing - stepping behind a pillar
	// must not reset the fight instantly.
	brain.Tick(1.f, lost);
	CHECK_EQ(static_cast<int>(brain.State()), static_cast<int>(AiState::Chase));

	// Past the grace and beyond deaggro range: back to idle.
	brain.Tick(4.f, lost);
	CHECK_EQ(static_cast<int>(brain.State()), static_cast<int>(AiState::Idle));
}

TEST(Brain_ForceAlertBypassesTheVisionCone)
{
	EnemyBrain brain(TestSettings(), DeterministicRng(1ULL));

	// Target far behind the enemy: not perceived.
	const AiContext behind = MakeContext(Float3::Zero, Float3(0.f, 0.f, 1.f), true,
		Float3(0.f, 0.f, -10.f), true, true, true);
	brain.Tick(0.1f, behind);
	CHECK_EQ(static_cast<int>(brain.State()), static_cast<int>(AiState::Idle));

	// Being hit always alerts, regardless of facing.
	brain.ForceAlert();
	brain.Tick(0.1f, behind);
	CHECK_EQ(static_cast<int>(brain.State()), static_cast<int>(AiState::Alert));
}

TEST(Brain_StaggerRemembersAndResumesTheInterruptedState)
{
	EnemyBrain brain(TestSettings(), DeterministicRng(1ULL));

	const AiContext sees = MakeContext(Float3::Zero, Float3(0.f, 0.f, 1.f), true,
		Float3(0.f, 0.f, 6.f), true, true, true);
	brain.Tick(0.1f, sees);
	brain.Tick(0.6f, sees);
	CHECK_EQ(static_cast<int>(brain.State()), static_cast<int>(AiState::Chase));

	// Staggered: everything stops, and the interrupted state is remembered.
	AiContext stunned = sees;
	stunned.IsStunned = true;
	AiIntent intent = brain.Tick(0.1f, stunned);
	CHECK_EQ(static_cast<int>(brain.State()), static_cast<int>(AiState::Staggered));
	CHECK(intent.MoveDirection == Float3::Zero);
	CHECK(!intent.WantsToAttack);

	// Recovered: resumes chasing, not restarting from idle.
	brain.Tick(0.1f, sees);
	CHECK_EQ(static_cast<int>(brain.State()), static_cast<int>(AiState::Chase));
}

TEST(Brain_PatrolPicksPointsAndReturnsHome)
{
	EnemyBrainSettings settings = TestSettings();
	settings.IdleDuration = 0.1f;
	settings.PatrolRadius = 6.f;

	EnemyBrain brain(settings, DeterministicRng(99ULL));

	const AiContext alone = MakeContext(Float3::Zero, Float3(0.f, 0.f, 1.f), false,
		Float3::Zero, /*alive*/ true, false, false);

	// Idle for long enough: starts patrolling.
	brain.Tick(0.2f, alone);
	CHECK_EQ(static_cast<int>(brain.State()), static_cast<int>(AiState::Patrol));

	// Patrol means moving.
	const AiIntent moving = brain.Tick(0.1f, alone);
	CHECK(moving.DesiredSpeed > 0.f);

	// Strayed far from home: heads straight back.
	const AiContext strayed = MakeContext(Float3(50.f, 0.f, 50.f), Float3(0.f, 0.f, 1.f), false,
		Float3::Zero, /*alive*/ true, false, false);
	const AiIntent returning = brain.Tick(0.1f, strayed);
	CHECK(returning.DesiredSpeed > 0.f);
	CHECK_EQ(static_cast<int>(brain.State()), static_cast<int>(AiState::Patrol));
}
