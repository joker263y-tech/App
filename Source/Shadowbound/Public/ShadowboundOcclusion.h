#pragma once

#include "CoreMinimal.h"

#include "SbEncounter.h"

// -----------------------------------------------------------------------------
// Answers line-of-sight queries with a real Unreal line trace.
//
// The core deliberately has no geometry (it is engine-free), so the engine
// layer supplies it - the same split the Unity layer had, where the physics
// raycast lived outside the core. This is what makes the enemies' vision cone
// meaningful: the arena's pillars block it.
//
// Combatant capsules use NoCollision (the core owns every position, so a
// physics body would be a second, conflicting authority), so a trace between
// two combatants only ever hits arena geometry.
// -----------------------------------------------------------------------------
class SHADOWBOUND_API FSbLineOfSight : public ShadowboundCore::IOcclusionProvider
{
public:
	/// Height above the ground plane at which sight is measured, so the
	/// trace does not skim the floor.
	float EyeHeight = 150.f;

	virtual bool HasLineOfSight(const ShadowboundCore::Float3& From,
		const ShadowboundCore::Float3& To) const override;

	UWorld* World = nullptr;
};
