#include "SbTest.h"

#include "SbNumerics.h"

using ShadowboundCore::CoreMath;
using ShadowboundCore::Float3;

TEST(Numerics_ClampVariants)
{
	CHECK_NEAR(CoreMath::Clamp(5.f, 0.f, 10.f), 5.f, 1e-6f);
	CHECK_NEAR(CoreMath::Clamp(-5.f, 0.f, 10.f), 0.f, 1e-6f);
	CHECK_NEAR(CoreMath::Clamp(15.f, 0.f, 10.f), 10.f, 1e-6f);
	CHECK_NEAR(CoreMath::Clamp01(2.f), 1.f, 1e-6f);
	CHECK_NEAR(CoreMath::SafeClamp01(0.5f), 0.5f, 1e-6f);
	CHECK_NEAR(CoreMath::SafeClamp01(NAN), 0.f, 1e-6f);
	CHECK_NEAR(CoreMath::SafeClamp01(INFINITY), 0.f, 1e-6f);
}

TEST(Numerics_DeltaAngleTakesTheShortWay)
{
	CHECK_NEAR(CoreMath::DeltaAngle(350.f, 10.f), 20.f, 1e-4f);
	CHECK_NEAR(CoreMath::DeltaAngle(10.f, 350.f), -20.f, 1e-4f);
	CHECK_NEAR(CoreMath::DeltaAngle(0.f, 90.f), 90.f, 1e-4f);
	CHECK_NEAR(CoreMath::DeltaAngle(0.f, 180.f), 180.f, 1e-4f);
}

TEST(Numerics_RepeatWrapsAndClamps)
{
	CHECK_NEAR(CoreMath::Repeat(400.f, 360.f), 40.f, 1e-4f);
	CHECK_NEAR(CoreMath::Repeat(-10.f, 360.f), 350.f, 1e-4f);
	CHECK_NEAR(CoreMath::Repeat(720.f, 360.f), 0.f, 1e-4f);
}

TEST(Numerics_MoveTowardsNeverOvershoots)
{
	CHECK_NEAR(CoreMath::MoveTowards(0.f, 10.f, 3.f), 3.f, 1e-6f);
	CHECK_NEAR(CoreMath::MoveTowards(0.f, 10.f, 20.f), 10.f, 1e-6f);
	CHECK_NEAR(CoreMath::MoveTowards(0.f, -10.f, 3.f), -3.f, 1e-6f);
	CHECK_NEAR(CoreMath::MoveTowardsZero(-2.f, 5.f), 0.f, 1e-6f);
}

TEST(Numerics_InverseLerpAndApproximately)
{
	CHECK_NEAR(CoreMath::InverseLerp(0.f, 10.f, 5.f), 0.5f, 1e-6f);
	CHECK_NEAR(CoreMath::InverseLerp(0.f, 10.f, 15.f), 1.f, 1e-6f);
	CHECK(CoreMath::Approximately(1.f, 1.f + 1e-7f));
	CHECK(!CoreMath::Approximately(1.f, 1.1f));
}

TEST(Numerics_Float3NormaliseIsSafe)
{
	const Float3 zero = Float3::Zero.Normalized();
	CHECK(zero == Float3::Zero);
	CHECK(!std::isnan(zero.X));

	// A purely vertical vector has no horizontal projection.
	CHECK((Float3(0.f, 5.f, 0.f).FlattenedXZ() == Float3::Zero));

	const Float3 unit = Float3(3.f, 0.f, 4.f).Normalized();
	CHECK_NEAR(unit.Magnitude(), 1.f, 1e-5f);
}

TEST(Numerics_Float3EqualityIsApproximate)
{
	CHECK(Float3(1.f, 2.f, 3.f) == Float3(1.f + 1e-7f, 2.f, 3.f));
	CHECK(Float3(1.f, 2.f, 3.f) != Float3(1.f, 2.f, 3.5f));
}

TEST(Numerics_DistanceXZIgnoresHeight)
{
	const Float3 a(0.f, 0.f, 0.f);
	const Float3 b(3.f, 100.f, 4.f);
	CHECK_NEAR(Float3::DistanceXZ(a, b), 5.f, 1e-5f);
	CHECK_NEAR(Float3::SqrDistanceXZ(a, b), 25.f, 1e-5f);
	CHECK(Float3::Distance(a, b) > 100.f);
}

TEST(Numerics_FacingAnglesMatchTheCSharpConvention)
{
	// 0 degrees faces +Z; 90 degrees faces +X.
	CHECK(Float3::DegreesToDirection(0.f) == Float3(0.f, 0.f, 1.f));
	CHECK(Float3::DegreesToDirection(90.f) == Float3(1.f, 0.f, 0.f));
	CHECK_NEAR(Float3::DirectionToDegrees(Float3(0.f, 0.f, 1.f)), 0.f, 1e-3f);
	CHECK_NEAR(Float3::DirectionToDegrees(Float3(1.f, 0.f, 0.f)), 90.f, 1e-3f);
	CHECK_NEAR(Float3::DirectionToDegrees(Float3(-1.f, 0.f, 0.f)), 270.f, 1e-3f);
}

TEST(Numerics_WithinConeNeedsRangeAndFacing)
{
	const Float3 origin = Float3::Zero;
	const Float3 forward = Float3(0.f, 0.f, 1.f);

	// Straight ahead, in range.
	CHECK(CoreMath::WithinCone(origin, forward, Float3(0.f, 0.f, 5.f), 45.f, 10.f));
	// Behind the origin.
	CHECK(!CoreMath::WithinCone(origin, forward, Float3(0.f, 0.f, -5.f), 45.f, 10.f));
	// Inside the cone but beyond range.
	CHECK(!CoreMath::WithinCone(origin, forward, Float3(0.f, 0.f, 50.f), 45.f, 10.f));
	// Wide cone accepts a flanker.
	CHECK(CoreMath::WithinCone(origin, forward, Float3(4.f, 0.f, 4.f), 45.f, 10.f));
}

TEST(Numerics_RotateYTurnsAroundTheUpAxis)
{
	const Float3 forward = Float3(0.f, 0.f, 1.f).RotateY(90.f);
	CHECK(forward == Float3(1.f, 0.f, 0.f));
}
