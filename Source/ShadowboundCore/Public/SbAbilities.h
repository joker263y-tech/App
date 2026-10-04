#pragma once

#include <memory>
#include <string>
#include <vector>

#include "SbNumerics.h"
#include "SbStats.h"
#include "SbStatusEffects.h"

namespace ShadowboundCore
{

class Combatant;
class StatusEffect;

// -----------------------------------------------------------------------------
// Broad shape of an ability, which decides how targets are selected.
// -----------------------------------------------------------------------------
enum class AbilityKind : int
{
	/// Single-target strike in front of the attacker.
	Melee = 0,

	/// Wide swing hitting everything in a cone.
	Cleave = 1,

	/// A ranged projectile. Resolved instantly along the facing direction.
	Bolt = 2,

	/// A burst centred on the caster, hitting in all directions.
	Burst = 3,

	/// Movement ability. Deals no damage.
	Dash = 4,

	/// Applies effects to the caster only.
	Self = 5
};

/// A status effect an ability can inflict, with an independent chance to land.
struct StatusApplication
{
	StatusKind Kind = StatusKind::Burning;
	float Magnitude = 0.f;
	float Duration = 0.f;
	float TickInterval = 0.f;
	int MaxStacks = 1;
	float Chance = 1.f;

	StatusApplication() = default;
	StatusApplication(StatusKind kind, float magnitude, float duration,
		float tickInterval = 0.f, int maxStacks = 1, float chance = 1.f)
		: Kind(kind)
		, Magnitude(magnitude)
		, Duration(duration)
		, TickInterval(tickInterval)
		, MaxStacks(maxStacks < 1 ? 1 : maxStacks)
		, Chance(magnitude <= 0.f ? 0.f : (chance < 0.f ? 0.f : (chance > 1.f ? 1.f : chance)))
	{
	}

	/// The damage school a damage-over-time application should use.
	EDamageType ResolveDamageType(EDamageType fallback) const
	{
		switch (Kind)
		{
		case StatusKind::Burning: return EDamageType::Ember;
		case StatusKind::Bleeding: return fallback;
		default: return fallback;
		}
	}
};

// -----------------------------------------------------------------------------
// A single action a combatant can perform. Authored content, not state: the
// same definition is shared by every combatant that knows the ability.
// Runtime state such as cooldown timers lives in AbilityController.
// -----------------------------------------------------------------------------
struct AbilityDefinition
{
	std::string Id = "unnamed";
	std::string DisplayName;
	AbilityKind Kind = AbilityKind::Melee;

	/// School of the direct damage.
	EDamageType DamageType = EDamageType::Physical;

	/// Scale off Umbra instead of Attack. The player's shadow abilities do this.
	bool UsesShadowPower = false;

	float StaminaCost = 0.f;
	float CooldownSeconds = 1.f;

	/// Seconds of telegraph before the blow lands. The counterplay window.
	float WindupSeconds = 0.25f;

	/// Seconds of lockout after the blow. Prevents instant re-casting.
	float RecoverySeconds = 0.3f;

	/// Reach in world units, measured on the horizontal plane.
	float Range = 2.2f;

	/// Half-angle of the hit cone in degrees. 180 means all around.
	float ConeHalfAngleDegrees = 60.f;

	/// Multiplier applied to the attacker's power stat.
	float DamageMultiplier = 1.f;

	float CritChanceBonus = 0.f;
	float Variance = 0.05f;

	/// Maximum targets hit. Zero means unlimited.
	int MaxTargets = 1;

	float KnockbackSpeed = 0.f;

	/// Fraction of movement speed retained while winding up. 0 roots the attacker.
	float MoveSpeedDuringWindup = 0.f;

	/// Seconds the caster is staggered by their own ability. Heavy attacks cost commitment.
	float SelfStaggerSeconds = 0.f;

	/// Distance travelled by a Dash, along the facing direction.
	float DashDistance = 0.f;

	std::vector<StatusApplication> OnHitStatuses;

	/// True when this ability can damage anything at all.
	bool DealsDamage() const
	{
		return Kind != AbilityKind::Dash && Kind != AbilityKind::Self && DamageMultiplier > 0.f;
	}

	/// Total time the caster is committed to this ability.
	float TotalDuration() const { return WindupSeconds + RecoverySeconds; }

	/// Full cooldown including the cast itself.
	float EffectiveCooldown() const { return CooldownSeconds + TotalDuration(); }
};

/// Why an ability activation was refused.
enum class AbilityFailure : int
{
	None = 0,
	UnknownAbility = 1,
	OnCooldown = 2,
	NotEnoughStamina = 3,
	Busy = 4,
	Stunned = 5,
	Dead = 6
};

/// Where the caster is in the commit-to-an-ability cycle.
enum class CastPhase : int
{
	/// Can start a new ability.
	Ready = 0,

	/// Committed, blow has not landed yet.
	Windup = 1,

	/// Blow has landed, still locked out.
	Recovery = 2
};

// -----------------------------------------------------------------------------
// Owns one combatant's abilities: cooldown timers, the cast state machine,
// and the stamina cost.
//
// Deliberately does NOT apply damage. It decides *when* a blow lands and
// reports it; AttackResolver decides *what* it hits. That split keeps timing
// testable without a world, and target selection testable with no timers.
//
// Cooldowns tick down on the caster's own cooldown-rate stat, so a hasted
// combatant recovers faster without special-casing at the call site.
// -----------------------------------------------------------------------------
class AbilityController
{
public:
	AbilityController(Combatant& self, const std::vector<std::shared_ptr<AbilityDefinition>>& abilities);

	int Count() const { return static_cast<int>(_abilities.size()); }

	CastPhase Phase() const { return _phase; }

	/// Index currently being cast, or -1.
	int CastingIndex() const { return _castingIndex; }

	/// True while committed to an ability, either winding up or recovering.
	bool IsBusy() const { return _phase != CastPhase::Ready; }

	/// Seconds left in the current phase. Zero when ready.
	float PhaseRemaining() const { return _phaseTimer; }

	const AbilityDefinition* operator[](int index) const
	{
		return (index >= 0 && index < Count()) ? _abilities[index].get() : nullptr;
	}

	const std::vector<std::shared_ptr<AbilityDefinition>>& Abilities() const { return _abilities; }

	float CooldownRemaining(int index) const;

	/// Fraction of the cooldown still to run, 1 when just used and 0 when ready.
	float CooldownFraction(int index) const;

	bool IsReady(int index) const;

	/// Validates an activation and reports the precise reason for failure, so
	/// the HUD can tell the player whether they are out of stamina, on
	/// cooldown, or mid-swing rather than silently doing nothing.
	AbilityFailure CanActivate(int index) const;

	/// Commits to an ability. Stamina is spent here, not when the blow lands,
	/// so a caster cannot dodge the cost by dying mid-windup.
	bool TryActivate(int index, AbilityFailure& outFailure);

	/// Advances cooldowns and the cast state machine.
	/// Returns the index of the ability whose blow landed this tick, or -1.
	///
	/// The return value is the signal to resolve a hit, and it fires exactly
	/// once per activation, so a caller cannot accidentally apply damage twice
	/// for one swing.
	int Tick(float deltaTime);

	/// Abandons the current cast without landing it. Used when the caster is
	/// staggered mid-windup, which is what makes interrupt abilities work.
	bool Interrupt();

	/// Clears all cooldowns. Used on respawn and encounter reset.
	void ResetCooldowns();

	/// Fraction of movement speed available while casting. Zero when ready,
	/// otherwise the current ability's windup allowance.
	float MoveSpeedMultiplier() const;

private:
	void AdvancePhase(float deltaTime);
	void EndCast();

	Combatant& _self;
	std::vector<std::shared_ptr<AbilityDefinition>> _abilities;
	std::vector<float> _cooldownRemaining;

	int _castingIndex;
	float _phaseTimer;
	CastPhase _phase;
	int _landedThisTick;
};

} // namespace ShadowboundCore
