#pragma once

#include "SbNumerics.h"
#include "SbRng.h"

namespace ShadowboundCore
{

// -----------------------------------------------------------------------------
// What an enemy is currently doing. Read by the presentation layer for
// animation and by the simulation for diagnostics.
// -----------------------------------------------------------------------------
enum class AiState : int
{
	/// Standing watch. Will wander after a while.
	Idle = 0,

	/// Moving between points near its post.
	Patrol = 1,

	/// Has noticed the player and is reacting. The reaction delay.
	Alert = 2,

	/// Closing on the target.
	Chase = 3,

	/// Committing to an attack, and locked into it.
	Attack = 4,

	/// Interrupted and unable to act.
	Staggered = 5,

	/// Permanently out of the fight.
	Dead = 6
};

const char* AiStateName(AiState state);

// -----------------------------------------------------------------------------
// Tunable behaviour values for one enemy archetype.
// -----------------------------------------------------------------------------
struct EnemyBrainSettings
{
	float ViewDistance = 14.f;
	float ViewHalfAngleDegrees = 70.f;
	float ProximityRadius = 3.f;

	/// Sight range multiplier once already alerted. An enemy that knows you
	/// exist tracks you further.
	float AlertedViewMultiplier = 1.4f;

	/// Beyond this, an alerted enemy gives up and goes home.
	float DeaggroRange = 30.f;

	/// Seconds an enemy keeps chasing after losing sight, before giving up.
	float LoseSightGrace = 4.f;

	/// Distance at which an enemy will commit to an attack.
	float AttackRange = 2.6f;

	/// Distance the enemy tries to hold while fighting.
	float PreferredRange = 2.f;

	/// Seconds between noticing the player and starting to move. The player's
	/// window to strike first.
	float ReactionTime = 0.45f;

	float IdleDuration = 3.f;
	float PatrolRadius = 9.f;
	float PatrolSpeedMultiplier = 0.45f;
	float ChaseSpeedMultiplier = 1.f;
	float BackoffSpeedMultiplier = 0.55f;

	/// Seconds locked into an attack before the enemy can move again. This is
	/// the enemy's commitment window, intentionally independent of the
	/// ability's own cooldown.
	float AttackCommitment = 1.1f;

	/// How long a patrol point is pursued before a new one is chosen.
	float PatrolWaypointTimeout = 6.f;
};

/// What the world looks like to an enemy this tick.
struct AiContext
{
	Float3 SelfPosition;
	Float3 SelfForward;

	/// Where the enemy was placed. It returns here when it loses interest.
	Float3 HomePosition;

	bool HasTarget = false;
	Float3 TargetPosition;
	bool TargetAlive = false;
	bool HasLineOfSight = false;

	bool IsAlive = false;
	bool IsStunned = false;

	/// True when the enemy's combat controller can start an attack right now.
	bool AttackReady = false;

	/// Movement speed in units per second at full commitment.
	float MoveSpeed = 0.f;

	AiContext() = default;
	AiContext(const Float3& selfPosition, const Float3& selfForward, const Float3& homePosition,
		bool hasTarget, const Float3& targetPosition, bool targetAlive, bool hasLineOfSight,
		bool isAlive, bool isStunned, bool attackReady, float moveSpeed)
		: SelfPosition(selfPosition)
		, SelfForward(selfForward)
		, HomePosition(homePosition)
		, HasTarget(hasTarget)
		, TargetPosition(targetPosition)
		, TargetAlive(targetAlive)
		, HasLineOfSight(hasLineOfSight)
		, IsAlive(isAlive)
		, IsStunned(isStunned)
		, AttackReady(attackReady)
		, MoveSpeed(moveSpeed)
	{
	}
};

/// What an enemy wants to do this tick. The simulation executes it.
struct AiIntent
{
	/// Unit direction to move, or zero to hold position.
	Float3 MoveDirection;

	/// Speed in units per second. Zero while holding.
	float DesiredSpeed = 0.f;

	/// True when the enemy should turn to face the target.
	bool FaceTarget = false;

	/// True when the enemy wants its attack started this tick.
	bool WantsToAttack = false;

	bool IsMoving() const { return DesiredSpeed > 0.f && MoveDirection != Float3::Zero; }
};

// -----------------------------------------------------------------------------
// The enemy decision machine.
//
// It holds no references to the world, to the engine, or to other combatants.
// Each tick it is handed an AiContext describing what it can see and handed
// back an AiIntent describing what it wants. That makes every branch of this
// state machine testable by constructing a context directly, with no arena,
// no physics and no frame timing involved.
//
// Design notes that matter for feel:
//
//   * Hysteresis. An enemy that loses sight keeps chasing for
//     LoseSightGrace seconds. Without this, stepping behind a pillar would
//     instantly reset a fight.
//
//   * Reaction time. AiState::Alert inserts a delay between noticing and
//     acting, which is the player's window to strike first.
//
//   * Aggro cannot be dodged by approaching from behind. The proximity radius
//     in PerceptionModel handles close range.
//
//   * Being hit always alerts, regardless of facing. Implemented by
//     ForceAlert, which the simulation calls on damage.
//
//   * Staggering remembers the interrupted state and resumes it, so an
//     interrupted charge resumes chasing rather than restarting from idle.
// -----------------------------------------------------------------------------
class EnemyBrain
{
public:
	EnemyBrain(const EnemyBrainSettings& settings, DeterministicRng rng);

	AiState State() const { return _state; }
	float TimeInState() const { return _timeInState; }
	float TimeSinceLastSeen() const { return _timeSinceLastSeen; }
	const EnemyBrainSettings& Settings() const { return _settings; }

	/// True while the enemy is aware of the player for any reason.
	bool IsAlerted() const;

	/// Forces the enemy to react on its next tick, ignoring facing and sight.
	/// Called when the enemy takes damage, so a player cannot backstab an
	/// enemy into eternity from outside its vision cone.
	void ForceAlert();

	/// Resets the brain to a standing watch. Used when an encounter resets.
	void Reset();

	AiIntent Tick(float deltaTime, const AiContext& context);

	/// Whether the enemy currently senses the target: proximity at grappling
	/// distance, or sight within the cone. Deliberately no third rule.
	bool Perceives(const AiContext& context) const;

private:
	void TickIdle(float deltaTime, const AiContext& context, bool perceives, AiIntent& intent);
	void TickPatrol(float deltaTime, const AiContext& context, bool perceives, AiIntent& intent);
	void TickAlert(float deltaTime, const AiContext& context, bool perceives, AiIntent& intent);
	void TickChase(float deltaTime, const AiContext& context, bool perceives, AiIntent& intent);
	void TickAttack(float deltaTime, const AiContext& context, AiIntent& intent);

	bool ShouldReturnHome(const AiContext& context) const;
	void TransitionTo(AiState next);
	void PickPatrolTarget(const Float3& home);

	EnemyBrainSettings _settings;
	DeterministicRng _rng;

	AiState _state;
	AiState _stateBeforeStagger;
	float _timeInState;
	float _timeSinceLastSeen;
	Float3 _patrolTarget;
	bool _hasPatrolTarget;
	bool _pendingAlert;
};

} // namespace ShadowboundCore
