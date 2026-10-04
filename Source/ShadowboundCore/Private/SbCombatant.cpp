#include "SbCombatant.h"

namespace ShadowboundCore
{

Combatant::Combatant(std::string id, Faction faction, int level)
	: Level(level < 1 ? 1 : level)
	, _id(std::move(id))
	, _faction(faction)
	, _vitals(_stats, _resistances)
	, _statuses(_stats, _vitals)
	, _position(Float3::Zero)
	, _pendingResult()
	, _hasPendingResult(false)
	, _facingDegrees(0.f)
	, _knockbackSpeed(0.f)
	, _knockbackDirection(Float3::Zero)
	, _deathAnnounced(false)
	, _vitalsDamagedBinding(0)
{
	if (_id.empty())
	{
		_id = "unnamed";
	}

	// Health can be removed through more than one path: a resolved attack via
	// ReceiveDamage, or a damage-over-time tick inside the status system
	// reaching straight for the vitals pool. Observing the pool itself means
	// every one of those paths raises Damaged, so burning to death alerts
	// allies exactly like being struck does.
	_vitalsDamagedBinding = _vitals.Damaged.Bind(this, &Combatant::HandleVitalsDamaged);
}

void Combatant::CopyResistancesFrom(const ResistanceSet& source)
{
	for (int i = 0; i < DamageTypes::Count; ++i)
	{
		const EDamageType type = static_cast<EDamageType>(i);
		_resistances.Set(type, source.Get(type));
	}
}

void Combatant::SetFacing(float degrees)
{
	_facingDegrees = CoreMath::Repeat(degrees, 360.f);
}

void Combatant::TurnTowards(const Float3& direction, float degreesPerSecond, float deltaTime)
{
	const Float3 flat = direction.FlattenedXZ();
	if (flat == Float3::Zero)
	{
		return;
	}

	const float desired = Float3::DirectionToDegrees(flat);
	float delta = CoreMath::DeltaAngle(_facingDegrees, desired);
	const float step = degreesPerSecond * deltaTime;
	if (delta > step)
	{
		delta = step;
	}
	else if (delta < -step)
	{
		delta = -step;
	}

	_facingDegrees = CoreMath::Repeat(_facingDegrees + delta, 360.f);
}

void Combatant::FaceImmediately(const Float3& direction)
{
	const Float3 flat = direction.FlattenedXZ();
	if (flat == Float3::Zero)
	{
		return;
	}

	_facingDegrees = CoreMath::Repeat(Float3::DirectionToDegrees(flat), 360.f);
}

void Combatant::ApplyKnockback(const Float3& direction, float speed)
{
	const Float3 flat = direction.FlattenedXZ();
	if (flat == Float3::Zero || speed <= 0.f)
	{
		return;
	}

	if (speed >= _knockbackSpeed)
	{
		_knockbackSpeed = speed;
		_knockbackDirection = flat;
	}
}

float Combatant::ReceiveDamage(const DamageResult& result, Combatant* source)
{
	if (!IsAlive())
	{
		return 0.f;
	}

	// The full result is carried through to the health pool, so the Damaged
	// event can report armour and crit detail rather than just a number. The
	// pool raises the event, which is what makes every damage path - attack or
	// damage over time - report identically.
	_pendingResult = result;
	_hasPendingResult = true;

	const float applied = _vitals.ApplyDamage(result.Applied, source);

	_hasPendingResult = false;
	_pendingResult = DamageResult();

	if (applied <= 0.f)
	{
		return 0.f;
	}

	if (!_vitals.IsAlive())
	{
		AnnounceDeath();
	}

	return applied;
}

void Combatant::Tick(float deltaTime)
{
	if (deltaTime <= 0.f)
	{
		return;
	}

	_statuses.Tick(deltaTime);
	_vitals.Tick(deltaTime);

	if (_knockbackSpeed > 0.f)
	{
		_knockbackSpeed = CoreMath::MoveTowardsZero(_knockbackSpeed, KnockbackDecayPerSecond * deltaTime);
	}

	if (!_vitals.IsAlive() && !_deathAnnounced)
	{
		AnnounceDeath();
	}
}

float Combatant::EffectiveMoveSpeed() const
{
	// Statuses() is available through the const path because the multiplier
	// getters are observers; keep the read cheap and allocation free.
	return _stats.Get(StatId::MoveSpeed) * _statuses.MoveSpeedMultiplier();
}

float Combatant::EffectiveCooldownRate() const
{
	return _stats.Get(StatId::CooldownRate) * _statuses.CooldownRateMultiplier();
}

float Combatant::EffectiveAttackPower() const
{
	return _stats.Get(StatId::AttackPower) * _statuses.DamageDealtMultiplier();
}

float Combatant::EffectiveShadowPower() const
{
	return _stats.Get(StatId::ShadowPower) * _statuses.DamageDealtMultiplier();
}

void Combatant::Revive(const Float3& position, float facingDegrees)
{
	_deathAnnounced = false;
	_knockbackSpeed = 0.f;
	_statuses.Clear();
	_vitals.ResetToFull();
	_position = position;
	_facingDegrees = CoreMath::Repeat(facingDegrees, 360.f);
}

float Combatant::DistanceTo(const Combatant* other) const
{
	return other == nullptr ? 1e30f : Float3::DistanceXZ(_position, other->_position);
}

bool Combatant::IsHostileTo(const Combatant* other) const
{
	if (other == nullptr || other == this)
	{
		return false;
	}

	if (_faction == Faction::Player)
	{
		return other->_faction == Faction::Hostile;
	}

	if (_faction == Faction::Hostile)
	{
		return other->_faction == Faction::Player;
	}

	return false;
}

void Combatant::HandleVitalsDamaged(void* context, float amount, const Combatant* source)
{
	static_cast<Combatant*>(context)->OnVitalsDamaged(amount, source);
}

void Combatant::OnVitalsDamaged(float amount, const Combatant* source)
{
	// Reports the full result when one is known, and a synthesised equivalent
	// for damage that reached the health pool directly, such as a
	// damage-over-time tick.
	const DamageResult result = _hasPendingResult
		? _pendingResult
		: DamageResult(amount, amount, amount, false, 0.f);

	_hasPendingResult = false;
	_pendingResult = DamageResult();

	// The C# original cast the source object back to Combatant; the C++ port
	// types the source as a Combatant* throughout, so no cast is needed.
	Damaged.Broadcast(*this, const_cast<Combatant*>(source), result);
}

void Combatant::AnnounceDeath()
{
	if (_deathAnnounced)
	{
		return;
	}

	_deathAnnounced = true;
	Died.Broadcast(*this);
}

} // namespace ShadowboundCore
