#pragma once

#include "CoreMinimal.h"
#include "GameFramework/GameModeBase.h"

#include "ShadowboundOcclusion.h"

#include "ShadowboundGameMode.generated.h"

class AShadowboundArena;
class AShadowboundCombatantActor;
class AShadowboundEnemyCharacter;
class AShadowboundHud;
class AShadowboundPlayerCharacter;
class UShadowboundGameInstance;

namespace ShadowboundCore
{
struct Participant;
struct DamageResult;
class Combatant;
}

// -----------------------------------------------------------------------------
// Assembles the running game, the Unreal replacement for the Unity
// GameBootstrap.
//
// The default map is an empty engine map: this builds the arena, the player,
// the enemies and the HUD in code. Nothing is authored as a binary asset, so
// the committed source of truth stays reviewable in a diff - the same rule the
// Unity bootstrap followed.
//
// The playthrough itself lives on the game instance, not here, because it must
// survive a level load. This class only owns the VIEWS (actors) and drives the
// frame loop: input is turned into intent by the driver during
// EncounterSimulation::Update, then every view copies the core's positions.
// -----------------------------------------------------------------------------
UCLASS()
class SHADOWBOUND_API AShadowboundGameMode : public AGameModeBase
{
	GENERATED_BODY()

public:
	AShadowboundGameMode();

	virtual void InitGame(const FString& MapName, const FString& Options, FString& ErrorMessage) override;
	virtual void StartPlay() override;
	virtual void Tick(float DeltaSeconds) override;
	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;

private:
	/// Builds the playthrough (if needed) and every view in the starting region.
	void Assemble();

	/// Spawns one region's creatures and their views. Ports GameBootstrap's
	/// PopulateRegion verbatim, so the same plan can be reused when region
	/// travel is ported.
	void PopulateRegion(const FString& RegionId);

	void SpawnPlayerView();

	/// Removes a non-boss corpse's body after it has lain on the ground for a
	/// moment, so a kill is visible before it disappears.
	void ExpireDeadViews(float DeltaSeconds);

	UShadowboundGameInstance* GetShadowboundGameInstance() const;

	/// Finds the view bound to a combatant, or null.
	AShadowboundCombatantActor* FindView(ShadowboundCore::Combatant* Combatant) const;

	AShadowboundHud* GetShadowboundHud() const;

	// Core event thunks. The core's TEvent stores a plain function pointer plus
	// a context, so member handlers cannot be bound directly - these are the
	// static trampolines, exactly as in AShadowboundCombatantActor.
	static void HandleDamageThunk(void* Context, ShadowboundCore::Participant* Attacker,
		ShadowboundCore::Combatant* Victim, const ShadowboundCore::DamageResult& Result);
	static void HandleDeathThunk(void* Context, ShadowboundCore::Combatant* Victim,
		ShadowboundCore::Participant* Participant);

	void OnDamage(ShadowboundCore::Combatant* Victim, const ShadowboundCore::DamageResult& Result);
	void OnDeath(ShadowboundCore::Combatant* Victim);

	UPROPERTY()
	TObjectPtr<AShadowboundArena> Arena;

	UPROPERTY()
	TObjectPtr<AShadowboundPlayerCharacter> PlayerView;

	UPROPERTY()
	TArray<TObjectPtr<AShadowboundEnemyCharacter>> EnemyViews;

	/// Corpses waiting to be removed, with their remaining linger time.
	UPROPERTY()
	TArray<TObjectPtr<AShadowboundCombatantActor>> DeadViews;

	TArray<float> DeadTimers;

	/// Line-of-sight provider handed to the core. Owned here: it is only ever
	/// used while a world exists.
	FSbLineOfSight Occlusion;

	int DiedBinding = 0;
	int DamagedBinding = 0;
};
