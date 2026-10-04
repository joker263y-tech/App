#pragma once

#include <vector>

#include "SbAbilities.h"
#include "SbCombatant.h"
#include "SbDamage.h"

namespace ShadowboundCore
{

class DeterministicRng;

// -----------------------------------------------------------------------------
// Turns a landed ability into actual hits: picks targets, rolls damage and
// applies effects.
//
// Separated from AbilityController so that "when does the blow land" and
// "what does the blow hit" can be tested independently. This class is
// stateless and does no allocation on the hot path: the caller supplies a
// reusable buffer for the hit list, and nearest-target selection is done by
// replacement rather than sorting.
// -----------------------------------------------------------------------------
class AttackResolver
{
public:
	/// Selects targets for a landed ability and applies damage and effects.
	/// Fills hits with the combatants actually struck, for presentation.
	/// Returns the number of targets hit.
	static int Resolve(Combatant& attacker, const AbilityDefinition& ability,
		const std::vector<Combatant*>& candidates, DeterministicRng* rng,
		std::vector<Combatant*>& hits);

	/// Fills hits with the hostile combatants the ability reaches, nearest
	/// first, capped at the ability's target limit.
	///
	/// Selection is by repeated replacement rather than by sorting: the
	/// candidate set is small, and this keeps the method allocation-free,
	/// which matters when a boss cleave runs against twenty enemies on a
	/// mobile CPU.
	static void SelectTargets(Combatant& attacker, const AbilityDefinition& ability,
		const std::vector<Combatant*>& candidates, std::vector<Combatant*>& hits);

	/// Resolves and applies one hit against one target.
	static DamageResult Strike(Combatant& attacker, const AbilityDefinition& ability,
		Combatant& target, DeterministicRng* rng);

	/// Rolls and applies an ability's on-hit statuses. Damage-over-time uses
	/// the school declared by the status kind, so burning always burns with
	/// Ember regardless of the ability's own damage type.
	static void ApplyStatuses(Combatant& target, const AbilityDefinition& ability,
		DeterministicRng* rng, Combatant* source);

	/// Builds the runtime status effect for one application.
	static StatusEffect BuildStatus(const StatusApplication& application, EDamageType fallback,
		const Combatant* source);

private:
	static void ApplyStatusesToSelf(Combatant& attacker, const AbilityDefinition& ability);
	static void ApplySelfStagger(Combatant& attacker, float seconds);

	/// Advances a dashing attacker along its facing direction. Bounds are
	/// intentionally not clamped here: the core has no notion of a world yet,
	/// and clamping is the responsibility of whatever owns the space (the
	/// simulation). A dash that would leave the arena is stopped there, not here.
	static void MoveAttacker(Combatant& attacker, const AbilityDefinition& ability);
};

} // namespace ShadowboundCore
