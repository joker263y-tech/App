#include "SbAttackResolver.h"

#include "SbRng.h"

namespace ShadowboundCore
{

int AttackResolver::Resolve(Combatant& attacker, const AbilityDefinition& ability,
	const std::vector<Combatant*>& candidates, DeterministicRng* rng,
	std::vector<Combatant*>& hits)
{
	hits.clear();

	if (!attacker.IsAlive())
	{
		return 0;
	}

	// A dash is pure movement. It still resolves so that the caller has a
	// single entry point, but it damages nothing.
	if (ability.Kind == AbilityKind::Dash)
	{
		MoveAttacker(attacker, ability);
		return 0;
	}

	// Self-stagger is the cost of committing to the ability, so it is paid
	// whether or not the blow connects. Applying it only on a hit would make
	// whiffing a heavy attack free, and would remove the risk that gives heavy
	// attacks their weight.
	//
	// It is also applied once per swing rather than once per target, so a
	// cleave that hits five enemies does not stagger its own caster five times.
	if (ability.SelfStaggerSeconds > 0.f)
	{
		ApplySelfStagger(attacker, ability.SelfStaggerSeconds);
	}

	if (ability.Kind == AbilityKind::Self)
	{
		ApplyStatusesToSelf(attacker, ability);
		return 0;
	}

	SelectTargets(attacker, ability, candidates, hits);

	if (hits.empty())
	{
		return 0;
	}

	for (Combatant* hit : hits)
	{
		Strike(attacker, ability, *hit, rng);
	}

	return static_cast<int>(hits.size());
}

void AttackResolver::SelectTargets(Combatant& attacker, const AbilityDefinition& ability,
	const std::vector<Combatant*>& candidates, std::vector<Combatant*>& hits)
{
	hits.clear();

	const float range = ability.Range;
	const int limit = ability.MaxTargets <= 0 ? 0x7fffffff : ability.MaxTargets;
	const Float3 forward = attacker.Forward();

	for (Combatant* candidate : candidates)
	{
		if (candidate == nullptr || !candidate->IsAlive() || !attacker.IsHostileTo(candidate))
		{
			continue;
		}

		const float distance = Float3::DistanceXZ(attacker.Position(), candidate->Position());
		if (distance > range)
		{
			continue;
		}

		if (!CoreMath::WithinCone(attacker.Position(), forward, candidate->Position(),
				ability.ConeHalfAngleDegrees, range))
		{
			continue;
		}

		// Maintain a nearest-first list of at most `limit` entries: a new
		// candidate replaces the current farthest entry, and only when it is
		// actually nearer.
		if (static_cast<int>(hits.size()) < limit)
		{
			hits.push_back(candidate);
			continue;
		}

		if (limit <= 0)
		{
			continue;
		}

		int farthestIndex = -1;
		float farthestDistance = -1.f;
		for (std::size_t i = 0; i < hits.size(); ++i)
		{
			const float d = Float3::DistanceXZ(attacker.Position(), hits[i]->Position());
			if (d > farthestDistance)
			{
				farthestDistance = d;
				farthestIndex = static_cast<int>(i);
			}
		}

		const float candidateDistance = Float3::DistanceXZ(attacker.Position(), candidate->Position());
		if (farthestIndex >= 0 && candidateDistance < farthestDistance)
		{
			hits[static_cast<std::size_t>(farthestIndex)] = candidate;
		}
	}
}

DamageResult AttackResolver::Strike(Combatant& attacker, const AbilityDefinition& ability,
	Combatant& target, DeterministicRng* rng)
{
	if (!ability.DealsDamage() || !target.IsAlive())
	{
		return DamageResult::None;
	}

	const StatId powerStat = ability.UsesShadowPower ? StatId::ShadowPower : StatId::AttackPower;
	const float baseDamage = attacker.Stats().Get(powerStat) * ability.DamageMultiplier;

	const DamageRequest request(ability.DamageType, baseDamage,
		0.f,
		attacker.Stats().Get(StatId::CritChance) + ability.CritChanceBonus,
		attacker.Stats().Get(StatId::CritMultiplier),
		attacker.Level,
		ability.Variance);

	DamageResult result = DamageCalculator::Resolve(request,
		target.Stats().Get(StatId::Armor),
		target.Vitals().Resistance().Get(ability.DamageType),
		attacker.Statuses().DamageDealtMultiplier(),
		target.Statuses().DamageTakenMultiplier(),
		rng);

	if (result.IsZero())
	{
		return result;
	}

	if (ability.KnockbackSpeed > 0.f)
	{
		const Float3 push = target.Position() - attacker.Position();
		target.ApplyKnockback(push, ability.KnockbackSpeed);
	}

	target.ReceiveDamage(result, &attacker);
	ApplyStatuses(target, ability, rng, &attacker);
	return result;
}

void AttackResolver::ApplyStatuses(Combatant& target, const AbilityDefinition& ability,
	DeterministicRng* rng, Combatant* source)
{
	if (ability.OnHitStatuses.empty())
	{
		return;
	}

	for (const StatusApplication& application : ability.OnHitStatuses)
	{
		if (application.Chance <= 0.f)
		{
			continue;
		}

		if (application.Chance < 1.f && rng != nullptr && !rng->Chance(application.Chance))
		{
			continue;
		}

		target.Statuses().Apply(BuildStatus(application, ability.DamageType, source));
	}
}

StatusEffect AttackResolver::BuildStatus(const StatusApplication& application, EDamageType fallback,
	const Combatant* source)
{
	if (StatusEffectSystem::IsDamageOverTime(application.Kind))
	{
		return StatusEffect::Dot(application.Kind, application.ResolveDamageType(fallback),
			application.Magnitude, application.Duration, application.TickInterval,
			source, application.MaxStacks == 0 ? 1 : application.MaxStacks);
	}

	return StatusEffect::Modifier(application.Kind, application.Magnitude, application.Duration, source);
}

void AttackResolver::ApplyStatusesToSelf(Combatant& attacker, const AbilityDefinition& ability)
{
	for (const StatusApplication& application : ability.OnHitStatuses)
	{
		attacker.Statuses().Apply(BuildStatus(application, ability.DamageType, &attacker));
	}
}

void AttackResolver::ApplySelfStagger(Combatant& attacker, float seconds)
{
	attacker.Statuses().Apply(StatusEffect::Modifier(StatusKind::Staggered, 1.f, seconds, nullptr));
}

void AttackResolver::MoveAttacker(Combatant& attacker, const AbilityDefinition& ability)
{
	if (ability.DashDistance <= 0.f)
	{
		return;
	}

	const Float3 destination = attacker.Position() + (attacker.Forward() * ability.DashDistance);
	attacker.SetPosition(destination);
}

} // namespace ShadowboundCore
