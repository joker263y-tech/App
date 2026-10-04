#include "SbContent.h"

namespace ShadowboundCore::Content
{

namespace
{

// -----------------------------------------------------------------------------
// The Warden's starting kit, with the C# GameContent numbers verbatim.
// Order matters: index 0 is the basic attack, which is what the AI and the
// default input binding assume.
// -----------------------------------------------------------------------------
std::shared_ptr<AbilityDefinition> NewAbility(const char* id, const char* displayName)
{
	auto ability = std::make_shared<AbilityDefinition>();
	ability->Id = id;
	ability->DisplayName = displayName;
	return ability;
}

EnemyBrainSettings AggressiveBrain(float attackRange, float reactionTime, float viewDistance)
{
	EnemyBrainSettings settings;
	settings.ViewDistance = viewDistance;
	settings.ViewHalfAngleDegrees = 80.f;
	settings.ProximityRadius = 3.5f;
	settings.DeaggroRange = 34.f;
	settings.LoseSightGrace = 5.f;
	settings.AttackRange = attackRange;
	settings.PreferredRange = attackRange * 0.8f;
	settings.ReactionTime = reactionTime;
	settings.IdleDuration = 4.f;
	settings.PatrolRadius = 10.f;
	settings.PatrolSpeedMultiplier = 0.4f;
	settings.ChaseSpeedMultiplier = 1.f;
	settings.BackoffSpeedMultiplier = 0.5f;
	settings.AttackCommitment = 1.f;
	settings.PatrolWaypointTimeout = 7.f;
	return settings;
}

} // namespace

std::vector<std::shared_ptr<AbilityDefinition>> BuildPlayerAbilities()
{
	std::vector<std::shared_ptr<AbilityDefinition>> abilities;
	abilities.reserve(5);

	// 0 - fast, cheap, the bread and butter.
	{
		auto ability = NewAbility("ember-edge", "Ember Edge");
		ability->Kind = AbilityKind::Melee;
		ability->DamageType = EDamageType::Physical;
		ability->StaminaCost = 8.f;
		ability->CooldownSeconds = 0.35f;
		ability->WindupSeconds = 0.18f;
		ability->RecoverySeconds = 0.16f;
		ability->Range = 2.6f;
		ability->ConeHalfAngleDegrees = 70.f;
		ability->DamageMultiplier = 1.f;
		ability->Variance = 0.06f;
		ability->MaxTargets = 1;
		ability->MoveSpeedDuringWindup = 0.3f;
		ability->KnockbackSpeed = 3.f;
		abilities.push_back(ability);
	}

	// 1 - the Umbra answer. Scales off Shadow Power, not Attack.
	{
		auto ability = NewAbility("umbra-lance", "Umbra Lance");
		ability->Kind = AbilityKind::Bolt;
		ability->DamageType = EDamageType::Shadow;
		ability->UsesShadowPower = true;
		ability->StaminaCost = 22.f;
		ability->CooldownSeconds = 2.2f;
		ability->WindupSeconds = 0.35f;
		ability->RecoverySeconds = 0.3f;
		ability->Range = 12.f;
		ability->ConeHalfAngleDegrees = 12.f;
		ability->DamageMultiplier = 1.35f;
		ability->Variance = 0.08f;
		ability->MaxTargets = 1;
		ability->MoveSpeedDuringWindup = 0.2f;
		ability->OnHitStatuses.emplace_back(StatusKind::Marked, 0.2f, 5.f, 0.f, 1);
		abilities.push_back(ability);
	}

	// 2 - mobility. Deals no damage by design.
	{
		auto ability = NewAbility("ashstep", "Ashstep");
		ability->Kind = AbilityKind::Dash;
		ability->DamageType = EDamageType::Physical;
		ability->StaminaCost = 18.f;
		ability->CooldownSeconds = 3.f;
		ability->WindupSeconds = 0.08f;
		ability->RecoverySeconds = 0.12f;
		ability->DashDistance = 5.5f;
		ability->MoveSpeedDuringWindup = 0.f;
		abilities.push_back(ability);
	}

	// 3 - the heavy answer. Wide, slow, and it costs the caster.
	{
		auto ability = NewAbility("sunder", "Sunder");
		ability->Kind = AbilityKind::Cleave;
		ability->DamageType = EDamageType::Physical;
		ability->StaminaCost = 32.f;
		ability->CooldownSeconds = 5.f;
		ability->WindupSeconds = 0.55f;
		ability->RecoverySeconds = 0.45f;
		ability->Range = 3.4f;
		ability->ConeHalfAngleDegrees = 130.f;
		ability->DamageMultiplier = 2.1f;
		ability->Variance = 0.1f;
		ability->MaxTargets = 4;
		ability->KnockbackSpeed = 9.f;
		ability->MoveSpeedDuringWindup = 0.f;
		ability->SelfStaggerSeconds = 0.35f;
		abilities.push_back(ability);
	}

	// 4 - the defensive option.
	{
		auto ability = NewAbility("ward-of-embers", "Ward of Embers");
		ability->Kind = AbilityKind::Self;
		ability->StaminaCost = 25.f;
		ability->CooldownSeconds = 14.f;
		ability->WindupSeconds = 0.3f;
		ability->RecoverySeconds = 0.4f;
		ability->Range = 0.f;
		ability->MaxTargets = 0;
		ability->DamageMultiplier = 0.f;
		ability->MoveSpeedDuringWindup = 0.5f;
		ability->OnHitStatuses.emplace_back(StatusKind::Warded, 0.45f, 8.f, 0.f, 1);
		ability->OnHitStatuses.emplace_back(StatusKind::Empowered, 0.2f, 8.f, 0.f, 1);
		abilities.push_back(ability);
	}

	return abilities;
}

std::unique_ptr<Combatant> CreatePlayer(const std::string& id)
{
	auto player = std::make_unique<Combatant>(id.empty() ? std::string("warden") : id,
		Faction::Player, 1);
	player->DisplayName = "Warden";
	player->ArchetypeId = "warden";
	player->IsPersistent = true;

	StatSet& stats = player->Stats();
	stats.SetBase(StatId::MaxHealth, 320.f);
	stats.SetBase(StatId::MaxStamina, 100.f);
	stats.SetBase(StatId::AttackPower, 18.f);
	stats.SetBase(StatId::ShadowPower, 16.f);
	stats.SetBase(StatId::Armor, 10.f);
	stats.SetBase(StatId::MoveSpeed, 5.5f);
	stats.SetBase(StatId::CritChance, 0.05f);
	stats.SetBase(StatId::CritMultiplier, 1.6f);
	stats.SetBase(StatId::CooldownRate, 1.f);
	stats.SetBase(StatId::HealthRegen, 1.5f);
	stats.SetBase(StatId::StaminaRegen, 14.f);
	stats.SetBase(StatId::StatusResistance, 0.1f);

	player->Vitals().ResetToFull();
	return player;
}

// ================================ creatures =================================

namespace
{

EnemyArchetype BuildHollowWalker()
{
	EnemyArchetype archetype;
	archetype.Id = ArchetypeHollowWalker;
	archetype.DisplayName = "Hollow Walker";
	archetype.Description = "It was a person. Whatever is left walks, and reaches.";
	archetype.Level = 1;
	archetype.MaxHealth = 90.f;
	archetype.AttackPower = 14.f;
	archetype.Armor = 5.f;
	archetype.MoveSpeed = 3.6f;
	archetype.CritChance = 0.02f;
	archetype.ExperienceReward = 35;
	archetype.LootTableId = "loot-hollow-walker";
	archetype.BodyScale = 0.95f;
	archetype.TintRgb[0] = 0.42f;
	archetype.TintRgb[1] = 0.40f;
	archetype.TintRgb[2] = 0.36f;
	archetype.AttackAbilityIndex = 0;
	archetype.Brain = AggressiveBrain(2.4f, 0.5f, 15.0f);

	{
		auto ability = NewAbility("hollow-claw", "Hollow Claw");
		ability->Kind = AbilityKind::Melee;
		ability->DamageType = EDamageType::Physical;
		ability->CooldownSeconds = 1.1f;
		ability->WindupSeconds = 0.45f;
		ability->RecoverySeconds = 0.35f;
		ability->Range = 2.4f;
		ability->ConeHalfAngleDegrees = 80.f;
		ability->DamageMultiplier = 1.f;
		ability->Variance = 0.08f;
		ability->MaxTargets = 1;
		ability->MoveSpeedDuringWindup = 0.15f;
		ability->KnockbackSpeed = 2.f;
		archetype.Abilities.push_back(ability);
	}

	return archetype;
}

EnemyArchetype BuildCinderHound()
{
	EnemyArchetype archetype;
	archetype.Id = ArchetypeCinderHound;
	archetype.DisplayName = "Cinder Hound";
	archetype.Description = "It burned to death and kept running. Fire still likes it.";
	archetype.Level = 3;
	archetype.MaxHealth = 130.f;
	archetype.AttackPower = 20.f;
	archetype.Armor = 0.f;
	archetype.MoveSpeed = 5.2f;
	archetype.CritChance = 0.08f;
	archetype.Resistances.Set(EDamageType::Ember, 0.6f);
	archetype.Resistances.Set(EDamageType::Frost, -0.25f);
	archetype.ExperienceReward = 60;
	archetype.LootTableId = "loot-cinder-hound";
	archetype.BodyScale = 0.8f;
	archetype.TintRgb[0] = 0.72f;
	archetype.TintRgb[1] = 0.32f;
	archetype.TintRgb[2] = 0.16f;
	archetype.AttackAbilityIndex = 0;
	archetype.Brain = AggressiveBrain(2.2f, 0.32f, 17.0f);

	{
		auto ability = NewAbility("cinder-maul", "Cinder Maul");
		ability->Kind = AbilityKind::Melee;
		ability->DamageType = EDamageType::Physical;
		ability->CooldownSeconds = 1.4f;
		ability->WindupSeconds = 0.38f;
		ability->RecoverySeconds = 0.3f;
		ability->Range = 2.2f;
		ability->ConeHalfAngleDegrees = 60.f;
		ability->DamageMultiplier = 0.9f;
		ability->Variance = 0.1f;
		ability->MaxTargets = 1;
		ability->MoveSpeedDuringWindup = 0.4f;
		ability->OnHitStatuses.emplace_back(StatusKind::Burning, 7.f, 5.f, 1.f, 3, 0.6f);
		archetype.Abilities.push_back(ability);
	}

	return archetype;
}

EnemyArchetype BuildVeilwarden()
{
	EnemyArchetype archetype;
	archetype.Id = ArchetypeVeilwarden;
	archetype.DisplayName = "Veilwarden";
	archetype.Description = "It still thinks it is guarding something. It is not wrong.";
	archetype.Level = 6;
	archetype.MaxHealth = 320.f;
	archetype.AttackPower = 30.f;
	archetype.ShadowPower = 24.f;
	archetype.Armor = 40.f;
	archetype.MoveSpeed = 4.2f;
	archetype.CritChance = 0.1f;
	archetype.StatusResistance = 0.3f;
	archetype.Resistances.Set(EDamageType::Shadow, 0.45f);
	archetype.Resistances.Set(EDamageType::Vital, -0.2f);
	archetype.ExperienceReward = 180;
	archetype.LootTableId = "loot-veilwarden";
	archetype.BodyScale = 1.35f;
	archetype.TintRgb[0] = 0.28f;
	archetype.TintRgb[1] = 0.30f;
	archetype.TintRgb[2] = 0.42f;
	archetype.AttackAbilityIndex = 0;
	archetype.Brain = AggressiveBrain(3.f, 0.42f, 18.0f);

	{
		auto ability = NewAbility("veil-sweep", "Veil Sweep");
		ability->Kind = AbilityKind::Cleave;
		ability->DamageType = EDamageType::Shadow;
		ability->UsesShadowPower = true;
		ability->CooldownSeconds = 2.4f;
		ability->WindupSeconds = 0.7f;
		ability->RecoverySeconds = 0.5f;
		ability->Range = 3.6f;
		ability->ConeHalfAngleDegrees = 140.f;
		ability->DamageMultiplier = 1.3f;
		ability->CritChanceBonus = 0.05f;
		ability->Variance = 0.08f;
		ability->MaxTargets = 3;
		ability->MoveSpeedDuringWindup = 0.1f;
		ability->KnockbackSpeed = 6.f;
		archetype.Abilities.push_back(ability);
	}

	return archetype;
}

EnemyArchetype BuildAshenSentinel()
{
	EnemyArchetype archetype;
	archetype.Id = ArchetypeAshenSentinel;
	archetype.DisplayName = "The Ashen Sentinel";
	archetype.Description = "It was left here to hold the gate. Nobody ever relieved it.";
	archetype.Level = 10;
	archetype.MaxHealth = 1500.f;
	archetype.AttackPower = 46.f;
	archetype.ShadowPower = 30.f;
	archetype.Armor = 70.f;
	archetype.MoveSpeed = 3.4f;
	archetype.CritChance = 0.12f;
	archetype.CritMultiplier = 1.8f;
	archetype.StatusResistance = 0.45f;
	archetype.HealthRegen = 4.f;
	archetype.Resistances.Set(EDamageType::Physical, 0.25f);
	archetype.Resistances.Set(EDamageType::Ember, 0.5f);
	archetype.Resistances.Set(EDamageType::Frost, -0.35f);
	archetype.ExperienceReward = 1200;
	archetype.LootTableId = "loot-sentinel";
	archetype.IsBoss = true;
	archetype.BodyScale = 2.2f;
	archetype.TintRgb[0] = 0.34f;
	archetype.TintRgb[1] = 0.26f;
	archetype.TintRgb[2] = 0.24f;
	archetype.AttackAbilityIndex = 0;
	archetype.Brain = AggressiveBrain(3.4f, 0.6f, 22.0f);

	{
		auto ability = NewAbility("sentinel-cleave", "Grave Cleave");
		ability->Kind = AbilityKind::Cleave;
		ability->DamageType = EDamageType::Physical;
		ability->CooldownSeconds = 2.6f;
		ability->WindupSeconds = 0.85f;
		ability->RecoverySeconds = 0.6f;
		ability->Range = 4.2f;
		ability->ConeHalfAngleDegrees = 160.f;
		ability->DamageMultiplier = 1.5f;
		ability->Variance = 0.08f;
		ability->MaxTargets = 4;
		ability->MoveSpeedDuringWindup = 0.f;
		ability->KnockbackSpeed = 10.f;
		ability->SelfStaggerSeconds = 0.4f;
		archetype.Abilities.push_back(ability);
	}

	return archetype;
}

} // namespace

std::vector<EnemyArchetype> BuildEnemyArchetypes()
{
	std::vector<EnemyArchetype> archetypes;
	archetypes.reserve(4);
	archetypes.push_back(BuildHollowWalker());
	archetypes.push_back(BuildCinderHound());
	archetypes.push_back(BuildVeilwarden());
	archetypes.push_back(BuildAshenSentinel());
	return archetypes;
}

const EnemyArchetype* FindArchetype(const std::string& id)
{
	// Built once: archetype templates are shared, read-only content, and the
	// abilities they hold are shared_ptr so every instance can reference them.
	static const std::vector<EnemyArchetype> archetypes = BuildEnemyArchetypes();

	for (const EnemyArchetype& archetype : archetypes)
	{
		if (archetype.Id == id)
		{
			return &archetype;
		}
	}

	return nullptr;
}

// ============================= instance building =============================

std::unique_ptr<Combatant> EnemyArchetype::Create(const std::string& instanceId,
	const Float3& position, int levelOverride) const
{
	const int level = levelOverride > 0 ? levelOverride : Level;

	auto combatant = std::make_unique<Combatant>(
		instanceId.empty() ? std::string("enemy") : instanceId, Faction::Hostile, level);
	combatant->DisplayName = DisplayName;
	combatant->ArchetypeId = Id;
	combatant->ExperienceReward = ExperienceReward;
	combatant->LootTableId = LootTableId;
	combatant->IsBoss = IsBoss;

	ApplyStats(*combatant, level);

	// Values are copied into the combatant's existing profile rather than the
	// profile being replaced, because the health pool shares that instance and
	// a replacement would be ignored by damage resolution.
	combatant->CopyResistancesFrom(Resistances);

	combatant->Vitals().ResetToFull();
	combatant->SetPosition(position);
	combatant->FaceImmediately(Float3(0.f, 0.f, 1.f));

	return combatant;
}

void EnemyArchetype::ApplyStats(Combatant& combatant, int level) const
{
	float levelDelta = static_cast<float>(level - Level);
	float healthScale = 1.f + (0.18f * levelDelta);
	float powerScale = 1.f + (0.12f * levelDelta);

	if (healthScale < 0.2f)
	{
		healthScale = 0.2f;
	}

	if (powerScale < 0.2f)
	{
		powerScale = 0.2f;
	}

	StatSet& stats = combatant.Stats();
	stats.SetBase(StatId::MaxHealth, MaxHealth * healthScale);
	stats.SetBase(StatId::MaxStamina, 100.f);
	stats.SetBase(StatId::AttackPower, AttackPower * powerScale);
	stats.SetBase(StatId::ShadowPower, ShadowPower * powerScale);
	stats.SetBase(StatId::Armor, Armor);
	stats.SetBase(StatId::MoveSpeed, MoveSpeed);
	stats.SetBase(StatId::CritChance, CritChance);
	stats.SetBase(StatId::CritMultiplier, CritMultiplier);
	stats.SetBase(StatId::CooldownRate, 1.f);
	stats.SetBase(StatId::HealthRegen, HealthRegen);
	stats.SetBase(StatId::StaminaRegen, 10.f);
	stats.SetBase(StatId::StatusResistance, StatusResistance);
}

} // namespace ShadowboundCore::Content
