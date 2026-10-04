#pragma once

#include "CoreMinimal.h"

#include "SbNumerics.h"

// -----------------------------------------------------------------------------
// The boundary between the engine-free core and Unreal.
//
// The core simulates in its own coordinates: Y up, facing 0 = +Z growing
// toward +X, and world units where the Warden is about 1.8 units tall and
// runs at 5.5 units/second (i.e. one core unit is one metre - the same scale
// the Unity build used).
//
// Unreal is Z up, X forward, yaw 0 = +X, and one unit is one centimetre.
// The mapping below is a proper rotation (determinant +1) times the unit
// scale, with two useful consequences:
//
//   * Core facing degrees == Unreal yaw. No conversion is needed for rotation.
//   * Float3(vector.Y, vector.Z, vector.X) / Scale round-trips exactly.
//
// Nothing outside this header converts coordinates. The engine layer copies
// positions FROM the core and never writes them back - the same one-authority
// rule the Unity layer followed.
// -----------------------------------------------------------------------------
namespace ShadowboundConvert
{

/// Unreal units (cm) per core unit (m).
constexpr float Scale = 100.f;

FORCEINLINE FVector ToUnreal(const ShadowboundCore::Float3& Value)
{
	return FVector(Value.Z * Scale, Value.X * Scale, Value.Y * Scale);
}

FORCEINLINE ShadowboundCore::Float3 ToCore(const FVector& Value)
{
	return ShadowboundCore::Float3(
		static_cast<float>(Value.Y) / Scale,
		static_cast<float>(Value.Z) / Scale,
		static_cast<float>(Value.X) / Scale);
}

/// Core facing degrees map 1:1 onto Unreal yaw under the transform above.
FORCEINLINE FRotator FacingToRotator(float FacingDegrees)
{
	return FRotator(0.f, FacingDegrees, 0.f);
}

FORCEINLINE float RotatorToFacing(const FRotator& Rotation)
{
	return static_cast<float>(Rotation.Yaw);
}

/// Convenience: a speed expressed in core units per second, as Unreal units
/// per second. (The simulation itself never needs this - it runs entirely in
/// core units - but presentation-side code occasionally does.)
FORCEINLINE float SpeedToUnreal(float CoreUnitsPerSecond)
{
	return CoreUnitsPerSecond * Scale;
}

} // namespace ShadowboundConvert
