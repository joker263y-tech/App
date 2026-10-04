#include "SbStatusEffects.h"

#include "SbStats.h"
#include "SbVitals.h"

namespace ShadowboundCore
{

StatusEffect StatusEffect::Dot(StatusKind kind, EDamageType type, float damagePerTick,
	float duration, float tickInterval, const Combatant* source, int maxStacks)
{
	StatusEffect effect;
	effect.Kind = kind;
	effect.DamageType = type;
	effect.Magnitude = damagePerTick;
	effect.Duration = duration;
	effect.Remaining = duration;
	effect.TickInterval = tickInterval;
	effect.TickAccumulator = 0.f;
	effect.Stacks = 1;
	effect.MaxStacks = maxStacks < 1 ? 1 : maxStacks;
	effect.StackRule = maxStacks > 1 ? StatusStackRule::Stack : StatusStackRule::Refresh;
	effect.Source = source;
	return effect;
}

StatusEffect StatusEffect::Modifier(StatusKind kind, float magnitude, float duration,
	const Combatant* source)
{
	StatusEffect effect;
	effect.Kind = kind;
	effect.DamageType = EDamageType::Physical;
	effect.Magnitude = magnitude;
	effect.Duration = duration;
	effect.Remaining = duration;
	effect.TickInterval = 0.f;
	effect.TickAccumulator = 0.f;
	effect.Stacks = 1;
	effect.MaxStacks = 1;
	effect.StackRule = StatusStackRule::Refresh;
	effect.Source = source;
	return effect;
}

StatusEffectSystem::StatusEffectSystem(StatSet& stats, VitalsPool& vitals)
	: _stats(stats)
	, _vitals(vitals)
{
	_active.reserve(8);
}

bool StatusEffectSystem::IsStunned() const
{
	return Has(StatusKind::Staggered);
}

float StatusEffectSystem::MoveSpeedMultiplier() const
{
	const float slow = StrongestMagnitude(StatusKind::Chilled);
	return CoreMath::Clamp01(1.f - slow);
}

float StatusEffectSystem::CooldownRateMultiplier() const
{
	const float slow = StrongestMagnitude(StatusKind::Chilled);
	return CoreMath::Clamp01(1.f - (slow * 0.5f));
}

float StatusEffectSystem::DamageDealtMultiplier() const
{
	return 1.f + StrongestMagnitude(StatusKind::Empowered);
}

float StatusEffectSystem::DamageTakenMultiplier() const
{
	const float ward = StrongestMagnitude(StatusKind::Warded);
	const float mark = StrongestMagnitude(StatusKind::Marked);
	return (1.f - CoreMath::Clamp01(ward)) * (1.f + mark);
}

void StatusEffectSystem::Apply(const StatusEffect& incoming)
{
	if (!_vitals.IsAlive())
	{
		return;
	}

	const float duration = ScaleDuration(incoming.Duration);
	if (duration <= 0.f)
	{
		return;
	}

	StatusEffect* existing = Find(incoming.Kind);
	if (existing == nullptr)
	{
		StatusEffect created = incoming;
		created.Duration = duration;
		created.Remaining = duration;
		created.TickAccumulator = 0.f;
		created.Stacks = 1;
		if (created.MaxStacks < 1)
		{
			created.MaxStacks = 1;
		}

		_active.push_back(created);
		Applied.Broadcast(_active.back());
		return;
	}

	switch (existing->StackRule)
	{
	case StatusStackRule::Stack:
		if (existing->Stacks < existing->MaxStacks)
		{
			existing->Stacks++;
		}

		existing->Remaining = duration > existing->Remaining ? duration : existing->Remaining;
		// A stronger source raising the weakest stack is the more useful
		// reading of a mixed-strength re-application.
		if (incoming.Magnitude > existing->Magnitude)
		{
			existing->Magnitude = incoming.Magnitude;
			existing->Source = incoming.Source;
		}

		Applied.Broadcast(*existing);
		return;

	case StatusStackRule::Ignore:
		if (incoming.Magnitude <= existing->Magnitude)
		{
			return;
		}

		existing->Magnitude = incoming.Magnitude;
		existing->Remaining = duration;
		existing->Source = incoming.Source;
		Applied.Broadcast(*existing);
		return;

	default:
		existing->Remaining = duration > existing->Remaining ? duration : existing->Remaining;
		if (incoming.Magnitude > existing->Magnitude)
		{
			existing->Magnitude = incoming.Magnitude;
			existing->Source = incoming.Source;
		}

		Applied.Broadcast(*existing);
		return;
	}
}

void StatusEffectSystem::Tick(float deltaTime)
{
	if (deltaTime <= 0.f || _active.empty())
	{
		return;
	}

	for (std::size_t i = _active.size(); i-- > 0;)
	{
		StatusEffect& effect = _active[i];

		if (effect.DealsDamageOverTime() && _vitals.IsAlive())
		{
			effect.TickAccumulator += deltaTime;

			// Guard against a huge delta producing an unbounded loop.
			int guard = 0;
			while (effect.TickAccumulator >= effect.TickInterval && guard < 32)
			{
				effect.TickAccumulator -= effect.TickInterval;
				guard++;

				const float damage = effect.DamagePerTick();
				const float applied = _vitals.ApplyDamage(damage, effect.Source);
				Ticked.Broadcast(effect, applied);

				if (!_vitals.IsAlive())
				{
					break;
				}
			}
		}

		effect.Remaining -= deltaTime;

		if (effect.IsExpired())
		{
			const StatusEffect expired = effect;
			_active.erase(_active.begin() + static_cast<std::ptrdiff_t>(i));
			Expired.Broadcast(expired);
		}
	}
}

bool StatusEffectSystem::Has(StatusKind kind) const
{
	return Find(kind) != nullptr;
}

int StatusEffectSystem::StackCount(StatusKind kind) const
{
	const StatusEffect* effect = Find(kind);
	return effect == nullptr ? 0 : effect->Stacks;
}

float StatusEffectSystem::Magnitude(StatusKind kind) const
{
	if (IsDamageOverTime(kind))
	{
		float total = 0.f;
		for (const StatusEffect& effect : _active)
		{
			if (effect.Kind == kind)
			{
				total += effect.DamagePerTick();
			}
		}

		return total;
	}

	return StrongestMagnitude(kind);
}

bool StatusEffectSystem::TryGet(StatusKind kind, StatusEffect& outEffect) const
{
	const StatusEffect* effect = Find(kind);
	if (effect == nullptr)
	{
		return false;
	}

	outEffect = *effect;
	return true;
}

int StatusEffectSystem::RemoveAll(StatusKind kind)
{
	int removed = 0;
	for (std::size_t i = _active.size(); i-- > 0;)
	{
		if (_active[i].Kind == kind)
		{
			const StatusEffect effect = _active[i];
			_active.erase(_active.begin() + static_cast<std::ptrdiff_t>(i));
			Expired.Broadcast(effect);
			removed++;
		}
	}

	return removed;
}

int StatusEffectSystem::RemoveAllDamageOverTime()
{
	int removed = 0;
	for (std::size_t i = _active.size(); i-- > 0;)
	{
		if (IsDamageOverTime(_active[i].Kind))
		{
			const StatusEffect effect = _active[i];
			_active.erase(_active.begin() + static_cast<std::ptrdiff_t>(i));
			Expired.Broadcast(effect);
			removed++;
		}
	}

	return removed;
}

void StatusEffectSystem::Clear()
{
	if (_active.empty())
	{
		return;
	}

	for (std::size_t i = _active.size(); i-- > 0;)
	{
		const StatusEffect effect = _active[i];
		_active.erase(_active.begin() + static_cast<std::ptrdiff_t>(i));
		Expired.Broadcast(effect);
	}
}

const StatusEffect* StatusEffectSystem::Find(StatusKind kind) const
{
	for (const StatusEffect& effect : _active)
	{
		if (effect.Kind == kind)
		{
			return &effect;
		}
	}

	return nullptr;
}

StatusEffect* StatusEffectSystem::Find(StatusKind kind)
{
	for (StatusEffect& effect : _active)
	{
		if (effect.Kind == kind)
		{
			return &effect;
		}
	}

	return nullptr;
}

float StatusEffectSystem::StrongestMagnitude(StatusKind kind) const
{
	float best = 0.f;
	for (const StatusEffect& effect : _active)
	{
		if (effect.Kind == kind && effect.Magnitude > best)
		{
			best = effect.Magnitude;
		}
	}

	return best;
}

float StatusEffectSystem::ScaleDuration(float duration) const
{
	const float resistance = CoreMath::Clamp(_stats.Get(StatId::StatusResistance), 0.f, 0.9f);
	const float scaled = duration * (1.f - resistance);
	return CoreMath::Clamp(scaled, 0.f, MaxDuration);
}

} // namespace ShadowboundCore
