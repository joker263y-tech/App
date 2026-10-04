#pragma once

#include <memory>
#include <string>
#include <vector>

#include "SbAbilities.h"
#include "SbCombatant.h"
#include "SbEnemyBrain.h"
#include "SbEvent.h"
#include "SbNumerics.h"
#include "SbRng.h"

namespace ShadowboundCore
{

// -----------------------------------------------------------------------------
// A rectangular slice of world an encounter happens inside.
// -----------------------------------------------------------------------------
struct WorldBounds
{
	Float3 Min;
	Float3 Max;

	WorldBounds() : Min(Float3::Zero), Max(Float3::Zero) {}
	WorldBounds(const Float3& min, const Float3& max) : Min(min), Max(max) {}

	/// A square arena, which is what placeholder content is authored against.
	static WorldBounds Square(float halfExtent, float minY = -1.f, float maxY = 6.f)
	{
		return WorldBounds(Float3(-halfExtent, minY, -halfExtent), Float3(halfExtent, maxY, halfExtent));
	}

	/// Bounds that constrain nothing. Used by tests that want free movement.
	static WorldBounds Unbounded()
	{
		const float limit = 1e7f;
		return WorldBounds(Float3(-limit, -limit, -limit), Float3(limit, limit, limit));
	}

	Float3 Size() const { return Max - Min; }

	bool Contains(const Float3& point) const
	{
		return point.X >= Min.X && point.X <= Max.X
			&& point.Y >= Min.Y && point.Y <= Max.Y
			&& point.Z >= Min.Z && point.Z <= Max.Z;
	}

	Float3 Clamp(const Float3& point) const
	{
		return Float3(
			CoreMath::Clamp(point.X, Min.X, Max.X),
			CoreMath::Clamp(point.Y, Min.Y, Max.Y),
			CoreMath::Clamp(point.Z, Min.Z, Max.Z));
	}
};

// -----------------------------------------------------------------------------
// What a driven combatant wants to do this step. The player's input layer and
// the enemy brain both ultimately produce one of these, which is why the two
// share a single movement path.
// -----------------------------------------------------------------------------
struct CombatIntent
{
	/// Unit direction to move, or zero to hold.
	Float3 MoveDirection;

	/// Fraction of the combatant's effective movement speed to use, 0..1.
	float SpeedScale = 0.f;

	/// Turn to face the current target.
	bool LookAtTarget = false;

	/// Turn to face a specific direction, when not facing the target.
	Float3 LookDirection;

	/// Whether an ability should be activated this step. Stated as an explicit
	/// flag rather than inferring "no ability" from a sentinel index, because a
	/// default-constructed struct would otherwise silently fire ability 0.
	bool ActivateAbility = false;

	/// Ability index to activate. Only read when ActivateAbility is set.
	int AbilityIndex = -1;

	/// Preferred target index into the participant list, or -1 for automatic.
	int TargetIndex = -1;

	static CombatIntent None()
	{
		CombatIntent intent;
		intent.AbilityIndex = -1;
		intent.TargetIndex = -1;
		intent.SpeedScale = 0.f;
		return intent;
	}
};

class EncounterSimulation;

// -----------------------------------------------------------------------------
// Drives one combatant. The engine layer supplies an input-driven
// implementation, and tests supply a scripted one, so neither the simulation
// nor the combat rules need to know where intent comes from.
// -----------------------------------------------------------------------------
class ICombatantDriver
{
public:
	virtual ~ICombatantDriver() = default;
	virtual CombatIntent Decide(float deltaTime, Combatant& self, EncounterSimulation& world) = 0;
};

// -----------------------------------------------------------------------------
// Answers whether one point can see another. Kept behind an interface because
// the core has no geometry: the engine layer supplies a line trace, and tests
// supply one that always or never sees.
// -----------------------------------------------------------------------------
class IOcclusionProvider
{
public:
	virtual ~IOcclusionProvider() = default;
	virtual bool HasLineOfSight(const Float3& from, const Float3& to) const = 0;
};

/// Visibility that is always clear. The default when no provider is given.
class OpenSight : public IOcclusionProvider
{
public:
	bool HasLineOfSight(const Float3& /*from*/, const Float3& /*to*/) const override { return true; }
};

// -----------------------------------------------------------------------------
// One combatant in a running encounter, with whatever drives it.
// -----------------------------------------------------------------------------
struct Participant
{
	Combatant* Self = nullptr;
	AbilityController Abilities;

	/// Set for enemies. Null for the player.
	std::unique_ptr<EnemyBrain> Brain;

	/// Set for the player. Null for enemies. Non-owning.
	ICombatantDriver* Driver = nullptr;

	/// Where this combatant belongs. Enemies return here when they lose interest.
	Float3 HomePosition;

	float TurnSpeedDegreesPerSecond = 540.f;

	/// Which ability the AI should attack with.
	int AttackAbilityIndex = 0;

	bool IsPlayer = false;

	/// Latest AI state, for animation and diagnostics.
	AiState LastAiState = AiState::Idle;

	/// Index of the ability that landed on the most recent step, or -1.
	int LastLandedAbility = -1;

	/// Intent decided this step, applied during the movement phase.
	CombatIntent PendingIntent;

	/// Event binding handles this encounter placed on the combatant, released
	/// again when the encounter is destroyed. The player combatant outlives
	/// encounters (a new one is built when the region changes), so unbinding is
	/// not optional: a stale handler would run against freed memory.
	int DamagedBinding = 0;
	int DiedBinding = 0;

	Participant(Combatant* combatant,
		const std::vector<std::shared_ptr<AbilityDefinition>>& abilities,
		int attackAbilityIndex)
		: Self(combatant)
		, Abilities(*combatant, abilities)
		, HomePosition(combatant->Position())
		, AttackAbilityIndex(attackAbilityIndex)
		, PendingIntent(CombatIntent::None())
	{
	}

	Participant(const Participant&) = delete;
	Participant& operator=(const Participant&) = delete;
	Participant(Participant&&) = default;
	Participant& operator=(Participant&&) = default;

	bool IsAlive() const { return Self != nullptr && Self->IsAlive(); }
	bool IsPlayerControlled() const { return IsPlayer; }
};

// -----------------------------------------------------------------------------
// Runs one encounter: a fixed-timestep loop that advances abilities, status
// effects, AI decisions, movement and damage in a defined order.
//
// The whole encounter is deterministic. Given the same seed, the same
// combatants and the same inputs, every step produces identical results, so a
// failing fight can be replayed from a seed rather than guessed at.
//
// Step order is fixed and deliberate:
//   1. Advance ability controllers; collect blows that land this step.
//   2. Interrupt any cast whose caster has just been staggered.
//   3. Advance statuses, regeneration and knockback decay.
//   4. Decide intent for every living combatant.
//   5. Apply movement and turning.
//   6. Resolve the blows collected in step 1, at the positions reached in
//      step 5.
//
// Resolving after movement is what makes a swing land where the target
// actually is at the moment of impact, rather than where it was when the
// animation started.
// -----------------------------------------------------------------------------
class EncounterSimulation
{
public:
	EncounterSimulation(DeterministicRng rng, WorldBounds bounds,
		IOcclusionProvider* occlusion = nullptr);

	~EncounterSimulation();

	/// The single random source for the session, shared by loot and combat.
	/// Keeping two streams would mean a save recorded only one of them.
	DeterministicRng& Rng() { return _rng; }
	const DeterministicRng& Rng() const { return _rng; }

	WorldBounds Bounds() const { return _bounds; }
	void SetBounds(const WorldBounds& bounds) { _bounds = bounds; }

	IOcclusionProvider* Occlusion() const { return _occlusion; }
	void SetOcclusion(IOcclusionProvider* provider) { _occlusion = provider; }

	/// Simulation timestep. Combat tuning is expressed against this.
	float FixedDeltaTime = 1.f / 60.f;

	/// Most steps a single Update may run. Prevents a frame hitch from
	/// triggering a spiral where catching up takes longer than the hitch.
	int MaxStepsPerUpdate = 6;

	/// Accumulated simulation time in seconds.
	float Time() const { return _time; }

	int StepCount() const { return _stepCount; }

	/// How many enemies are still standing.
	int HostilesRemaining() const { return _hostilesRemaining; }

	const std::vector<Participant>& Participants() const { return _participants; }
	const std::vector<Combatant*>& Combatants() const { return _combatants; }

	/// Raised for each applied hit. Arguments: attacker participant, victim, result.
	TEvent<Participant*, Combatant*, const DamageResult&> DamageDealt;

	/// Raised once per combatant, when it dies.
	TEvent<Combatant*, Participant*> Died;

	/// How close an ally must be to be alerted when one of its number is hurt.
	/// Without this, a player can pick off a group one at a time from the edge
	/// of the fight while the rest stand and watch.
	float AggroPropagationRadius = 14.f;

	/// Spawns a combatant with a scripted or input driver, for the player or
	/// for anything else that should not have an enemy brain.
	Participant* AddDriven(Combatant* combatant,
		const std::vector<std::shared_ptr<AbilityDefinition>>& abilities,
		ICombatantDriver* driver, bool isPlayer = false);

	/// Spawns an enemy with its own brain.
	Participant* AddEnemy(Combatant* combatant,
		const std::vector<std::shared_ptr<AbilityDefinition>>& abilities,
		const EnemyBrainSettings& brainSettings, int attackAbilityIndex = 0);

	Participant* Find(const std::string& combatantId);
	Participant* FindParticipant(Combatant* combatant);

	/// Nearest living hostile within range, or nullptr.
	Combatant* FindNearestHostile(Combatant* self, float maxRange);

	bool HasLineOfSight(const Float3& from, Combatant* target) const;

	/// Advances the simulation by real elapsed time, running as many fixed
	/// steps as the accumulator allows. Returns the number of steps taken.
	int Update(float deltaTime);

	/// Runs a fixed number of steps. Used by tests and by offline simulation.
	void Advance(float seconds);

	/// Runs exactly one fixed timestep.
	void Step();

	/// Returns every participant to its starting state: full vitals, no
	/// status effects, cleared cooldowns and a reset brain. Dead participants
	/// are revived too - this is the boss-retry path.
	void Reset();

	/// Replaces the generator, for loading a save mid-encounter. The state is
	/// restored exactly so subsequent rolls continue the same sequence.
	void RestoreRng(std::uint64_t state, std::uint64_t increment);

private:
	struct Landing
	{
		Participant* ParticipantPtr;
		int AbilityIndex;
	};

	Participant* Attach(Combatant* combatant,
		const std::vector<std::shared_ptr<AbilityDefinition>>& abilities,
		int attackAbilityIndex);

	static void HandleDamaged(void* context, Combatant& victim, Combatant* attacker,
		const DamageResult& result);
	static void HandleDied(void* context, Combatant& victim);

	void OnCombatantDamaged(Combatant* victim, Combatant* attacker, const DamageResult& result);
	void OnCombatantDied(Combatant* victim);
	void PropagateAggro(Combatant* victim);

	void CollectLandings(float deltaTime);
	void InterruptStaggeredCasts();
	void AdvanceCombatants(float deltaTime);
	void Decide();
	CombatIntent DecideFor(Participant& participant);
	Combatant* ResolveTarget(Participant& participant);
	void Move(float deltaTime);
	void ApplyMovement(Participant& participant, const CombatIntent& intent, float deltaTime);
	void ResolveLandings();
	void RecountHostiles();

	DeterministicRng _rng;
	WorldBounds _bounds;
	IOcclusionProvider* _occlusion;

	std::vector<Participant> _participants;
	std::vector<Combatant*> _combatants;
	std::vector<Combatant*> _hitBuffer;
	std::vector<Landing> _landings;

	float _accumulator;
	float _time;
	int _stepCount;
	int _hostilesRemaining;
};

} // namespace ShadowboundCore
