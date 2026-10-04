#include "SbDamage.h"

#include "SbRng.h"

namespace ShadowboundCore
{

const char* DamageTypes::Name(EDamageType type)
{
	switch (type)
	{
	case EDamageType::Physical: return "Physical";
	case EDamageType::Shadow: return "Umbra";
	case EDamageType::Ember: return "Ember";
	case EDamageType::Frost: return "Frost";
	case EDamageType::Vital: return "Vital";
	default: return "Unknown";
	}
}

ResistanceSet::ResistanceSet()
{
	for (int i = 0; i < DamageTypes::Count; ++i)
	{
		_values[i] = 0.f;
	}
}

ResistanceSet::ResistanceSet(float uniformValue)
	: ResistanceSet()
{
	for (int i = 0; i < DamageTypes::Count; ++i)
	{
		_values[i] = uniformValue;
	}
}

float ResistanceSet::Get(EDamageType type) const
{
	const int index = static_cast<int>(type);
	return (index >= 0 && index < DamageTypes::Count) ? _values[index] : 0.f;
}

void ResistanceSet::Set(EDamageType type, float value)
{
	const int index = static_cast<int>(type);
	if (index < 0 || index >= DamageTypes::Count)
	{
		return;
	}

	_values[index] = ClampResistance(value);
}

void ResistanceSet::Add(EDamageType type, float delta)
{
	Set(type, Get(type) + delta);
}

float ResistanceSet::ClampResistance(float value)
{
	if (value < -1.f)
	{
		return -1.f;
	}

	return value > 0.9f ? 0.9f : value;
}

DamageRequest::DamageRequest(EDamageType type, float amount, float armorPenetration,
	float critChance, float critMultiplier, int attackerLevel, float variance,
	float armorPenetrationPercent)
	: Type(type)
	, Amount(amount)
	, ArmorPenetration(armorPenetration)
	, ArmorPenetrationPercent(CoreMath::Clamp(armorPenetrationPercent, 0.f, 1.f))
	, CritChance(critChance)
	, CritMultiplier(critMultiplier < 1.f ? 1.f : critMultiplier)
	, AttackerLevel(attackerLevel < 1 ? 1 : attackerLevel)
	, Variance(CoreMath::Clamp(variance, 0.f, 0.9f))
{
}

const DamageResult DamageResult::None(0.f, 0.f, 0.f, false, 0.f);

DamageResult DamageCalculator::Resolve(const DamageRequest& request, float defenderArmor,
	float defenderResistance, float attackerDamageMultiplier,
	float defenderDamageTakenMultiplier, DeterministicRng* rng)
{
	if (request.Amount <= 0.f)
	{
		return DamageResult::None;
	}

	if (rng == nullptr)
	{
		// A null generator means "no randomness": average variance, no crits.
		return ResolveDeterministic(request, defenderArmor, defenderResistance,
			attackerDamageMultiplier, defenderDamageTakenMultiplier);
	}

	float amount = request.Amount;

	if (request.Variance > 0.f)
	{
		amount *= 1.f + rng->Range(-request.Variance, request.Variance);
	}

	const bool critical = request.CritChance > 0.f && rng->Chance(request.CritChance);
	if (critical)
	{
		amount *= request.CritMultiplier;
	}

	amount *= attackerDamageMultiplier;

	const float mitigation = MitigationFraction(defenderArmor, request.ArmorPenetration,
		request.ArmorPenetrationPercent, request.AttackerLevel);
	const float afterArmor = amount * (1.f - mitigation);

	const float resistance = ResistanceSet::ClampResistance(defenderResistance);
	const float afterResistance = afterArmor * (1.f - resistance);

	float applied = afterResistance * defenderDamageTakenMultiplier;
	if (applied < 0.f)
	{
		applied = 0.f;
	}

	const float reduction = amount <= CoreMath::Epsilon
		? 0.f
		: CoreMath::Clamp01(1.f - (applied / amount));

	return DamageResult(amount, afterArmor, applied, critical, reduction);
}

DamageResult DamageCalculator::ResolveDeterministic(const DamageRequest& request,
	float defenderArmor, float defenderResistance,
	float attackerDamageMultiplier,
	float defenderDamageTakenMultiplier)
{
	if (request.Amount <= 0.f)
	{
		return DamageResult::None;
	}

	const float amount = request.Amount * attackerDamageMultiplier;

	const float mitigation = MitigationFraction(defenderArmor, request.ArmorPenetration,
		request.ArmorPenetrationPercent, request.AttackerLevel);
	const float afterArmor = amount * (1.f - mitigation);

	const float resistance = ResistanceSet::ClampResistance(defenderResistance);
	const float afterResistance = afterArmor * (1.f - resistance);

	float applied = afterResistance * defenderDamageTakenMultiplier;
	if (applied < 0.f)
	{
		applied = 0.f;
	}

	const float reduction = amount <= CoreMath::Epsilon
		? 0.f
		: CoreMath::Clamp01(1.f - (applied / amount));

	return DamageResult(amount, afterArmor, applied, false, reduction);
}

float DamageCalculator::MitigationFraction(float armor, float penetration, int attackerLevel)
{
	return MitigationFraction(armor, penetration, 0.f, attackerLevel);
}

float DamageCalculator::MitigationFraction(float armor, float penetration,
	float penetrationPercent, int attackerLevel)
{
	const float percent = CoreMath::Clamp(penetrationPercent, 0.f, 1.f);
	const float effectiveArmor = (armor * (1.f - percent)) - penetration;
	if (effectiveArmor <= 0.f)
	{
		return 0.f;
	}

	const int level = attackerLevel < 1 ? 1 : attackerLevel;
	const float denominator = effectiveArmor + (ArmorConstant * (1.f + (ArmorLevelScaling * static_cast<float>(level))));
	float mitigation = effectiveArmor / denominator;
	return mitigation > MaxMitigation ? MaxMitigation : mitigation;
}

float DamageCalculator::ExpectedDamage(const DamageRequest& request, float defenderArmor,
	float defenderResistance, float attackerDamageMultiplier,
	float defenderDamageTakenMultiplier)
{
	const DamageResult plain = ResolveDeterministic(request, defenderArmor, defenderResistance,
		attackerDamageMultiplier, defenderDamageTakenMultiplier);

	const float critChance = CoreMath::Clamp01(request.CritChance);
	if (critChance <= 0.f)
	{
		return plain.Applied;
	}

	// The crit is resolved as its own hit rather than by scaling the plain
	// result, so that the intermediate stages (Raw, Mitigated) stay meaningful
	// and so the model keeps behaving under future non-multiplicative defence
	// such as flat damage reduction.
	//
	// Today this is arithmetically equivalent to scaling the plain result,
	// because every defence term is multiplicative: armor and resistance scale
	// a crit and a normal hit by the same factor, so a crit's *relative*
	// advantage is independent of how armoured the target is. That is a
	// deliberate property - it keeps crit and armor as independent build axes -
	// and it is asserted by tests.
	const DamageRequest critRequest(request.Type, request.Amount * request.CritMultiplier,
		request.ArmorPenetration, 0.f, 1.f, request.AttackerLevel, 0.f,
		request.ArmorPenetrationPercent);

	const DamageResult critical = ResolveDeterministic(critRequest, defenderArmor,
		defenderResistance, attackerDamageMultiplier, defenderDamageTakenMultiplier);

	return plain.Applied + (critChance * (critical.Applied - plain.Applied));
}

} // namespace ShadowboundCore
