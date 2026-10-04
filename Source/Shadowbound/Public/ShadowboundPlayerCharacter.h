#pragma once

#include "CoreMinimal.h"
#include "ShadowboundCombatantActor.h"
#include "ShadowboundPlayerCharacter.generated.h"

class UShadowboundFollowCamera;

// -----------------------------------------------------------------------------
// The Warden: the player's body and camera rig.
//
// It is a view, not a simulation: position and facing are copied from the
// core combatant every frame (see AShadowboundCombatantActor). Input arrives
// through the player controller, is turned into CombatIntent by the driver,
// and the core decides what actually happens - the player and the enemies
// move through identical code.
// -----------------------------------------------------------------------------
UCLASS()
class SHADOWBOUND_API AShadowboundPlayerCharacter : public AShadowboundCombatantActor
{
	GENERATED_BODY()

public:
	AShadowboundPlayerCharacter();

	UShadowboundFollowCamera* GetCameraRig() const { return CameraRig; }

	/// Placeholder art: the Warden's colour from the Unity bootstrap, scale 1.
	virtual void BeginPlay() override;

private:
	UPROPERTY(VisibleAnywhere, Category = Camera)
	TObjectPtr<UShadowboundFollowCamera> CameraRig;
};
