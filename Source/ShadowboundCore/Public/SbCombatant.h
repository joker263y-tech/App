#pragma once

#include <string>

#include "SbDamage.h"
#include "SbEvent.h"
#include "SbNumerics.h"
#include "SbStats.h"
#include "SbStatusEffects.h"
#include "SbVitals.h"

namespace ShadowboundCore
{

// -----------------------------------------------------------------------------
// Which side a combatant fights for. Determines valid targets.
// -----------------------------------------------------------------------------
enum class Faction : int
{
	/// The player character.
	Player = 0,

	/// Enemies. All hostiles are mutually non-aggressive unless provoked.
	Hostile = 1,

	/// Does not fight and cannot be targeted by default.
	Neutral = 2
};

// -----------------------------------------------------------------------------
// Everything that can fight: the player and every enemy. This is the shared
// state that combat, AI, abilities and status effects all operate on.
//
// The player and enemies differ only in what drives them. Movement and
// ability decisions arrive as a CombatIntent, produced either by the input
// layer or by EnemyBrain. Nothing in this class knows which of the two it is
// talking to.
// -----------------------------------------------------------------------------
class Combatant
{
public:
	Combatant(std::string id, Faction faction, int level = 1);

	Combatant(const Combatant&) = delete;
	Combatant& operator=(const Combatant&) = delete;

	/// Stable identifier. Used for target locking, saves and kill attribution.
	const std::string& Id() const { return _id; }

	Faction GetFaction() const { return _faction; }

	int Level = 1;

	/// Archetype name shown in the HUD, e.g. "Hollow Walker".
	std::string DisplayName;

	StatSet& Stats() { return _stats; }
	const StatSet& Stats() const { return _stats; }

	/// This combatant's resistance profile. Read-only by reference on purpose:
	/// the health pool holds the same instance, so replacing it would leave
	/// damage resolution reading the old one. Use CopyResistancesFrom instead.
	ResistanceSet& Resistances() { return _resistances; }
	const ResistanceSet& Resistances() const { return _resistances; }

	/// Copies a resistance profile's values into this combatant's own profile.
	void CopyResistancesFrom(const ResistanceSet& source);

	VitalsPool& Vitals() { return _vitals; }
	const VitalsPool& Vitals() const { return _vitals; }

	StatusEffectSystem& Statuses() { return _statuses; }
	const StatusEffectSystem& Statuses() const { return _statuses; }

	/// Set on the player and on enemies that can be permanently killed.
	bool IsPersistent = false;

	/// Archetype identifier, such as "hollow-walker". Used as the target id in
	/// quest kill objectives and in encounter bookkeeping.
	std::string ArchetypeId;

	/// Experience granted to whoever defeats this combatant.
	int ExperienceReward = 0;

	/// Loot table rolled on defeat. Empty means nothing drops. (Ported data;
	/// the loot system itself is a later phase.)
	std::string LootTableId;

	/// Bosses additionally satisfy DefeatBoss objectives.
	bool IsBoss = false;

	/// Fired once, the first time this combatant's health reaches zero.
	TEvent<Combatant&> Died;

	/// Raised with the victim, the attacker and the resolved hit. The attacker
	/// is included because kill credit, aggro propagation and loot all need to
	/// know who dealt the blow. Null for environmental damage.
	TEvent<Combatant&, Combatant*, const DamageResult&> Damaged;

	const Float3& Position() const { return _position; }

	/// Facing in degrees around Y, where 0 faces +Z.
	float FacingDegrees() const { return _facingDegrees; }

	bool IsAlive() const { return _vitals.IsAlive(); }

	bool IsStunned() const { return _statuses.IsStunned(); }

	/// Current knockback velocity, decayed by Tick.
	float KnockbackSpeed() const { return _knockbackSpeed; }

	const Float3& KnockbackDirection() const { return _knockbackDirection; }

	void SetPosition(const Float3& position) { _position = position; }

	void SetFacing(float degrees);

	/// Turns toward a direction, limited to degreesPerSecond.
	void TurnTowards(const Float3& direction, float degreesPerSecond, float deltaTime);

	/// Snaps facing to a direction with no turn rate limit.
	void FaceImmediately(const Float3& direction);

	/// Converts a facing angle in degrees to a unit direction on the XZ plane.
	Float3 Forward() const { return Float3::DegreesToDirection(_facingDegrees); }

	/// Applies a knockback impulse. The strongest impulse in a frame wins.
	void ApplyKnockback(const Float3& direction, float speed);

	/// Applies a pre-resolved hit. Damage has already been calculated by
	/// DamageCalculator, so this only mutates state and raises events.
	/// Returns the health actually lost.
	float ReceiveDamage(const DamageResult& result, Combatant* source);

	/// Advances time-based state: statuses, vitals regeneration, knockback
	/// decay, and death announcement. Position is NOT integrated here - the
	/// controller moves the combatant and then calls Tick.
	void Tick(float deltaTime);

	/// How quickly knockback bleeds off, in units per second squared.
	float KnockbackDecayPerSecond = 18.f;

	/// Reads a stat, applying the status multiplier that governs it.
	float EffectiveMoveSpeed() const;
	float EffectiveCooldownRate() const;
	float EffectiveAttackPower() const;
	float EffectiveShadowPower() const;

	/// Revives the combatant at a position with full vitals. Used by respawn
	/// and encounter resets.
	void Revive(const Float3& position, float facingDegrees);

	/// True once death has been broadcast, so listeners cannot double-handle it.
	bool DeathAnnounced() const { return _deathAnnounced; }

	/// Distance between two combatants on the horizontal plane.
	float DistanceTo(const Combatant* other) const;

	bool IsHostileTo(const Combatant* other) const;

private:
	static void HandleVitalsDamaged(void* context, float amount, const Combatant* source);

	void OnVitalsDamaged(float amount, const Combatant* source);
	void AnnounceDeath();

	std::string _id;
	Faction _faction;

	StatSet _stats;
	ResistanceSet _resistances;
	VitalsPool _vitals;
	StatusEffectSystem _statuses;

	Float3 _position;
	DamageResult _pendingResult;
	bool _hasPendingResult;
	float _facingDegrees;
	float _knockbackSpeed;
	Float3 _knockbackDirection;
	bool _deathAnnounced;

	int _vitalsDamagedBinding;
};

} // namespace ShadowboundCore
