#pragma once

#include "CoreMinimal.h"
#include "Engine/GameInstance.h"

#include <memory>
#include <vector>

#include "SbAbilities.h"
#include "SbCombatant.h"
#include "SbEncounter.h"

#include "ShadowboundPlayerDriver.h"

#include "ShadowboundGameInstance.generated.h"

// -----------------------------------------------------------------------------
// Persistent playthrough state - the Unreal replacement for the parts of
// GameBootstrap/GameSession that survived region travel in the Unity build.
//
// The engine-free core objects live here rather than in the game mode,
// because the game mode dies with the level while the playthrough must not.
// The Unity build kept the same boundary: the session (player, RNG, encounter)
// outlived any single scene object.
//
// DECLARATION ORDER MATTERS. Members are destroyed in reverse order, so the
// encounter - declared last - is destroyed FIRST, while every combatant it
// still references is alive. Its destructor unbinds the event handlers it
// placed on those combatants; reversing this order would call into freed
// memory the next time the player took damage.
// -----------------------------------------------------------------------------
UCLASS()
class SHADOWBOUND_API UShadowboundGameInstance : public UGameInstance
{
	GENERATED_BODY()

public:
	virtual void Init() override;

	/// Seed for the session's single deterministic RNG stream. Matches the
	/// Unity GameBootstrap default so the same seed replays the same fight.
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = Shadowbound)
	int32 Seed = 20250925;

	/// Arena half-extent in core units (metres) - WorldBounds::Square(...).
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = Shadowbound)
	float ArenaHalfExtent = 38.f;

	/// Region the game boots into. The Unity build opened in the safe hub
	/// (region travel is phase 2 of the migration), so phase 1 boots into the
	/// first combat region to make Player -> Enemy -> Combat reachable.
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = Shadowbound)
	FString StartingRegion = TEXT("grey-wilds");

	// ---- playthrough state (engine-free core) --------------------------------

	std::unique_ptr<ShadowboundCore::Combatant> PlayerCombatant;
	std::vector<std::unique_ptr<ShadowboundCore::Combatant>> EnemyCombatants;
	std::vector<std::shared_ptr<ShadowboundCore::AbilityDefinition>> PlayerAbilities;

	/// The player's input driver. Owned here, not by the game mode, because the
	/// encounter holds a non-owning pointer to it and the encounter outlives any
	/// single level. Declared before Encounter so it is destroyed after it.
	std::unique_ptr<FSbPlayerDriver> PlayerDriver;

	std::unique_ptr<ShadowboundCore::EncounterSimulation> Encounter;

	/// Creates the playthrough once. Safe to call every level load.
	void EnsurePlaythrough();

	bool IsPlaythroughReady() const
	{
		return PlayerCombatant != nullptr && Encounter != nullptr;
	}
};
