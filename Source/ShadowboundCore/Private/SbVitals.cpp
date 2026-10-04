#include "SbVitals.h"

namespace ShadowboundCore
{

VitalsPool::VitalsPool(StatSet& stats, ResistanceSet& resistance)
	: _stats(stats)
	, _resistance(resistance)
	, _health(0.f)
	, _stamina(0.f)
	, _initialized(false)
{
}

float VitalsPool::HealthFraction() const
{
	const float max = MaxHealth();
	return max <= 0.f ? 0.f : CoreMath::Clamp01(_health / max);
}

float VitalsPool::StaminaFraction() const
{
	const float max = MaxStamina();
	return max <= 0.f ? 0.f : CoreMath::Clamp01(_stamina / max);
}

void VitalsPool::ResetToFull()
{
	_health = MaxHealth();
	_stamina = MaxStamina();
	_initialized = true;
}

void VitalsPool::Tick(float deltaTime)
{
	if (deltaTime <= 0.f)
	{
		return;
	}

	if (!_initialized)
	{
		return;
	}

	// A dead pool never regenerates.
	//
	// Without this guard, a corpse with any HealthRegen stat climbs back above
	// zero and comes back to life on the next Tick, because the regeneration
	// branch only asks "is health below maximum?". Death must be a terminal
	// state until something explicitly revives the combatant.
	if (_health <= 0.f)
	{
		return;
	}

	const float maxHealth = MaxHealth();
	const float maxStamina = MaxStamina();

	if (_health < maxHealth)
	{
		const float regen = _stats.Get(StatId::HealthRegen);
		if (regen > 0.f)
		{
			_health = CoreMath::Clamp(_health + (regen * deltaTime), 0.f, maxHealth);
		}
	}

	if (_stamina < maxStamina)
	{
		const float regen = _stats.Get(StatId::StaminaRegen);
		if (regen > 0.f)
		{
			_stamina = CoreMath::Clamp(_stamina + (regen * deltaTime), 0.f, maxStamina);
		}
	}

	// A maximum that shrank (armour removed) must pull the current value down.
	if (_health > maxHealth)
	{
		_health = maxHealth;
	}

	if (_stamina > maxStamina)
	{
		_stamina = maxStamina;
	}
}

float VitalsPool::ApplyDamage(float amount, const Combatant* source)
{
	if (amount <= 0.f || !_initialized || !IsAlive())
	{
		return 0.f;
	}

	const bool wasAlive = IsAlive();
	const float applied = amount > _health ? _health : amount;
	_health -= applied;

	Damaged.Broadcast(applied, source);

	if (wasAlive && !IsAlive())
	{
		Died.Broadcast();
	}

	return applied;
}

float VitalsPool::Heal(float amount)
{
	if (amount <= 0.f || !_initialized || !IsAlive())
	{
		return 0.f;
	}

	const float max = MaxHealth();
	if (_health >= max)
	{
		return 0.f;
	}

	const float restored = amount > max - _health ? max - _health : amount;
	_health += restored;
	Healed.Broadcast(restored);
	return restored;
}

bool VitalsPool::TrySpendStamina(float cost)
{
	if (cost <= 0.f)
	{
		return true;
	}

	if (_stamina < cost)
	{
		StaminaDepleted.Broadcast();
		return false;
	}

	_stamina -= cost;
	StaminaSpent.Broadcast(cost);
	return true;
}

void VitalsPool::RestoreStamina(float amount)
{
	if (amount <= 0.f || !_initialized)
	{
		return;
	}

	const float max = MaxStamina();
	_stamina = CoreMath::Clamp(_stamina + amount, 0.f, max);
}

void VitalsPool::KillSilently()
{
	_health = 0.f;
}

} // namespace ShadowboundCore
