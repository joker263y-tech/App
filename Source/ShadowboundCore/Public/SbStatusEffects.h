#pragma once

#include <vector>

#include "SbDamage.h"
#include "SbEvent.h"
#include "SbNumerics.h"

namespace ShadowboundCore
{

class StatSet;
class VitalsPool;
class Combatant;

// -----------------------------------------------------------------------------
// Status effects in the game. Members fall into two behavioural groups,
// described on StatusEffectSystem.
// -----------------------------------------------------------------------------
enum class StatusKind : int
{
	/// Damage over time. Ember. Sums across stacks.
	Burning = 0,

	/// Damage over time. Physical. Sums across stacks.
	Bleeding = 1,

	/// Movement and recovery slowed. Strongest instance applies.
	Chilled = 2,

	/// Cannot act. Strongest instance applies.
	Staggered = 3,

	/// Damage taken reduced. Strongest instance applies.
	Warded = 4,

	/// Damage dealt increased. Strongest instance applies.
	Empowered = 5,

	/// Damage taken increased, from being marked by the Umbra. Strongest instance applies.
	Marked = 6
};

/// What happens when a status is applied while one of its kind is already active.
enum class StatusStackRule : int
{
	/// Resets the duration of the existing effect and keeps the stronger magnitude.
	Refresh = 0,

	/// Adds another stack, up to MaxStacks.
	Stack = 1,

	/// Ignored unless the incoming magnitude is stronger.
	Ignore = 2
};

// -----------------------------------------------------------------------------
// One live status effect on one combatant. Mutable by design: the system ticks
// durations and accumulators in place to avoid per-frame allocation.
// -----------------------------------------------------------------------------
struct StatusEffect
{
	StatusKind Kind = StatusKind::Burning;

	/// School used when this effect deals damage over time.
	EDamageType DamageType = EDamageType::Physical;

	/// Meaning depends on Kind: damage per tick for damage over time, or a
	/// 0..1 fraction for the modifier kinds.
	float Magnitude = 0.f;

	/// Total duration in seconds, as originally applied.
	float Duration = 0.f;

	/// Seconds left before expiry.
	float Remaining = 0.f;

	/// Seconds between damage ticks. Zero means the effect never ticks damage.
	float TickInterval = 0.f;

	/// Accumulated time toward the next tick.
	float TickAccumulator = 0.f;

	/// Number of stacked instances, never below 1 while active.
	int Stacks = 1;

	int MaxStacks = 1;

	StatusStackRule StackRule = StatusStackRule::Refresh;

	/// The dealer that applied this effect, for damage attribution and kill credit.
	const Combatant* Source = nullptr;

	bool IsExpired() const { return Remaining <= 0.f; }

	/// True when this effect should deal damage on its tick.
	bool DealsDamageOverTime() const { return TickInterval > 0.f && Magnitude > 0.f; }

	float FractionRemaining() const { return Duration <= 0.f ? 0.f : Remaining / Duration; }

	/// Damage this effect applies on a single tick, across all stacks.
	float DamagePerTick() const { return Magnitude * static_cast<float>(Stacks); }

	static StatusEffect Dot(StatusKind kind, EDamageType type, float damagePerTick,
		float duration, float tickInterval, const Combatant* source, int maxStacks);

	static StatusEffect Modifier(StatusKind kind, float magnitude, float duration,
		const Combatant* source);
};

// -----------------------------------------------------------------------------
// Holds and advances the status effects on one combatant.
//
// Two behavioural groups, chosen so that neither can run away:
//
//   Damage over time (Burning, Bleeding) - magnitudes SUM across stacks.
//   Five bleeds means five bleeds' worth of damage per tick. Stacks are
//   capped per effect.
//
//   Modifiers (Chilled, Warded, Empowered, Marked, Staggered) - the STRONGEST
//   instance applies, not the sum. Stacking slows additively would let a group
//   of enemies freeze the player permanently, so the system takes the maximum
//   instead and lets duration do the work.
//
// Incoming durations are scaled by the target's Resolve (StatusResistance).
// -----------------------------------------------------------------------------
class StatusEffectSystem
{
public:
	/// Longest a single application can last, after resistance. A safety ceiling.
	static constexpr float MaxDuration = 30.f;

	StatusEffectSystem(StatSet& stats, VitalsPool& vitals);

	/// Raised when an effect is newly applied or refreshed, for VFX.
	TEvent<const StatusEffect&> Applied;
	TEvent<const StatusEffect&> Expired;

	/// Raised when an effect deals damage. Arguments: effect, damage applied.
	TEvent<const StatusEffect&, float> Ticked;

	const std::vector<StatusEffect>& Active() const { return _active; }

	int ActiveCount() const { return static_cast<int>(_active.size()); }

	/// True while staggered, which blocks all actions including movement.
	bool IsStunned() const;

	/// Combined movement speed multiplier from slows, in 0..1.
	float MoveSpeedMultiplier() const;

	/// Multiplier applied to cooldown recovery. Chilled makes abilities come back slower.
	float CooldownRateMultiplier() const;

	/// Multiplier applied to outgoing damage, from Empowered.
	float DamageDealtMultiplier() const;

	/// Multiplier applied to incoming damage. Warded reduces it, Marked
	/// increases it, and both compose.
	float DamageTakenMultiplier() const;

	/// Applies an effect, honouring its stacking rule and the target's Resolve.
	/// The incoming instance is not stored directly, so callers can safely
	/// reuse a template object.
	void Apply(const StatusEffect& incoming);

	/// Advances all effects by deltaTime, applying damage over time and
	/// removing expired effects. Effects that tick more than once in a long
	/// frame are caught up rather than losing ticks.
	void Tick(float deltaTime);

	bool Has(StatusKind kind) const;
	int StackCount(StatusKind kind) const;

	/// Effective magnitude of a kind. For damage over time the total per tick
	/// across stacks; for modifiers the strongest single instance. 0 when absent.
	float Magnitude(StatusKind kind) const;

	bool TryGet(StatusKind kind, StatusEffect& outEffect) const;

	/// Removes every instance of a kind. Used by cleanse abilities.
	int RemoveAll(StatusKind kind);

	/// Removes all damage-over-time effects. The one cleanse the game needs.
	int RemoveAllDamageOverTime();

	void Clear();

	static bool IsDamageOverTime(StatusKind kind)
	{
		return kind == StatusKind::Burning || kind == StatusKind::Bleeding;
	}

private:
	const StatusEffect* Find(StatusKind kind) const;
	StatusEffect* Find(StatusKind kind);
	float StrongestMagnitude(StatusKind kind) const;
	float ScaleDuration(float duration) const;

	std::vector<StatusEffect> _active;
	StatSet& _stats;
	VitalsPool& _vitals;
};

} // namespace ShadowboundCore
