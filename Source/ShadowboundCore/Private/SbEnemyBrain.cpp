#include "SbEnemyBrain.h"

#include <limits>

#include "SbPerception.h"

namespace ShadowboundCore
{

const char* AiStateName(AiState state)
{
	switch (state)
	{
	case AiState::Idle: return "Idle";
	case AiState::Patrol: return "Patrol";
	case AiState::Alert: return "Alert";
	case AiState::Chase: return "Chase";
	case AiState::Attack: return "Attack";
	case AiState::Staggered: return "Staggered";
	case AiState::Dead: return "Dead";
	default: return "Unknown";
	}
}

EnemyBrain::EnemyBrain(const EnemyBrainSettings& settings, DeterministicRng rng)
	: _settings(settings)
	, _rng(rng)
	, _state(AiState::Idle)
	, _stateBeforeStagger(AiState::Idle)
	, _timeInState(0.f)
	, _timeSinceLastSeen(std::numeric_limits<float>::max())
	, _patrolTarget(Float3::Zero)
	, _hasPatrolTarget(false)
	, _pendingAlert(false)
{
}

bool EnemyBrain::IsAlerted() const
{
	return _state == AiState::Alert || _state == AiState::Chase || _state == AiState::Attack;
}

void EnemyBrain::ForceAlert()
{
	_pendingAlert = true;
	_timeSinceLastSeen = 0.f;
}

void EnemyBrain::Reset()
{
	_state = AiState::Idle;
	_timeInState = 0.f;
	_stateBeforeStagger = AiState::Idle;
	_timeSinceLastSeen = std::numeric_limits<float>::max();
	_hasPatrolTarget = false;
	_pendingAlert = false;
}

AiIntent EnemyBrain::Tick(float deltaTime, const AiContext& context)
{
	AiIntent intent;

	if (!context.IsAlive)
	{
		_state = AiState::Dead;
		_timeInState = 0.f;
		return intent;
	}

	if (deltaTime > 0.f)
	{
		_timeInState += deltaTime;
	}

	// Staggering overrides everything and remembers what it interrupted.
	if (context.IsStunned)
	{
		if (_state != AiState::Staggered)
		{
			_stateBeforeStagger = IsAlerted() ? AiState::Chase : _state;
			_timeInState = 0.f;
		}

		_state = AiState::Staggered;
		return intent;
	}

	if (_state == AiState::Staggered)
	{
		// Resume the interrupted behaviour, not from scratch.
		_state = _stateBeforeStagger;
		_timeInState = 0.f;
	}

	if (_state == AiState::Dead)
	{
		_state = AiState::Idle;
		_timeInState = 0.f;
	}

	const bool perceives = Perceives(context);

	if (perceives)
	{
		_timeSinceLastSeen = 0.f;
	}
	else if (_timeSinceLastSeen < std::numeric_limits<float>::max())
	{
		_timeSinceLastSeen += deltaTime;
	}

	if (_pendingAlert && context.HasTarget && context.TargetAlive)
	{
		_pendingAlert = false;
		TransitionTo(AiState::Alert);
		_timeSinceLastSeen = 0.f;
	}

	switch (_state)
	{
	case AiState::Idle:
		TickIdle(deltaTime, context, perceives, intent);
		break;
	case AiState::Patrol:
		TickPatrol(deltaTime, context, perceives, intent);
		break;
	case AiState::Alert:
		TickAlert(deltaTime, context, perceives, intent);
		break;
	case AiState::Chase:
		TickChase(deltaTime, context, perceives, intent);
		break;
	case AiState::Attack:
		TickAttack(deltaTime, context, intent);
		break;
	default:
		break;
	}

	return intent;
}

bool EnemyBrain::Perceives(const AiContext& context) const
{
	// An alerted enemy tracks further, but still only within line of sight.
	const float viewDistance = IsAlerted()
		? _settings.ViewDistance * _settings.AlertedViewMultiplier
		: _settings.ViewDistance;

	const PerceptionQuery query(context.SelfPosition, context.SelfForward, context.TargetPosition,
		viewDistance, _settings.ViewHalfAngleDegrees, _settings.ProximityRadius,
		context.HasTarget, context.TargetAlive, context.HasLineOfSight);

	return PerceptionModel::CanPerceive(query);
}

void EnemyBrain::TickIdle(float /*deltaTime*/, const AiContext& context, bool perceives,
	AiIntent& intent)
{
	if (perceives)
	{
		// Turn to look on the same tick it notices, rather than burning a
		// frame in the new state with an empty intent.
		TransitionTo(AiState::Alert);
		intent.FaceTarget = true;
		return;
	}

	if (ShouldReturnHome(context))
	{
		intent.MoveDirection = (context.HomePosition - context.SelfPosition).FlattenedXZ();
		intent.DesiredSpeed = context.MoveSpeed * _settings.PatrolSpeedMultiplier;
		return;
	}

	if (_timeInState >= _settings.IdleDuration)
	{
		PickPatrolTarget(context.HomePosition);
		TransitionTo(AiState::Patrol);
	}
}

void EnemyBrain::TickPatrol(float /*deltaTime*/, const AiContext& context, bool perceives,
	AiIntent& intent)
{
	if (perceives)
	{
		TransitionTo(AiState::Alert);
		intent.FaceTarget = true;
		return;
	}

	if (ShouldReturnHome(context))
	{
		intent.MoveDirection = (context.HomePosition - context.SelfPosition).FlattenedXZ();
		intent.DesiredSpeed = context.MoveSpeed * _settings.PatrolSpeedMultiplier;
		return;
	}

	if (!_hasPatrolTarget)
	{
		PickPatrolTarget(context.HomePosition);
	}

	const Float3 toTarget = _patrolTarget - context.SelfPosition;
	const float distance = toTarget.Magnitude();

	if (distance <= 0.6f || _timeInState >= _settings.PatrolWaypointTimeout)
	{
		_hasPatrolTarget = false;
		TransitionTo(AiState::Idle);
		return;
	}

	intent.MoveDirection = toTarget.FlattenedXZ();
	intent.DesiredSpeed = context.MoveSpeed * _settings.PatrolSpeedMultiplier;
}

void EnemyBrain::TickAlert(float /*deltaTime*/, const AiContext& /*context*/, bool perceives,
	AiIntent& intent)
{
	// Stop and stare. This is the player's window to act first.
	intent.FaceTarget = true;

	if (!perceives && _timeSinceLastSeen > _settings.LoseSightGrace)
	{
		TransitionTo(AiState::Idle);
		return;
	}

	if (_timeInState >= _settings.ReactionTime)
	{
		TransitionTo(AiState::Chase);
	}
}

void EnemyBrain::TickChase(float /*deltaTime*/, const AiContext& context, bool perceives,
	AiIntent& intent)
{
	intent.FaceTarget = true;

	const float distance = Float3::DistanceXZ(context.SelfPosition, context.TargetPosition);

	// Give up only when out of range AND sight has been lost long enough.
	if (distance > _settings.DeaggroRange && _timeSinceLastSeen > _settings.LoseSightGrace)
	{
		TransitionTo(AiState::Idle);
		return;
	}

	if (!perceives && _timeSinceLastSeen > _settings.LoseSightGrace)
	{
		// Lost the player but still close by: sweep back toward home.
		intent.MoveDirection = (context.HomePosition - context.SelfPosition).FlattenedXZ();
		intent.DesiredSpeed = context.MoveSpeed * _settings.PatrolSpeedMultiplier;

		if (Float3::DistanceXZ(context.SelfPosition, context.HomePosition) <= 1.f)
		{
			TransitionTo(AiState::Idle);
		}

		return;
	}

	// The only gate on attacking is whether the combat controller can
	// actually start one. The brain deliberately keeps no attack timer of its
	// own: the ability's windup, recovery and cooldown already define the
	// rhythm, and a second timer here duplicated that and made the enemy
	// swing again the instant its previous swing ended.
	if (distance <= _settings.AttackRange && context.AttackReady)
	{
		intent.WantsToAttack = true;
		intent.FaceTarget = true;
		TransitionTo(AiState::Attack);
		return;
	}

	if (distance > _settings.PreferredRange)
	{
		intent.MoveDirection = (context.TargetPosition - context.SelfPosition).FlattenedXZ();
		intent.DesiredSpeed = context.MoveSpeed * _settings.ChaseSpeedMultiplier;
		return;
	}

	if (distance < _settings.PreferredRange * 0.7f)
	{
		// Too close. Back off so attacks do not overlap in a scrum.
		intent.MoveDirection = (context.SelfPosition - context.TargetPosition).FlattenedXZ();
		intent.DesiredSpeed = context.MoveSpeed * _settings.BackoffSpeedMultiplier;
	}
}

void EnemyBrain::TickAttack(float /*deltaTime*/, const AiContext& /*context*/, AiIntent& intent)
{
	intent.FaceTarget = true;

	if (_timeInState >= _settings.AttackCommitment)
	{
		TransitionTo(AiState::Chase);
	}
}

bool EnemyBrain::ShouldReturnHome(const AiContext& context) const
{
	const float distanceFromHome = Float3::DistanceXZ(context.SelfPosition, context.HomePosition);
	return distanceFromHome > _settings.PatrolRadius * 1.5f;
}

void EnemyBrain::TransitionTo(AiState next)
{
	if (_state == next)
	{
		return;
	}

	_state = next;
	_timeInState = 0.f;
}

void EnemyBrain::PickPatrolTarget(const Float3& home)
{
	const float angle = _rng.Range(0.f, 360.f);
	const float distance = _rng.Range(_settings.PatrolRadius * 0.35f, _settings.PatrolRadius);
	_patrolTarget = home + (Float3::DegreesToDirection(angle) * distance);
	_hasPatrolTarget = true;
}

} // namespace ShadowboundCore
