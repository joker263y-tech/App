#pragma once

#include "SbDamage.h"
#include "SbEvent.h"
#include "SbStats.h"

namespace ShadowboundCore
{

class Combatant;

// -----------------------------------------------------------------------------
// Health and stamina for one combatant.
//
// Maximums are read live from the StatSet rather than cached, so equipping
// armour or gaining a level immediately changes the ceiling. Health is never
// silently scaled when that ceiling moves; callers decide whether a new
// maximum should grant extra health (level up) or leave the current value
// clamped (armour swap).
//
// Raises events so the presentation layer can react with VFX, audio and UI
// without polling every frame.
// -----------------------------------------------------------------------------
class VitalsPool
{
public:
	VitalsPool(StatSet& stats, ResistanceSet& resistance);

	/// Fired with the amount actually removed and the source that caused it.
	TEvent<float, const Combatant*> Damaged;
	TEvent<float> Healed;

	/// Fired once, when health reaches zero.
	TEvent<> Died;

	/// Fired when stamina is insufficient to cover a requested cost.
	TEvent<> StaminaDepleted;
	TEvent<float> StaminaSpent;

	float Health() const { return _health; }
	float Stamina() const { return _stamina; }

	float MaxHealth() const { return _stats.Get(StatId::MaxHealth); }
	float MaxStamina() const { return _stats.Get(StatId::MaxStamina); }

	ResistanceSet& Resistance() { return _resistance; }
	const ResistanceSet& Resistance() const { return _resistance; }

	float HealthFraction() const;
	float StaminaFraction() const;

	bool IsAlive() const { return _health > 0.f; }
	bool IsInitialized() const { return _initialized; }

	/// Fills health and stamina, and marks the pool usable. Called after stats are configured.
	void ResetToFull();

	/// Regenerates over time and clamps to current maximums.
	void Tick(float deltaTime);

	/// Removes health and returns the amount actually applied (less than
	/// requested when the blow is lethal). Returns 0 for a corpse, so overkill
	/// damage cannot be re-counted by a second attacker.
	float ApplyDamage(float amount, const Combatant* source);

	/// Restores health up to the maximum and returns the amount actually restored.
	float Heal(float amount);

	/// Spends stamina if the full cost is available. Never partially spends: a
	/// failed check leaves the pool untouched, which keeps ability
	/// affordability predictable.
	bool TrySpendStamina(float cost);

	void RestoreStamina(float amount);

	/// Removes all health without raising Died. Used when resetting an encounter.
	void KillSilently();

private:
	StatSet& _stats;
	ResistanceSet& _resistance;

	float _health;
	float _stamina;
	bool _initialized;
};

} // namespace ShadowboundCore
