using Shadowbound.Core.Numerics;
using Xunit;

namespace Shadowbound.Core.Tests.Numerics
{
    public class FMathTests
    {
        [Fact]
        public void Clamp01_OfNaN_ReturnsZero()
        {
            Assert.Equal(0f, FMath.SafeClamp01(float.NaN));
            Assert.Equal(0f, FMath.SafeClamp01(float.PositiveInfinity));
        }

        [Fact]
        public void Clamp_ConstrainsBothEnds()
        {
            Assert.Equal(0f, FMath.Clamp(-5f, 0f, 10f));
            Assert.Equal(10f, FMath.Clamp(50f, 0f, 10f));
            Assert.Equal(5f, FMath.Clamp(5f, 0f, 10f));
        }

        [Fact]
        public void DeltaAngle_TakesTheShortWayAround()
        {
            // 350 -> 10 should be +20, not -340.
            Assert.Equal(20f, FMath.DeltaAngle(350f, 10f), 3);

            // 10 -> 350 should be -20, not +340.
            Assert.Equal(-20f, FMath.DeltaAngle(10f, 350f), 3);
        }

        [Fact]
        public void MoveTowardsZero_NeverCrossesZero()
        {
            Assert.Equal(0f, FMath.MoveTowardsZero(0.1f, 1f));
            Assert.Equal(0f, FMath.MoveTowardsZero(-0.1f, 1f));
            Assert.Equal(0.5f, FMath.MoveTowardsZero(1f, 0.5f), 5);
        }

        [Fact]
        public void WithinCone_TargetBehindIsNotSeen()
        {
            var origin = Float3.Zero;
            var forward = Float3.Forward;
            var behind = new Float3(0f, 0f, -5f);

            Assert.False(FMath.WithinCone(origin, forward, behind, 45f, 10f));
        }

        [Fact]
        public void WithinCone_TargetInFrontWithinRangeIsSeen()
        {
            var origin = Float3.Zero;
            var forward = Float3.Forward;
            var ahead = new Float3(0f, 0f, 5f);

            Assert.True(FMath.WithinCone(origin, forward, ahead, 45f, 10f));
        }

        [Fact]
        public void WithinCone_TargetInFrontButTooFarIsNotSeen()
        {
            var origin = Float3.Zero;
            var forward = Float3.Forward;
            var farAhead = new Float3(0f, 0f, 50f);

            Assert.False(FMath.WithinCone(origin, forward, farAhead, 45f, 10f));
        }
    }
}
