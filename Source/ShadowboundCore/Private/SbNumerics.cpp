#include "SbNumerics.h"

#include <cstdio>

namespace ShadowboundCore
{

const Float3 Float3::Zero(0.f, 0.f, 0.f);
const Float3 Float3::One(1.f, 1.f, 1.f);
const Float3 Float3::Up(0.f, 1.f, 0.f);
const Float3 Float3::Down(0.f, -1.f, 0.f);
const Float3 Float3::Right(1.f, 0.f, 0.f);
const Float3 Float3::Forward(0.f, 0.f, 1.f);

bool Float3::Equals(const Float3& other) const noexcept
{
	return CoreMath::Approximately(X, other.X)
		&& CoreMath::Approximately(Y, other.Y)
		&& CoreMath::Approximately(Z, other.Z);
}

std::string Float3::ToString() const
{
	char buffer[96];
	std::snprintf(buffer, sizeof(buffer), "(%.3f, %.3f, %.3f)",
		static_cast<double>(X), static_cast<double>(Y), static_cast<double>(Z));
	return std::string(buffer);
}

Float3 Float3::Lerp(const Float3& a, const Float3& b, float t) noexcept
{
	const float c = CoreMath::Clamp01(t);
	return Float3(
		a.X + ((b.X - a.X) * c),
		a.Y + ((b.Y - a.Y) * c),
		a.Z + ((b.Z - a.Z) * c));
}

Float3 Float3::MoveTowards(const Float3& current, const Float3& target, float maxDelta) noexcept
{
	const Float3 delta = target - current;
	const float distance = delta.Magnitude();
	if (distance <= maxDelta || distance <= CoreMath::Epsilon)
	{
		return target;
	}

	return current + (delta / distance * maxDelta);
}

float Float3::DirectionToDegrees(const Float3& direction) noexcept
{
	// atan2(x, z) because 0 degrees is +Z and the angle grows toward +X.
	const float degrees = std::atan2(direction.X, direction.Z) * CoreMath::Rad2Deg;
	return CoreMath::Repeat(degrees, 360.f);
}

Float3 Float3::DegreesToDirection(float degrees) noexcept
{
	const float radians = degrees * CoreMath::Deg2Rad;
	return Float3(std::sin(radians), 0.f, std::cos(radians));
}

float CoreMath::DeltaAngle(float fromDegrees, float toDegrees) noexcept
{
	float delta = Repeat(toDegrees - fromDegrees, 360.f);
	if (delta > 180.f)
	{
		delta -= 360.f;
	}

	return delta;
}

float CoreMath::Repeat(float value, float length) noexcept
{
	if (length <= Epsilon)
	{
		return 0.f;
	}

	const float result = value - (std::floor(value / length) * length);
	return Clamp(result, 0.f, length);
}

bool CoreMath::WithinCone(const Float3& origin, const Float3& forward, const Float3& target,
	float halfAngleDegrees, float range) noexcept
{
	const Float3 delta = target - origin;
	if (delta.SqrMagnitude() > range * range)
	{
		return false;
	}

	const Float3 toTarget = delta.Normalized();
	const Float3 look = forward.Normalized();
	if (toTarget == Float3::Zero || look == Float3::Zero)
	{
		return false;
	}

	const float cosAngle = Float3::Dot(look, toTarget);
	const float cosThreshold = std::cos(halfAngleDegrees * Deg2Rad);
	return cosAngle >= cosThreshold;
}

} // namespace ShadowboundCore
