#include "ShadowboundArena.h"

#include "Components/LightComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Engine/DirectionalLight.h"
#include "Engine/ExponentialHeightFog.h"
#include "Engine/StaticMesh.h"
#include "Engine/World.h"
#include "UObject/ConstructorHelpers.h"

namespace
{
/// Engine placeholder art. Real art replaces these meshes; no game rule changes.
UStaticMesh* LoadCube()
{
	static ConstructorHelpers::FObjectFinder<UStaticMesh> Finder(TEXT("/Engine/BasicShapes/Cube.Cube"));
	return Finder.Object;
}

UStaticMesh* LoadPlane()
{
	static ConstructorHelpers::FObjectFinder<UStaticMesh> Finder(TEXT("/Engine/BasicShapes/Plane.Plane"));
	return Finder.Object;
}

/// Core-units (metres) to Unreal units (cm).
constexpr float M(float Metres)
{
	return Metres * 100.f;
}
} // namespace

AShadowboundArena::AShadowboundArena()
{
	PrimaryActorTick.bCanEverTick = false;

	Root = CreateDefaultSubobject<USceneComponent>(TEXT("Root"));
	RootComponent = Root;

	UStaticMesh* Cube = LoadCube();
	UStaticMesh* Plane = LoadPlane();

	// Floor: 76m x 76m, top surface at Z = 0, so the core's Y=0 ground plane
	// and the visual ground coincide.
	Floor = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("Floor"));
	Floor->SetupAttachment(Root);
	Floor->SetStaticMesh(Plane);
	Floor->SetRelativeLocation(FVector(0.f, 0.f, 0.f));
	Floor->SetRelativeScale3D(FVector(76.f, 76.f, 1.f));

	// Walls: 4m tall, 1m thick, spanning the full 76m - the player cannot
	// walk out (the core's WorldBounds clamps too; both agree by construction).
	//
	// Positions map core (x, y, z) -> Unreal (z, x, y), see ShadowboundConvert.
	WallNorth = MakeBlock(TEXT("WallNorth"), Root,
		FVector(M(38.f), 0.f, M(2.f)), FVector(M(1.f), M(76.f), M(4.f)), Cube);
	WallSouth = MakeBlock(TEXT("WallSouth"), Root,
		FVector(M(-38.f), 0.f, M(2.f)), FVector(M(1.f), M(76.f), M(4.f)), Cube);
	WallEast = MakeBlock(TEXT("WallEast"), Root,
		FVector(0.f, M(38.f), M(2.f)), FVector(M(76.f), M(1.f), M(4.f)), Cube);
	WallWest = MakeBlock(TEXT("WallWest"), Root,
		FVector(0.f, M(-38.f), M(2.f)), FVector(M(76.f), M(1.f), M(4.f)), Cube);

	// Scattered pillars give the enemies something to break line of sight
	// against, which is what makes their vision cone matter. Core positions:
	// (6, 1.5, 4), (-7, 1.5, 2), (3, 1.5, -12), (-4, 1.5, -14).
	Pillar1 = MakeBlock(TEXT("Pillar1"), Root,
		FVector(M(4.f), M(6.f), M(1.5f)), FVector(M(1.6f), M(1.6f), M(3.f)), Cube);
	Pillar2 = MakeBlock(TEXT("Pillar2"), Root,
		FVector(M(2.f), M(-7.f), M(1.5f)), FVector(M(1.6f), M(1.6f), M(3.f)), Cube);
	Pillar3 = MakeBlock(TEXT("Pillar3"), Root,
		FVector(M(-12.f), M(3.f), M(1.5f)), FVector(M(2.2f), M(2.2f), M(3.f)), Cube);
	Pillar4 = MakeBlock(TEXT("Pillar4"), Root,
		FVector(M(-14.f), M(-4.f), M(1.5f)), FVector(M(2.2f), M(2.2f), M(3.f)), Cube);

	// The gate marker at the far end. In the Unity build it teleported the
	// player to the next region; region travel is phase 2 of this migration
	// (the world graph has not been ported yet), so for now it is scenery.
	Gate = MakeBlock(TEXT("Gate"), Root,
		FVector(M(32.f), 0.f, M(1.6f)), FVector(M(0.5f), M(7.f), M(3.2f)), Cube);

	// Warm gate colour, matching the Unity placeholder tint.
	if (Gate != nullptr)
	{
		Gate->SetCastShadow(false);
	}
}

UStaticMeshComponent* AShadowboundArena::MakeBlock(const TCHAR* Name, USceneComponent* Parent,
	const FVector& Location, const FVector& Size, UStaticMesh* Mesh)
{
	UStaticMeshComponent* Block = CreateDefaultSubobject<UStaticMeshComponent>(Name);
	Block->SetupAttachment(Parent);
	Block->SetStaticMesh(Mesh);
	Block->SetRelativeLocation(Location);

	// Engine cubes are 100 units (1m) per side, so scale == size in metres.
	Block->SetRelativeScale3D(FVector(
		Size.X / 100.f,
		Size.Y / 100.f,
		Size.Z / 100.f));

	return Block;
}

void AShadowboundArena::BeginPlay()
{
	Super::BeginPlay();

	UWorld* World = GetWorld();
	if (World == nullptr)
	{
		return;
	}

	FActorSpawnParameters Params;
	Params.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;

	// Key light: the Unity build created a directional light at (48, 145, 0)
	// with a cool-white colour, and this is its replacement. Spawned rather
	// than authored so the committed source of truth stays code.
	ADirectionalLight* Sun = World->SpawnActor<ADirectionalLight>(
		GetActorLocation(), FRotator(-48.f, 145.f, 0.f), Params);
	if (Sun != nullptr && Sun->GetLightComponent() != nullptr)
	{
		Sun->GetLightComponent()->SetIntensity(5.f);
		Sun->GetLightComponent()->SetLightColor(FLinearColor(0.86f, 0.88f, 1.f));
	}

	// The game is dark and banding in shadows is the first visible artefact -
	// a thin height fog keeps the far wall from reading as a hard edge.
	AExponentialHeightFog* Fog = World->SpawnActor<AExponentialHeightFog>(
		FVector::ZeroVector, FRotator::ZeroRotator, Params);
	if (Fog != nullptr && Fog->GetComponent() != nullptr)
	{
		Fog->GetComponent()->FogDensity = 0.012f;
	}
}
