#pragma once

#include <cstddef>
#include <string>
#include <vector>

namespace ShadowboundCore
{

// -----------------------------------------------------------------------------
// Every numerical property that can describe a combatant. Values are held in
// arrays indexed by this enum, so iteration order is a performance and
// determinism concern: append new members at the end, never reorder.
// -----------------------------------------------------------------------------
enum class StatId : int
{
	MaxHealth = 0,
	MaxStamina = 1,
	AttackPower = 2,
	ShadowPower = 3,
	Armor = 4,
	MoveSpeed = 5,
	CritChance = 6,
	CritMultiplier = 7,
	HealthRegen = 8,
	StaminaRegen = 9,
	CooldownRate = 10,
	StatusResistance = 11
};

class StatIds
{
public:
	static constexpr int Count = 12;

	/// Human-readable label, used by tooling and the HUD.
	static const char* Name(StatId id);

	/// Stats stored as a 0..1 fraction rather than a raw number.
	static bool IsFraction(StatId id);
};

/// How a modifier combines with the others applied to the same stat.
enum class ModifierOp : int
{
	/// Added before percentages. Equipment flat bonuses.
	Flat = 0,

	/// Summed with other additive percentages, then applied once.
	PercentAdditive = 1,

	/// Applied as its own multiplier, compounding with other multiplicative sources.
	PercentMultiplicative = 2
};

// The C# core held modifier sources as `object` (any item, status or system)
// and used reference identity to remove exactly one source's contributions.
// The C++ port keeps that contract with an opaque, non-owning token.
using ModifierSource = const void*;

// -----------------------------------------------------------------------------
// A single stat change contributed by one source: a piece of equipment, a
// status effect, a level-up, a boss aura. Every modifier carries the source
// that produced it, so a source can remove exactly its own contributions
// without disturbing anything else.
// -----------------------------------------------------------------------------
struct StatModifier
{
	StatId Stat = StatId::MaxHealth;
	ModifierOp Op = ModifierOp::Flat;
	float Value = 0.f;
	ModifierSource Source = nullptr;

	StatModifier() = default;
	StatModifier(StatId stat, ModifierOp op, float value, ModifierSource source = nullptr)
		: Stat(stat), Op(op), Value(value), Source(source)
	{
	}

	static StatModifier MakeFlat(StatId stat, float value, ModifierSource source = nullptr)
	{
		return StatModifier(stat, ModifierOp::Flat, value, source);
	}

	static StatModifier MakePercent(StatId stat, float value, ModifierSource source = nullptr)
	{
		return StatModifier(stat, ModifierOp::PercentAdditive, value, source);
	}

	static StatModifier MakeMultiply(StatId stat, float value, ModifierSource source = nullptr)
	{
		return StatModifier(stat, ModifierOp::PercentMultiplicative, value, source);
	}

	std::string ToString() const;
};

// -----------------------------------------------------------------------------
// Base values plus a modifier stack, resolved on demand.
//
// Resolution order is fixed and deliberate:
//     ((base + flat) * (1 + sumPercentAdditive)) * product(1 + mult_i)
//
// Flat bonuses are affected by percentages (a heavier weapon benefits more
// from a damage buff), and multiplicative sources compound with each other.
// Results are clamped at zero and cached until the stack changes, because
// stats are read several times per frame per combatant on a mobile budget.
// -----------------------------------------------------------------------------
class StatSet
{
public:
	StatSet();

	int ModifierCount() const { return static_cast<int>(_modifiers.size()); }

	float Base(StatId id) const { return _base[Index(id)]; }

	void SetBase(StatId id, float value);
	void AddToBase(StatId id, float delta);

	/// Resolved value of a stat, including all modifiers. Like the C# original,
	/// this may refresh the internal cache on first read after a change - the
	/// cache is an implementation detail, not observable state.
	float Get(StatId id) const;

	void AddModifier(const StatModifier& modifier);
	void AddModifiers(const std::vector<StatModifier>& modifiers);

	/// Removes every modifier owned by source; returns how many were removed.
	/// Used when equipment is unequipped or a status expires.
	int RemoveModifiersFrom(ModifierSource source);

	void ClearModifiers();

	/// Forces the next Get() to recompute. Used after bulk base edits.
	void Invalidate() { _dirty = true; }

private:
	static int Index(StatId id) { return static_cast<int>(id); }
	void Recompute() const;

	float _base[StatIds::Count];

	/// Resolved values and the dirty flag are a pure cache of _base+
	/// _modifiers: reading a stat may refresh them, exactly as in the C# core.
	mutable float _cache[StatIds::Count];
	mutable bool _dirty;

	std::vector<StatModifier> _modifiers;
};

} // namespace ShadowboundCore
