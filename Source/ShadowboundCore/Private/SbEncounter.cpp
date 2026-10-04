#include "SbEncounter.h"

#include <algorithm>

#include "SbAttackResolver.h"

namespace ShadowboundCore
{

EncounterSimulation::EncounterSimulation(DeterministicRng rng, WorldBounds bounds,
	IOcclusionProvider* occlusion)
	: _rng(rng)
	, _bounds(bounds)
	, _occlusion(occlusion)
	, _accumulator(0.f)
	, _time(0.f)
	, _stepCount(0)
	, _hostilesRemaining(0)
{
	_participants.reserve(16);
	_combatants.reserve(16);
	_hitBuffer.reserve(8);
	_landings.reserve(8);
}

EncounterSimulation::~EncounterSimulation()
{
	// Release this encounter's subscriptions on the combatants. The player
	// outlives encounters (a new encounter is built when the region changes),
	// so leaving these bound would later call into freed memory.
	for (Participant& participant : _participants)
	{
		if (participant.Self != nullptr)
		{
			participant.Self->Damaged.Unbind(participant.DamagedBinding);
			participant.Self->Died.Unbind(participant.DiedBinding);
		}
	}
}

Participant* EncounterSimulation::AddDriven(Combatant* combatant,
	const std::vector<std::shared_ptr<AbilityDefinition>>& abilities,
	ICombatantDriver* driver, bool isPlayer)
{
	Participant* participant = Attach(combatant, abilities, 0);
	participant->Driver = driver;
	participant->IsPlayer = isPlayer;
	return participant;
}

Participant* EncounterSimulation::AddEnemy(Combatant* combatant,
	const std::vector<std::shared_ptr<AbilityDefinition>>& abilities,
	const EnemyBrainSettings& brainSettings, int attackAbilityIndex)
{
	Participant* participant = Attach(combatant, abilities, attackAbilityIndex);

	// Each enemy gets its own generator stream, so changing one enemy's
	// behaviour cannot shift the patrol route of another. The hash must be
	// process-stable or the same seed would play out differently each launch.
	const std::uint64_t stream = DeterministicRng::StableHash(combatant->Id()) | 1ULL;
	participant->Brain = std::make_unique<EnemyBrain>(brainSettings, _rng.Fork(stream));

	return participant;
}

Participant* EncounterSimulation::Find(const std::string& combatantId)
{
	for (Participant& participant : _participants)
	{
		if (participant.Self != nullptr && participant.Self->Id() == combatantId)
		{
			return &participant;
		}
	}

	return nullptr;
}

Participant* EncounterSimulation::FindParticipant(Combatant* combatant)
{
	for (Participant& participant : _participants)
	{
		if (participant.Self == combatant)
		{
			return &participant;
		}
	}

	return nullptr;
}

Combatant* EncounterSimulation::FindNearestHostile(Combatant* self, float maxRange)
{
	if (self == nullptr)
	{
		return nullptr;
	}

	Combatant* best = nullptr;
	float bestDistance = maxRange;

	for (Combatant* candidate : _combatants)
	{
		if (!candidate->IsAlive() || !self->IsHostileTo(candidate))
		{
			continue;
		}

		const float distance = Float3::DistanceXZ(self->Position(), candidate->Position());
		if (distance < bestDistance)
		{
			bestDistance = distance;
			best = candidate;
		}
	}

	return best;
}

bool EncounterSimulation::HasLineOfSight(const Float3& from, Combatant* target) const
{
	if (target == nullptr)
	{
		return false;
	}

	return _occlusion == nullptr || _occlusion->HasLineOfSight(from, target->Position());
}

int EncounterSimulation::Update(float deltaTime)
{
	if (deltaTime <= 0.f || FixedDeltaTime <= 0.f)
	{
		return 0;
	}

	_accumulator += deltaTime;

	int steps = 0;
	while (_accumulator >= FixedDeltaTime && steps < MaxStepsPerUpdate)
	{
		_accumulator -= FixedDeltaTime;
		Step();
		steps++;
	}

	if (steps >= MaxStepsPerUpdate)
	{
		// Drop the backlog rather than trying to catch up forever.
		_accumulator = 0.f;
	}

	return steps;
}

void EncounterSimulation::Advance(float seconds)
{
	if (seconds <= 0.f)
	{
		return;
	}

	const int steps = static_cast<int>(seconds / FixedDeltaTime);
	for (int i = 0; i < steps; i++)
	{
		Step();
	}
}

void EncounterSimulation::Step()
{
	const float deltaTime = FixedDeltaTime;
	if (deltaTime <= 0.f)
	{
		return;
	}

	_time += deltaTime;
	_stepCount++;

	CollectLandings(deltaTime);
	InterruptStaggeredCasts();
	AdvanceCombatants(deltaTime);
	Decide();
	Move(deltaTime);
	ResolveLandings();
	RecountHostiles();
}

void EncounterSimulation::Reset()
{
	for (Participant& participant : _participants)
	{
		const float facing = participant.Self->FacingDegrees();
		participant.Self->Revive(participant.HomePosition, facing);
		participant.Abilities.ResetCooldowns();
		participant.PendingIntent = CombatIntent::None();
		participant.LastLandedAbility = -1;

		if (participant.Brain != nullptr)
		{
			participant.Brain->Reset();
		}
	}

	_accumulator = 0.f;
	RecountHostiles();
}

void EncounterSimulation::RestoreRng(std::uint64_t state, std::uint64_t increment)
{
	_rng = DeterministicRng::Restore(state, increment);
}

Participant* EncounterSimulation::Attach(Combatant* combatant,
	const std::vector<std::shared_ptr<AbilityDefinition>>& abilities,
	int attackAbilityIndex)
{
	_participants.emplace_back(combatant, abilities, attackAbilityIndex);
	Participant& participant = _participants.back();

	participant.DamagedBinding = combatant->Damaged.Bind(this, &EncounterSimulation::HandleDamaged);
	participant.DiedBinding = combatant->Died.Bind(this, &EncounterSimulation::HandleDied);

	_combatants.push_back(combatant);
	RecountHostiles();

	return &participant;
}

void EncounterSimulation::HandleDamaged(void* context, Combatant& victim, Combatant* attacker,
	const DamageResult& result)
{
	static_cast<EncounterSimulation*>(context)->OnCombatantDamaged(&victim, attacker, result);
}

void EncounterSimulation::HandleDied(void* context, Combatant& victim)
{
	static_cast<EncounterSimulation*>(context)->OnCombatantDied(&victim);
}

void EncounterSimulation::OnCombatantDamaged(Combatant* victim, Combatant* attacker,
	const DamageResult& result)
{
	Participant* victimParticipant = FindParticipant(victim);

	// Being hit always alerts, regardless of facing, so an enemy cannot be
	// killed from behind its vision cone without ever reacting.
	if (victimParticipant != nullptr && victimParticipant->Brain != nullptr && victim->IsAlive())
	{
		victimParticipant->Brain->ForceAlert();
	}

	PropagateAggro(victim);

	DamageDealt.Broadcast(FindParticipant(attacker), victim, result);
}

void EncounterSimulation::OnCombatantDied(Combatant* victim)
{
	Died.Broadcast(victim, FindParticipant(victim));
}

void EncounterSimulation::PropagateAggro(Combatant* victim)
{
	if (AggroPropagationRadius <= 0.f || victim == nullptr)
	{
		return;
	}

	for (Participant& ally : _participants)
	{
		if (ally.Brain == nullptr || !ally.Self->IsAlive() || ally.Self == victim)
		{
			continue;
		}

		if (ally.Self->GetFaction() != victim->GetFaction())
		{
			continue;
		}

		if (Float3::DistanceXZ(ally.Self->Position(), victim->Position()) <= AggroPropagationRadius)
		{
			ally.Brain->ForceAlert();
		}
	}
}

void EncounterSimulation::CollectLandings(float deltaTime)
{
	_landings.clear();

	for (Participant& participant : _participants)
	{
		const int landed = participant.Abilities.Tick(deltaTime);
		participant.LastLandedAbility = landed;

		if (landed >= 0)
		{
			_landings.push_back(Landing{ &participant, landed });
		}
	}
}

/// A caster staggered mid-windup loses the ability. This is what makes
/// interrupts meaningful: without it, staggering would only delay a blow that
/// still lands a moment later.
void EncounterSimulation::InterruptStaggeredCasts()
{
	for (Participant& participant : _participants)
	{
		if (participant.Self->IsStunned() && participant.Abilities.Phase() == CastPhase::Windup)
		{
			participant.Abilities.Interrupt();
		}
	}
}

void EncounterSimulation::AdvanceCombatants(float deltaTime)
{
	for (Combatant* combatant : _combatants)
	{
		combatant->Tick(deltaTime);
	}
}

void EncounterSimulation::Decide()
{
	for (Participant& participant : _participants)
	{
		if (!participant.IsAlive())
		{
			continue;
		}

		CombatIntent intent = DecideFor(participant);
		participant.PendingIntent = intent;

		if (intent.ActivateAbility && intent.AbilityIndex >= 0)
		{
			AbilityFailure failure;
			participant.Abilities.TryActivate(intent.AbilityIndex, failure);
		}
	}
}

CombatIntent EncounterSimulation::DecideFor(Participant& participant)
{
	CombatIntent intent = CombatIntent::None();
	Combatant* self = participant.Self;

	if (participant.Driver != nullptr)
	{
		// The driver states explicitly whether it wants an ability, so there
		// is nothing to infer here.
		return participant.Driver->Decide(FixedDeltaTime, *self, *this);
	}

	if (participant.Brain == nullptr)
	{
		return intent;
	}

	Combatant* target = ResolveTarget(participant);
	const float moveSpeed = self->EffectiveMoveSpeed();

	const AiContext context(
		self->Position(),
		self->Forward(),
		participant.HomePosition,
		target != nullptr,
		target != nullptr ? target->Position() : Float3::Zero,
		target != nullptr && target->IsAlive(),
		HasLineOfSight(self->Position(), target),
		self->IsAlive(),
		self->IsStunned(),
		participant.Abilities.IsReady(participant.AttackAbilityIndex),
		moveSpeed);

	const AiIntent ai = participant.Brain->Tick(FixedDeltaTime, context);
	participant.LastAiState = participant.Brain->State();

	intent.MoveDirection = ai.MoveDirection;

	// The brain expresses speed against the combatant's effective movement
	// speed, so converting back to a 0..1 scale lets both drivers share one
	// movement path without slowing the enemy twice.
	intent.SpeedScale = moveSpeed <= CoreMath::Epsilon ? 0.f : ai.DesiredSpeed / moveSpeed;
	intent.LookAtTarget = ai.FaceTarget;

	intent.ActivateAbility = ai.WantsToAttack;
	intent.AbilityIndex = participant.AttackAbilityIndex;

	return intent;
}

/// Nearest living hostile, used both for facing and for AI context.
Combatant* EncounterSimulation::ResolveTarget(Participant& participant)
{
	return FindNearestHostile(participant.Self, 1e30f);
}

void EncounterSimulation::Move(float deltaTime)
{
	for (Participant& participant : _participants)
	{
		if (!participant.IsAlive())
		{
			continue;
		}

		ApplyMovement(participant, participant.PendingIntent, deltaTime);
	}
}

void EncounterSimulation::ApplyMovement(Participant& participant, const CombatIntent& intent,
	float deltaTime)
{
	Combatant* combatant = participant.Self;
	Float3 delta = Float3::Zero;

	if (!combatant->IsStunned())
	{
		const Float3 direction = intent.MoveDirection.FlattenedXZ();

		if (direction != Float3::Zero)
		{
			const float scale = CoreMath::Clamp(intent.SpeedScale, 0.f, 2.f);
			const float speed = combatant->EffectiveMoveSpeed() * scale
				* participant.Abilities.MoveSpeedMultiplier();

			if (speed > 0.f)
			{
				delta += direction * (speed * deltaTime);
			}
		}
	}

	if (combatant->KnockbackSpeed() > 0.f)
	{
		delta += combatant->KnockbackDirection() * (combatant->KnockbackSpeed() * deltaTime);
	}

	if (delta != Float3::Zero)
	{
		combatant->SetPosition(_bounds.Clamp(combatant->Position() + delta));
	}

	if (intent.LookAtTarget)
	{
		Combatant* target = FindNearestHostile(combatant, 1e30f);
		if (target != nullptr)
		{
			combatant->TurnTowards(target->Position() - combatant->Position(),
				participant.TurnSpeedDegreesPerSecond, deltaTime);
		}

		return;
	}

	if (intent.LookDirection != Float3::Zero)
	{
		combatant->TurnTowards(intent.LookDirection, participant.TurnSpeedDegreesPerSecond, deltaTime);
		return;
	}

	if (intent.MoveDirection != Float3::Zero)
	{
		combatant->TurnTowards(intent.MoveDirection, participant.TurnSpeedDegreesPerSecond, deltaTime);
	}
}

void EncounterSimulation::ResolveLandings()
{
	for (const Landing& landing : _landings)
	{
		Participant* participant = landing.ParticipantPtr;

		if (!participant->Self->IsAlive())
		{
			continue;
		}

		const AbilityDefinition* ability = participant->Abilities[landing.AbilityIndex];
		if (ability == nullptr)
		{
			continue;
		}

		AttackResolver::Resolve(*participant->Self, *ability, _combatants, &_rng, _hitBuffer);
	}
}

void EncounterSimulation::RecountHostiles()
{
	int alive = 0;
	for (Combatant* combatant : _combatants)
	{
		if (combatant->IsAlive() && combatant->GetFaction() == Faction::Hostile)
		{
			alive++;
		}
	}

	_hostilesRemaining = alive;
}

} // namespace ShadowboundCore
