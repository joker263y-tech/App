#pragma once

#include "SbNumerics.h"

namespace ShadowboundCore
{

class DeterministicRng;

// -----------------------------------------------------------------------------
// Damage schools in the game's original cosmology.
// Append new members at the end; values index arrays.
// -----------------------------------------------------------------------------
enum class EDamageType : int
{
	/// Steel and impact. The baseline school.
	Physical = 0,

	/// Umbra. The player's primary offensive school.
	Shadow = 1,

	/// Fire and burning light.
	Ember = 2,

	/// Cold and stillness.
	Frost = 3,

	/// Direct life drain. Resisted by very little.
	Vital = 4
};

class DamageTypes
{
public:
	static constexpr int Count = 5;

	static const char* Name(EDamageType type);
};

// -----------------------------------------------------------------------------
// Per-school resistance as a 0..1 damage reduction fraction. Kept separate
// from StatSet because resistances are per-type and the stat system is not.
// -----------------------------------------------------------------------------
class ResistanceSet
{
public:
	ResistanceSet();
	explicit ResistanceSet(float uniformValue);

	float Get(EDamageType type) const;

	/// Sets resistance. Values are clamped to -1..0.9, so nothing is fully immune.
	void Set(EDamageType type, float value);
	void Add(EDamageType type, float delta);

	/// Resistance floor of -1 allows up to double damage; the 0.9 ceiling
	/// prevents unkillable enemies.
	static float ClampResistance(float value);

private:
	float _values[DamageTypes::Count];
};

/// One incoming attack, before any mitigation is resolved.
struct DamageRequest
{
	EDamageType Type = EDamageType::Physical;

	/// Base damage before variance, crit and mitigation.
	float Amount = 0.f;

	/// Flat armor ignored by this attack. Strong against light armor.
	float ArmorPenetration = 0.f;

	/// Fraction of armor ignored, 0..1. Strong against heavy armor.
	///
	/// The two penetration stats are not redundant: because mitigation uses a
	/// diminishing-returns curve, flat penetration removes the most mitigation
	/// where the curve is steepest - at low armor values - while a percentage
	/// cut scales with the target's armor and therefore pays off most against
	/// heavily armoured bosses. Both are kept so encounters can favour one.
	float ArmorPenetrationPercent = 0.f;

	/// Chance to crit, 0..1.
	float CritChance = 0.f;

	/// Damage multiplier on a crit, e.g. 1.75.
	float CritMultiplier = 1.5f;

	/// Attacker level, used to scale down the value of flat armor.
	int AttackerLevel = 1;

	/// Fraction of random spread, e.g. 0.1 for +/-10%.
	float Variance = 0.f;

	DamageRequest() = default;
	DamageRequest(EDamageType type, float amount, float armorPenetration = 0.f,
		float critChance = 0.f, float critMultiplier = 1.5f, int attackerLevel = 1,
		float variance = 0.f, float armorPenetrationPercent = 0.f);
};

/// Every intermediate stage of a resolved hit, kept for HUD feedback and tests.
struct DamageResult
{
	/// Damage after variance, crit and attacker buffs, before defence.
	float Raw = 0.f;

	/// Damage after armor.
	float Mitigated = 0.f;

	/// Damage after resistance and defender buffs. What the target loses.
	float Applied = 0.f;

	bool Critical = false;

	/// Fraction of incoming damage removed by armor and resistance combined, 0..1.
	float ReductionFraction = 0.f;

	DamageResult() = default;
	DamageResult(float raw, float mitigated, float applied, bool critical, float reductionFraction)
		: Raw(raw), Mitigated(mitigated), Applied(applied), Critical(critical)
		, ReductionFraction(reductionFraction)
	{
	}

	bool IsZero() const { return Applied <= 0.f; }

	static const DamageResult None;
};

// -----------------------------------------------------------------------------
// Turns a DamageRequest into a DamageResult.
//
// Resolution order, and why:
//   1. Variance first, so spread is visible in the crit number too.
//   2. Crit next, so a crit multiplies the attacker's intent, not the
//      defender's mitigation.
//   3. Attacker buffs, then armor, then resistance, then defender debuffs.
//      Defence is the last word, which is what makes armour feel protective.
//
// Armor uses a diminishing-returns curve. The constant grows with the
// attacker's level, so a fixed amount of armour is worth less against
// higher-level enemies - the knob that makes progression change encounters.
// -----------------------------------------------------------------------------
class DamageCalculator
{
public:
	/// Armor value at which mitigation is roughly half against a level 1 attacker.
	static constexpr float ArmorConstant = 100.f;

	/// Per-level growth of the armor constant.
	static constexpr float ArmorLevelScaling = 0.08f;

	/// Hard ceiling on armor mitigation, so armor alone can never make a target unkillable.
	static constexpr float MaxMitigation = 0.85f;

	/// Resolves a hit. A null rng means "no randomness": average variance, no crits.
	static DamageResult Resolve(const DamageRequest& request, float defenderArmor,
		float defenderResistance, float attackerDamageMultiplier,
		float defenderDamageTakenMultiplier, DeterministicRng* rng);

	/// Variance-free, crit-free resolution. Used by AI threat evaluation and
	/// by tests that assert on exact numbers.
	static DamageResult ResolveDeterministic(const DamageRequest& request, float defenderArmor,
		float defenderResistance, float attackerDamageMultiplier,
		float defenderDamageTakenMultiplier);

	/// Fraction of physical damage removed by armor, 0..MaxMitigation.
	static float MitigationFraction(float armor, float penetration, int attackerLevel);

	/// As above, also removing a fraction of the target's armor first.
	static float MitigationFraction(float armor, float penetration, float penetrationPercent,
		int attackerLevel);

	/// Expected damage of a request, ignoring variance but including the
	/// probability-weighted value of a crit. Used for AI decisions and for
	/// previewing an ability's damage.
	static float ExpectedDamage(const DamageRequest& request, float defenderArmor,
		float defenderResistance, float attackerDamageMultiplier,
		float defenderDamageTakenMultiplier);
};

} // namespace ShadowboundCore
