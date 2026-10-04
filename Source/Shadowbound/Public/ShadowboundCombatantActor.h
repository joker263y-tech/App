#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Pawn.h"
#include "SbCombatant.h"
#include "ShadowboundCombatantActor.generated.h"

// -----------------------------------------------------------------------------
// The Unreal replacement for the Unity CombatantView.
//
// The core simulation owns position and facing; this actor only COPIES them
// onto its transform and reacts to events. It never writes to the combatant,
// so there is exactly one authority over where anything is - the same rule
// the Unity layer followed, for the same reason.
//
// Collision: the capsule exists for camera attachment and debug visibility
// only, and is set to NoCollision. The core owns every position, so a physics
// body would be a second, conflicting authority (the Unity layer deleted its
// primitive colliders for exactly this reason). It also keeps line traces
// honest: the occlusion provider only ever sees arena geometry.
// -----------------------------------------------------------------------------
UCLASS(Abstract)
class SHADOWBOUND_API AShadowboundCombatantActor : public APawn
{
	GENERATED_BODY()

public:
	AShadowboundCombatantActor();

	/// Binds this view to a core combatant and applies placeholder art
	/// (scale + tint), mirroring the Unity CreateView path.
	void BindCombatant(ShadowboundCore::Combatant* InCombatant, float InBodyScale,
		const FLinearColor& InTint);

	ShadowboundCore::Combatant* GetCombatant() const { return BoundCombatant; }

	/// Copies the core position/facing onto the transform and decays hit
	/// feedback. Called once per frame by the game mode, AFTER the simulation
	/// has stepped - the Unity bootstrap did the same in its SyncViews pass.
	virtual void SyncFromCore(float DeltaSeconds);

	virtual void BeginPlay() override;
	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;

protected:
	UPROPERTY(VisibleAnywhere, Category = Combat)
	TObjectPtr<class UCapsuleComponent> Body;

	UPROPERTY(VisibleAnywhere, Category = Combat)
	TObjectPtr<class UStaticMeshComponent> Mesh;

	/// Seconds a hit flash lasts.
	float FlashDuration = 0.12f;

	// Named BoundCombatant, not Combatant: C++ forbids a member sharing its
	// own type name (the C# original had no such restriction).
	ShadowboundCore::Combatant* BoundCombatant = nullptr;
	float BodyScale = 1.f;
	FLinearColor BaseTint = FLinearColor::White;

	float FlashRemaining = 0.f;
	float PunchRemaining = 0.f;
	bool bDeathHandled = false;

	int32 DamagedBinding = 0;
	int32 DiedBinding = 0;

	/// Applies the tint to the material instance (or a grey on death).
	void SetTint(const FLinearColor& Tint);

	class UMaterialInstanceDynamic* Material = nullptr;

private:
	static void HandleDamaged(void* Context, ShadowboundCore::Combatant& Victim,
		ShadowboundCore::Combatant* Attacker, const ShadowboundCore::DamageResult& Result);
	static void HandleDied(void* Context, ShadowboundCore::Combatant& Victim);

	void OnDamaged();
	void OnDied();
};
