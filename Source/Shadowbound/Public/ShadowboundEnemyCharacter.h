#pragma once

#include "CoreMinimal.h"
#include "ShadowboundCombatantActor.h"
#include "ShadowboundEnemyCharacter.generated.h"

// -----------------------------------------------------------------------------
// A creature's body: Hollow Walker, Cinder Hound, Veilwarden, Ashen Sentinel.
// Identical machinery for all of them - stats, abilities and behaviour come
// from the archetype through the core, and this class only draws the result.
// The boss is the same class with a bigger body scale (the Unity build used a
// cube for bosses and capsules for everything else; the tint and scale carry
// that distinction here).
// -----------------------------------------------------------------------------
UCLASS()
class SHADOWBOUND_API AShadowboundEnemyCharacter : public AShadowboundCombatantActor
{
	GENERATED_BODY()

public:
	AShadowboundEnemyCharacter();
};
