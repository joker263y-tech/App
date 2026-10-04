#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "ShadowboundArena.generated.h"

// -----------------------------------------------------------------------------
// The arena: floor, walls, line-of-sight pillars and the gate marker.
//
// The Unity build assembled the whole game at runtime from primitives so no
// binary scene or prefab could drift out of step with the code. This is the
// same decision in Unreal: no .umap is authored, the game mode spawns this
// actor, and its geometry comes from engine content (BasicShapes). Real art
// replaces the meshes; no game rule changes.
//
// The pillars are gameplay, not decoration: they break line of sight, which is
// what makes the enemies' vision cone and the player's cover meaningful.
// -----------------------------------------------------------------------------
UCLASS()
class SHADOWBOUND_API AShadowboundArena : public AActor
{
	GENERATED_BODY()

public:
	AShadowboundArena();

	/// Half-extent of the playable square, matching the core's
	/// WorldBounds::Square(halfExtent) used by the session.
	static constexpr float HalfExtent = 3800.f; // 38 world units, x100 scale

private:
	UPROPERTY(VisibleAnywhere, Category = Arena)
	TObjectPtr<USceneComponent> Root;

	UPROPERTY(VisibleAnywhere, Category = Arena)
	TObjectPtr<class UStaticMeshComponent> Floor;

	UPROPERTY(VisibleAnywhere, Category = Arena)
	TObjectPtr<class UStaticMeshComponent> WallNorth;

	UPROPERTY(VisibleAnywhere, Category = Arena)
	TObjectPtr<class UStaticMeshComponent> WallSouth;

	UPROPERTY(VisibleAnywhere, Category = Arena)
	TObjectPtr<class UStaticMeshComponent> WallEast;

	UPROPERTY(VisibleAnywhere, Category = Arena)
	TObjectPtr<class UStaticMeshComponent> WallWest;

	UPROPERTY(VisibleAnywhere, Category = Arena)
	TObjectPtr<class UStaticMeshComponent> Pillar1;

	UPROPERTY(VisibleAnywhere, Category = Arena)
	TObjectPtr<class UStaticMeshComponent> Pillar2;

	UPROPERTY(VisibleAnywhere, Category = Arena)
	TObjectPtr<class UStaticMeshComponent> Pillar3;

	UPROPERTY(VisibleAnywhere, Category = Arena)
	TObjectPtr<class UStaticMeshComponent> Pillar4;

	UPROPERTY(VisibleAnywhere, Category = Arena)
	TObjectPtr<class UStaticMeshComponent> Gate;

	/// Key light and height fog are spawned actors; created on BeginPlay so
	/// the arena works the same when placed in an editor map later.
	virtual void BeginPlay() override;

	UStaticMeshComponent* MakeBlock(const TCHAR* Name, USceneComponent* Parent,
		const FVector& Location, const FVector& Size, UStaticMesh* Mesh);
};
