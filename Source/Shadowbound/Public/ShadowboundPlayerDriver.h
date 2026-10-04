#pragma once

#include "CoreMinimal.h"

#include "ShadowboundConvert.h"
#include "SbEncounter.h"

// -----------------------------------------------------------------------------
// Camera-relative movement input, ported from the Unity PlayerInputDriver.
//
// This is the only place that knows a human is playing. The core simulation
// cannot tell this apart from the enemy brain, which is why the player and the
// enemies move through exactly the same code path.
//
// Movement is camera-relative: pushing forward moves the Warden away from the
// camera, not along a fixed world axis. Facing is left to the core, which
// turns the combatant toward its movement direction.
// -----------------------------------------------------------------------------
class SHADOWBOUND_API FSbPlayerDriver : public ShadowboundCore::ICombatantDriver
{
public:
	explicit FSbPlayerDriver(TWeakObjectPtr<class AShadowboundPlayerController> InOwner)
		: Owner(InOwner)
	{
	}

	/// The controller does not exist when the playthrough is created, so it is
	/// attached later, when the game mode possesses the player. A weak pointer
	/// because the controller dies with the level while this driver may outlive it.
	void SetOwner(TWeakObjectPtr<class AShadowboundPlayerController> InOwner)
	{
		Owner = InOwner;
	}

	virtual ShadowboundCore::CombatIntent Decide(float DeltaTime,
		ShadowboundCore::Combatant& Self,
		ShadowboundCore::EncounterSimulation& World) override;

private:
	TWeakObjectPtr<class AShadowboundPlayerController> Owner;
};
