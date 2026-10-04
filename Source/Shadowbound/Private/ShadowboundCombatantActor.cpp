#include "ShadowboundCombatantActor.h"

#include "Components/CapsuleComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Engine/StaticMesh.h"
#include "Materials/MaterialInstanceDynamic.h"
#include "UObject/ConstructorHelpers.h"

#include "ShadowboundConvert.h"

AShadowboundCombatantActor::AShadowboundCombatantActor()
{
	PrimaryActorTick.bCanEverTick = false;

	Body = CreateDefaultSubobject<UCapsuleComponent>(TEXT("Body"));
	Body->InitCapsuleSize(42.f, 88.f);
	// The core owns every position; see the header for why there is no
	// physics body here.
	Body->SetCollisionEnabled(ECollisionEnabled::NoCollision);
	RootComponent = Body;

	Mesh = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("Mesh"));
	Mesh->SetupAttachment(RootComponent);
	Mesh->SetCollisionEnabled(ECollisionEnabled::NoCollision);
	Mesh->SetCastShadow(true);

	// Placeholder art, exactly as the Unity build used primitives: a cylinder
	// from engine content. Real art replaces this mesh; no game rule changes.
	static ConstructorHelpers::FObjectFinder<UStaticMesh> CylinderFinder(
		TEXT("/Engine/BasicShapes/Cylinder.Cylinder"));
	if (CylinderFinder.Object != nullptr)
	{
		Mesh->SetStaticMesh(CylinderFinder.Object);
	}
}

void AShadowboundCombatantActor::BeginPlay()
{
	Super::BeginPlay();

	// Per-object colour via a dynamic instance, so every instance sharing the
	// base material can flash independently (the Unity layer did the same
	// with a MaterialPropertyBlock).
	UMaterialInterface* BaseMaterial = Mesh->GetMaterial(0);
	if (BaseMaterial != nullptr)
	{
		Material = UMaterialInstanceDynamic::Create(BaseMaterial, this);
		Mesh->SetMaterial(0, Material);
		SetTint(BaseTint);
	}
}

void AShadowboundCombatantActor::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	if (BoundCombatant != nullptr)
	{
		BoundCombatant->Damaged.Unbind(DamagedBinding);
		BoundCombatant->Died.Unbind(DiedBinding);
		BoundCombatant = nullptr;
	}

	Super::EndPlay(EndPlayReason);
}

void AShadowboundCombatantActor::BindCombatant(ShadowboundCore::Combatant* InCombatant,
	float InBodyScale, const FLinearColor& InTint)
{
	if (BoundCombatant != nullptr)
	{
		BoundCombatant->Damaged.Unbind(DamagedBinding);
		BoundCombatant->Died.Unbind(DiedBinding);
	}

	BoundCombatant = InCombatant;
	BodyScale = FMath::Max(0.1f, InBodyScale);
	BaseTint = InTint;

	SetActorScale3D(FVector(BodyScale));

	DamagedBinding = BoundCombatant->Damaged.Bind(this, &AShadowboundCombatantActor::HandleDamaged);
	DiedBinding = BoundCombatant->Died.Bind(this, &AShadowboundCombatantActor::HandleDied);

	if (Material != nullptr)
	{
		SetTint(BaseTint);
	}

	SyncFromCore(0.f);
}

void AShadowboundCombatantActor::SyncFromCore(float DeltaSeconds)
{
	if (BoundCombatant == nullptr)
	{
		return;
	}

	// Core position is the FEET point (Y up); the actor origin is the capsule
	// centre, so lift by the capsule half-height. Under the conversion in
	// ShadowboundConvert, core facing degrees are already Unreal yaw.
	const ShadowboundCore::Float3 CorePosition = BoundCombatant->Position();
	const FVector ActorLocation = ShadowboundConvert::ToUnreal(CorePosition)
		+ FVector(0.f, 0.f, 88.f * BodyScale);

	SetActorLocation(ActorLocation);
	SetActorRotation(ShadowboundConvert::FacingToRotator(BoundCombatant->FacingDegrees()));

	const bool bWasPunching = PunchRemaining > 0.f;

	if (DeltaSeconds > 0.f)
	{
		FlashRemaining = FMath::Max(0.f, FlashRemaining - DeltaSeconds);
		PunchRemaining = FMath::Max(0.f, PunchRemaining - DeltaSeconds);
	}

	// A dead combatant stops taking hits, so clear the flash immediately.
	if (!BoundCombatant->IsAlive())
	{
		FlashRemaining = 0.f;
	}

	if (Material != nullptr)
	{
		const float T = FlashDuration <= 0.f
			? 0.f
			: FMath::Clamp(FlashRemaining / FlashDuration, 0.f, 1.f);
		SetTint(FLinearColor::LerpUsingHSV(BaseTint, FLinearColor::White, T));
	}

	// Scale punch: the cheapest way to make a hit feel like it landed. The
	// body scale lives on the ACTOR (it scales the capsule too), so the mesh
	// keeps a relative scale of 1 and only carries the punch.
	const bool bPunching = PunchRemaining > 0.f;
	if (bWasPunching || bPunching)
	{
		const float Punch = 1.f + (0.12f * FMath::Clamp(PunchRemaining / 0.12f, 0.f, 1.f));
		Mesh->SetRelativeScale3D(FVector(Punch));
	}
	else
	{
		Mesh->SetRelativeScale3D(FVector(1.f));
	}
}

void AShadowboundCombatantActor::SetTint(const FLinearColor& Tint)
{
	if (Material != nullptr)
	{
		// BasicShapeMaterial's parameter is "Color"; if a future art material
		// renames it, this becomes a no-op rather than an error - the shape
		// simply keeps its default colour, and the hit punch still shows.
		Material->SetVectorParameterValue(TEXT("Color"), Tint);
	}
}

void AShadowboundCombatantActor::HandleDamaged(void* Context, ShadowboundCore::Combatant& /*Victim*/,
	ShadowboundCore::Combatant* /*Attacker*/, const ShadowboundCore::DamageResult& /*Result*/)
{
	static_cast<AShadowboundCombatantActor*>(Context)->OnDamaged();
}

void AShadowboundCombatantActor::HandleDied(void* Context, ShadowboundCore::Combatant& /*Victim*/)
{
	static_cast<AShadowboundCombatantActor*>(Context)->OnDied();
}

void AShadowboundCombatantActor::OnDamaged()
{
	FlashRemaining = FlashDuration;
	PunchRemaining = 0.12f;
}

void AShadowboundCombatantActor::OnDied()
{
	if (bDeathHandled)
	{
		return;
	}

	bDeathHandled = true;

	// Corpses read as grey rather than their living tint - the Unity layer
	// relied on the death animation it did not have either; this is the
	// placeholder equivalent.
	SetTint(FLinearColor(0.25f, 0.24f, 0.24f));
}
