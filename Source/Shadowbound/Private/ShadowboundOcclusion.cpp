#include "ShadowboundOcclusion.h"

#include "Engine/World.h"
#include "ShadowboundConvert.h"

bool FSbLineOfSight::HasLineOfSight(const ShadowboundCore::Float3& From,
	const ShadowboundCore::Float3& To) const
{
	if (World == nullptr)
	{
		return true;
	}

	const FVector Start = ShadowboundConvert::ToUnreal(From) + FVector(0.f, 0.f, EyeHeight);
	const FVector End = ShadowboundConvert::ToUnreal(To) + FVector(0.f, 0.f, EyeHeight);

	FCollisionQueryParams Params(TEXT("ShadowboundLOS"), /*bTraceComplex*/ true);

	FHitResult Hit;
	const bool bBlocked = World->LineTraceSingleByChannel(Hit, Start, End, ECC_Visibility, Params);

	return !bBlocked;
}
