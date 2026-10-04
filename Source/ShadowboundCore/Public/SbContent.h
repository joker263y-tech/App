#pragma once

#include <memory>
#include <string>
#include <vector>

#include "SbCombatant.h"
#include "SbAbilities.h"
#include "SbEnemyBrain.h"

namespace ShadowboundCore::Content
{

// Identifiers, ported from the C# GameContent constants.
constexpr const char* ArchetypeHollowWalker = "hollow-walker";
constexpr const char* ArchetypeCinderHound = "cinder-hound";
constexpr const char* ArchetypeVeilwarden = "veilwarden";
constexpr const char* ArchetypeAshenSentinel = "ashen-sentinel";

constexpr const char* RegionCamp = "last-ember-camp";
constexpr const char* RegionWilds = "grey-wilds";
constexpr const char* RegionRuins = "hollowed-ruins";
constexpr const char* RegionWard = "sunken-ward";
constexpr const char* RegionSanctum = "umbral-sanctum";

// -----------------------------------------------------------------------------
// A reusable enemy template: the stats, abilities and behaviour every
// instance of a creature shares.
//
// Distinct from a Combatant, which is one living instance. Three Hollow
// Walkers are three combatants built from one archetype, each with its own
// health, position and status effects.
// -----------------------------------------------------------------------------
struct EnemyArchetype
{
	std::string Id;
	std::string DisplayName;

	/// Flavour text shown on the first encounter. Original to this game.
	std::string Description;

	int Level = 1;

	float MaxHealth = 100.f;
	float AttackPower = 10.f;
	float ShadowPower = 0.f;
	float Armor = 0.f;
	float MoveSpeed = 4.f;
	float CritChance = 0.f;
	float CritMultiplier = 1.5f;
	float StatusResistance = 0.f;
	float HealthRegen = 0.f;

	/// Resistance per damage school. Defaults to none.
	ResistanceSet Resistances;

	int ExperienceReward = 10;
	std::string LootTableId;

	bool IsBoss = false;

	/// Index into Abilities that the AI attacks with.
	int AttackAbilityIndex = 0;

	std::vector<std::shared_ptr<AbilityDefinition>> Abilities;
	EnemyBrainSettings Brain;

	/// Visually, how large this creature is. The placeholder art uses it for scale.
	float BodyScale = 1.f;

	/// Colour used for placeholder materials.
	float TintRgb[3] = { 0.5f, 0.5f, 0.5f };

	/// Builds one living instance at a position.
	std::unique_ptr<Combatant> Create(const std::string& instanceId, const Float3& position,
		int levelOverride = 0) const;

	/// Writes base stats, scaling health and power with level for enemies
	/// placed above or below their archetype's baseline. Without this a
	/// late-game zone would reuse the same numbers as the opening area.
	void ApplyStats(Combatant& combatant, int level) const;
};

/// The Warden's starting kit. Index 0 is the basic attack, which is what the
/// AI and the default input binding assume. Ported with the C# numbers.
std::vector<std::shared_ptr<AbilityDefinition>> BuildPlayerAbilities();

/// A level 1 Warden at full health, ready to place in a scene.
std::unique_ptr<Combatant> CreatePlayer(const std::string& id = "warden");

/// All four creature archetypes (stats, abilities, brain tuning) as authored.
std::vector<EnemyArchetype> BuildEnemyArchetypes();

/// Finds an archetype by id, or nullptr. Built once, then queried.
const EnemyArchetype* FindArchetype(const std::string& id);

} // namespace ShadowboundCore::Content
