#include "SbStats.h"

namespace ShadowboundCore
{

const char* StatIds::Name(StatId id)
{
	switch (id)
	{
	case StatId::MaxHealth: return "Vitality";
	case StatId::MaxStamina: return "Endurance";
	case StatId::AttackPower: return "Attack";
	case StatId::ShadowPower: return "Umbra";
	case StatId::Armor: return "Ward";
	case StatId::MoveSpeed: return "Speed";
	case StatId::CritChance: return "Precision";
	case StatId::CritMultiplier: return "Severity";
	case StatId::HealthRegen: return "Mending";
	case StatId::StaminaRegen: return "Recovery";
	case StatId::CooldownRate: return "Haste";
	case StatId::StatusResistance: return "Resolve";
	default: return "Unknown";
	}
}

bool StatIds::IsFraction(StatId id)
{
	return id == StatId::CritChance || id == StatId::StatusResistance;
}

std::string StatModifier::ToString() const
{
	return std::string(StatIds::Name(Stat)) + " " +
		(Op == ModifierOp::Flat ? "Flat" :
		 Op == ModifierOp::PercentAdditive ? "PercentAdditive" : "PercentMultiplicative") +
		" " + std::to_string(Value);
}

StatSet::StatSet()
	: _dirty(true)
{
	for (int i = 0; i < StatIds::Count; ++i)
	{
		_base[i] = 0.f;
		_cache[i] = 0.f;
	}

	// Stats that act as rates or multipliers must not default to zero, because
	// zero is a degenerate value rather than a neutral one: a CooldownRate of 0
	// freezes every cooldown forever, and a CritMultiplier of 0 makes a critical
	// hit deal no damage. Content authors set these explicitly when they want a
	// different value, so seeding a sensible identity here removes a silent
	// footgun without taking anything away.
	_base[Index(StatId::CooldownRate)] = 1.f;
	_base[Index(StatId::CritMultiplier)] = 1.5f;
}

void StatSet::SetBase(StatId id, float value)
{
	_base[Index(id)] = value;
	_dirty = true;
}

void StatSet::AddToBase(StatId id, float delta)
{
	_base[Index(id)] += delta;
	_dirty = true;
}

float StatSet::Get(StatId id) const
{
	if (_dirty)
	{
		Recompute();
	}

	return _cache[Index(id)];
}

void StatSet::AddModifier(const StatModifier& modifier)
{
	if (modifier.Source == nullptr && modifier.Value == 0.f)
	{
		return;
	}

	// nullptr is a valid source (an unowned template modifier, e.g. an authored
	// item definition); RemoveModifiersFrom(nullptr) deliberately removes none.
	_modifiers.push_back(modifier);
	_dirty = true;
}

void StatSet::AddModifiers(const std::vector<StatModifier>& modifiers)
{
	for (const StatModifier& modifier : modifiers)
	{
		AddModifier(modifier);
	}
}

int StatSet::RemoveModifiersFrom(ModifierSource source)
{
	if (source == nullptr || _modifiers.empty())
	{
		return 0;
	}

	int removed = 0;
	for (std::size_t i = _modifiers.size(); i-- > 0;)
	{
		if (_modifiers[i].Source == source)
		{
			_modifiers.erase(_modifiers.begin() + static_cast<std::ptrdiff_t>(i));
			removed++;
		}
	}

	if (removed > 0)
	{
		_dirty = true;
	}

	return removed;
}

void StatSet::ClearModifiers()
{
	if (_modifiers.empty())
	{
		return;
	}

	_modifiers.clear();
	_dirty = true;
}

void StatSet::Recompute() const
{
	// Scratch arrays are locals rather than members: nothing outside this
	// function ever reads them, and keeping them here keeps Recompute() const.
	float flatSum[StatIds::Count];
	float additiveSum[StatIds::Count];
	float multiplierProduct[StatIds::Count];

	for (int i = 0; i < StatIds::Count; ++i)
	{
		flatSum[i] = 0.f;
		additiveSum[i] = 0.f;
		multiplierProduct[i] = 1.f;
	}

	for (const StatModifier& m : _modifiers)
	{
		const int index = Index(m.Stat);
		if (index < 0 || index >= StatIds::Count)
		{
			continue;
		}

		switch (m.Op)
		{
		case ModifierOp::Flat:
			flatSum[index] += m.Value;
			break;
		case ModifierOp::PercentAdditive:
			additiveSum[index] += m.Value;
			break;
		case ModifierOp::PercentMultiplicative:
			multiplierProduct[index] *= 1.f + m.Value;
			break;
		}
	}

	for (int i = 0; i < StatIds::Count; ++i)
	{
		const float value = (_base[i] + flatSum[i]) * (1.f + additiveSum[i]) * multiplierProduct[i];
		_cache[i] = value < 0.f ? 0.f : value;
	}

	_dirty = false;
}

} // namespace ShadowboundCore
