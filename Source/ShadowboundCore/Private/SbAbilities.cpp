#include "SbAbilities.h"

#include "SbCombatant.h"

namespace ShadowboundCore
{

AbilityController::AbilityController(Combatant& self,
	const std::vector<std::shared_ptr<AbilityDefinition>>& abilities)
	: _self(self)
	, _abilities(abilities)
	, _castingIndex(-1)
	, _phaseTimer(0.f)
	, _phase(CastPhase::Ready)
	, _landedThisTick(-1)
{
	_cooldownRemaining.assign(_abilities.size(), 0.f);
}

float AbilityController::CooldownRemaining(int index) const
{
	return (index >= 0 && index < static_cast<int>(_cooldownRemaining.size()))
		? _cooldownRemaining[static_cast<std::size_t>(index)]
		: 0.f;
}

float AbilityController::CooldownFraction(int index) const
{
	if (index < 0 || index >= Count())
	{
		return 0.f;
	}

	const float total = _abilities[static_cast<std::size_t>(index)]->EffectiveCooldown();
	if (total <= CoreMath::Epsilon)
	{
		return 0.f;
	}

	return CoreMath::Clamp01(_cooldownRemaining[static_cast<std::size_t>(index)] / total);
}

bool AbilityController::IsReady(int index) const
{
	if (index < 0 || index >= Count() || IsBusy() || _self.IsStunned() || !_self.IsAlive())
	{
		return false;
	}

	return _cooldownRemaining[static_cast<std::size_t>(index)] <= 0.f;
}

AbilityFailure AbilityController::CanActivate(int index) const
{
	if (index < 0 || index >= Count())
	{
		return AbilityFailure::UnknownAbility;
	}

	if (!_self.IsAlive())
	{
		return AbilityFailure::Dead;
	}

	if (_self.IsStunned())
	{
		return AbilityFailure::Stunned;
	}

	if (IsBusy())
	{
		return AbilityFailure::Busy;
	}

	if (_cooldownRemaining[static_cast<std::size_t>(index)] > 0.f)
	{
		return AbilityFailure::OnCooldown;
	}

	if (_self.Vitals().Stamina() < _abilities[static_cast<std::size_t>(index)]->StaminaCost)
	{
		return AbilityFailure::NotEnoughStamina;
	}

	return AbilityFailure::None;
}

bool AbilityController::TryActivate(int index, AbilityFailure& outFailure)
{
	outFailure = CanActivate(index);
	if (outFailure != AbilityFailure::None)
	{
		return false;
	}

	const AbilityDefinition& ability = *_abilities[static_cast<std::size_t>(index)];

	if (ability.StaminaCost > 0.f && !_self.Vitals().TrySpendStamina(ability.StaminaCost))
	{
		outFailure = AbilityFailure::NotEnoughStamina;
		return false;
	}

	_castingIndex = index;
	_phaseTimer = ability.WindupSeconds;
	_phase = CastPhase::Windup;
	_landedThisTick = -1;

	// An ability with no windup is left in the Windup phase with a zero timer,
	// and lands on the next Tick. It is deliberately NOT landed here: Tick
	// clears the landed flag as its first action, so setting it during
	// activation would be erased before any caller could read it, and an
	// instant ability would silently never deal damage.

	if (ability.EffectiveCooldown() > 0.f)
	{
		_cooldownRemaining[static_cast<std::size_t>(index)] = ability.EffectiveCooldown();
	}

	return true;
}

int AbilityController::Tick(float deltaTime)
{
	_landedThisTick = -1;

	if (deltaTime > 0.f)
	{
		// Cooldown recovery scales with the Haste stat, and is further reduced
		// by Chilled. A staggered caster still recovers.
		float rate = _self.Stats().Get(StatId::CooldownRate);
		if (rate < 0.f)
		{
			rate = 0.f;
		}

		rate *= _self.Statuses().CooldownRateMultiplier();

		for (std::size_t i = 0; i < _cooldownRemaining.size(); ++i)
		{
			if (_cooldownRemaining[i] > 0.f)
			{
				_cooldownRemaining[i] -= deltaTime * rate;
				if (_cooldownRemaining[i] < 0.f)
				{
					_cooldownRemaining[i] = 0.f;
				}
			}
		}

		AdvancePhase(deltaTime);
	}

	return _landedThisTick;
}

void AbilityController::AdvancePhase(float deltaTime)
{
	if (_phase == CastPhase::Ready)
	{
		return;
	}

	_phaseTimer -= deltaTime;
	if (_phaseTimer > 0.f)
	{
		return;
	}

	const AbilityDefinition& ability = *_abilities[static_cast<std::size_t>(_castingIndex)];

	if (_phase == CastPhase::Windup)
	{
		// Carry the overshoot into recovery so a long frame does not shorten
		// the caster's commitment.
		const float overshoot = -_phaseTimer;
		_landedThisTick = _castingIndex;
		_phase = CastPhase::Recovery;
		_phaseTimer = ability.RecoverySeconds - overshoot;

		if (_phaseTimer <= 0.f)
		{
			EndCast();
		}

		return;
	}

	EndCast();
}

void AbilityController::EndCast()
{
	_castingIndex = -1;
	_phaseTimer = 0.f;
	_phase = CastPhase::Ready;
}

bool AbilityController::Interrupt()
{
	if (_phase != CastPhase::Windup)
	{
		return false;
	}

	// An interrupted ability does not refund its stamina or cooldown.
	// That cost is what makes interrupting worthwhile.
	EndCast();
	return true;
}

void AbilityController::ResetCooldowns()
{
	for (float& remaining : _cooldownRemaining)
	{
		remaining = 0.f;
	}

	EndCast();
}

float AbilityController::MoveSpeedMultiplier() const
{
	if (_phase != CastPhase::Windup || _castingIndex < 0)
	{
		return 1.f;
	}

	return CoreMath::Clamp01(_abilities[static_cast<std::size_t>(_castingIndex)]->MoveSpeedDuringWindup);
}

} // namespace ShadowboundCore
