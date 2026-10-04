#pragma once

#include <cmath>
#include <cstdint>
#include <string>

namespace ShadowboundCore
{

// -----------------------------------------------------------------------------
// Float3
//
// Engine-independent vector type used by all core simulation code. The core
// deliberately does not know about FVector so that combat, AI and world logic
// can run and be tested without an engine (ported from the C# Float3, which
// likewise did not know about UnityEngine.Vector3).
//
// Coordinate conventions (documented once, relied on everywhere):
//   * Y is up. X/Z is the ground plane.
//   * 0 degrees of facing is +Z, and the angle grows toward +X.
// The Unreal layer converts at the boundary:
//   FVector(Float3.Z, Float3.X, Float3.Y)  /  Float3(FVector.Y, FVector.Z, FVector.X)
// which maps Y-up onto UE's Z-up and keeps UE yaw == core facing degrees.
// -----------------------------------------------------------------------------
struct Float3
{
	float X;
	float Y;
	float Z;

	constexpr Float3() noexcept : X(0.f), Y(0.f), Z(0.f) {}
	constexpr Float3(float x, float y, float z) noexcept : X(x), Y(y), Z(z) {}

	static const Float3 Zero;
	static const Float3 One;
	static const Float3 Up;
	static const Float3 Down;
	static const Float3 Right;
	static const Float3 Forward;

	/// Squared length. Prefer this over Magnitude() for comparisons.
	float SqrMagnitude() const noexcept { return (X * X) + (Y * Y) + (Z * Z); }

	float Magnitude() const noexcept
	{
		return std::sqrt((X * X) + (Y * Y) + (Z * Z));
	}

	/// Unit-length copy, or Zero when too short to normalise. Never NaN.
	Float3 Normalized() const noexcept
	{
		const float sqr = SqrMagnitude();
		if (sqr <= 1e-10f)
		{
			return Zero;
		}

		const float inv = 1.f / std::sqrt(sqr);
		return Float3(X * inv, Y * inv, Z * inv);
	}

	/// Projection onto the horizontal plane, normalised. Used for movement and facing.
	Float3 FlattenedXZ() const noexcept
	{
		return Float3(X, 0.f, Z).Normalized();
	}

	/// Rotation around the Y axis by degrees. Converts facing into a movement
	/// direction for strafing enemies.
	Float3 RotateY(float degrees) const noexcept
	{
		const float radians = degrees * 0.0174532924f;
		const float cos = std::cos(radians);
		const float sin = std::sin(radians);
		return Float3((X * cos) + (Z * sin), Y, (-X * sin) + (Z * cos));
	}

	bool Equals(const Float3& other) const noexcept;

	std::string ToString() const;

	Float3 operator+(const Float3& o) const noexcept { return Float3(X + o.X, Y + o.Y, Z + o.Z); }
	Float3 operator-(const Float3& o) const noexcept { return Float3(X - o.X, Y - o.Y, Z - o.Z); }
	Float3 operator-() const noexcept { return Float3(-X, -Y, -Z); }
	Float3 operator*(float s) const noexcept { return Float3(X * s, Y * s, Z * s); }
	Float3 operator/(float s) const noexcept
	{
		return s == 0.f ? Zero : Float3(X / s, Y / s, Z / s);
	}

	Float3& operator+=(const Float3& o) noexcept { X += o.X; Y += o.Y; Z += o.Z; return *this; }

	static float Dot(const Float3& a, const Float3& b) noexcept
	{
		return (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
	}

	static Float3 Cross(const Float3& a, const Float3& b) noexcept
	{
		return Float3(
			(a.Y * b.Z) - (a.Z * b.Y),
			(a.Z * b.X) - (a.X * b.Z),
			(a.X * b.Y) - (a.Y * b.X));
	}

	static float Distance(const Float3& a, const Float3& b) noexcept { return (a - b).Magnitude(); }

	/// Horizontal distance, ignoring height. The right metric for most aggro ranges.
	static float DistanceXZ(const Float3& a, const Float3& b) noexcept
	{
		const float dx = a.X - b.X;
		const float dz = a.Z - b.Z;
		return std::sqrt((dx * dx) + (dz * dz));
	}

	static float SqrDistanceXZ(const Float3& a, const Float3& b) noexcept
	{
		const float dx = a.X - b.X;
		const float dz = a.Z - b.Z;
		return (dx * dx) + (dz * dz);
	}

	static Float3 Lerp(const Float3& a, const Float3& b, float t) noexcept;

	/// Steps from current toward target by at most maxDelta units. Steering and
	/// camera follow use this.
	static Float3 MoveTowards(const Float3& current, const Float3& target, float maxDelta) noexcept;

	/// Converts a direction on the XZ plane to an angle in degrees, 0 faces +Z.
	static float DirectionToDegrees(const Float3& direction) noexcept;

	/// Converts an angle in degrees to a unit direction on the XZ plane.
	static Float3 DegreesToDirection(float degrees) noexcept;
};

/// Approximate equality, matching the C# Float3.Operators (which compare with
/// an epsilon) - this matters because the simulation tests "direction != Zero".
inline bool operator==(const Float3& a, const Float3& b) noexcept { return a.Equals(b); }
inline bool operator!=(const Float3& a, const Float3& b) noexcept { return !a.Equals(b); }
inline Float3 operator*(float s, const Float3& v) noexcept { return v * s; }

// -----------------------------------------------------------------------------
// CoreMath
//
// Engine-independent math helpers, deliberately separate from std so the core
// has one consistent, auditable source of clamping and comparison behaviour.
// Ported from the C# FMath (named CoreMath so it can never be confused with
// Unreal's own global FMath when both are visible in one translation unit).
// -----------------------------------------------------------------------------
class CoreMath
{
public:
	static constexpr float Epsilon = 1e-5f;
	static constexpr float EpsilonSqr = 1e-10f;
	static constexpr float Deg2Rad = 0.0174532924f;
	static constexpr float Rad2Deg = 57.29578f;
	static constexpr float TwoPi = 6.2831855f;
	static constexpr float Pi = 3.1415927f;

	static float Clamp(float value, float min, float max) noexcept
	{
		if (value < min) { return min; }
		return value > max ? max : value;
	}

	static int ClampInt(int value, int min, int max) noexcept
	{
		if (value < min) { return min; }
		return value > max ? max : value;
	}

	static float Clamp01(float value) noexcept { return Clamp(value, 0.f, 1.f); }

	/// Clamps to 0..1 and back to 0 when the value is not a real number.
	static float SafeClamp01(float value) noexcept
	{
		if (std::isnan(value) || std::isinf(value)) { return 0.f; }
		return Clamp01(value);
	}

	static float Lerp(float a, float b, float t) noexcept { return a + ((b - a) * Clamp01(t)); }

	/// Unclamped lerp, for curves where overshoot is meaningful.
	static float LerpUnclamped(float a, float b, float t) noexcept { return a + ((b - a) * t); }

	static float InverseLerp(float a, float b, float value) noexcept
	{
		if (std::fabs(b - a) <= Epsilon) { return 0.f; }
		return Clamp01((value - a) / (b - a));
	}

	static float MoveTowards(float current, float target, float maxDelta) noexcept
	{
		const float delta = target - current;
		if (std::fabs(delta) <= maxDelta) { return target; }
		return current + ((delta < 0.f ? -1.f : 1.f) * maxDelta);
	}

	/// Steps toward zero, never overshooting. Drains knockback impulses.
	static float MoveTowardsZero(float current, float maxDelta) noexcept
	{
		return MoveTowards(current, 0.f, maxDelta);
	}

	static bool Approximately(float a, float b) noexcept { return std::fabs(b - a) <= Epsilon; }
	static bool Approximately(float a, float b, float tolerance) noexcept { return std::fabs(b - a) <= tolerance; }

	/// Smallest signed difference between two angles in degrees, -180..180.
	/// Enemy facing and camera yaw both need this to turn the short way round.
	static float DeltaAngle(float fromDegrees, float toDegrees) noexcept;

	static float Repeat(float value, float length) noexcept;

	static float Sqrt(float value) noexcept { return value <= 0.f ? 0.f : std::sqrt(value); }

	/// Distance over which a value falls from 1 to 0, with a floor.
	static float Falloff(float distance, float radius, float minimum) noexcept
	{
		if (radius <= Epsilon) { return minimum; }
		const float t = Clamp01(1.f - (distance / radius));
		return minimum + ((1.f - minimum) * t);
	}

	/// True when target lies inside the cone from origin along forward,
	/// within range. Used for perceiving a target inside a vision cone and for
	/// ability cone targeting.
	static bool WithinCone(const Float3& origin, const Float3& forward, const Float3& target,
		float halfAngleDegrees, float range) noexcept;
};

} // namespace ShadowboundCore
