using Shadowbound.Core.Numerics;
using Xunit;

namespace Shadowbound.Core.Tests.Numerics
{
    public class Float3Tests
    {
        [Fact]
        public void Normalized_OfZeroVector_ReturnsZeroInsteadOfNaN()
        {
            Float3 result = Float3.Zero.Normalized;

            Assert.Equal(Float3.Zero, result);
            Assert.False(float.IsNaN(result.X));
        }

        [Fact]
        public void Normalized_ProducesUnitLength()
        {
            var v = new Float3(3f, 4f, 12f);

            Float3 n = v.Normalized;

            Assert.Equal(1f, n.Magnitude, 4);
        }

        [Fact]
        public void Magnitude_OfThreeFourFiveTriangle_IsFive()
        {
            var v = new Float3(3f, 4f, 0f);

            Assert.Equal(5f, v.Magnitude, 5);
        }

        [Fact]
        public void DistanceXZ_IgnoresVerticalOffset()
        {
            var a = new Float3(0f, 100f, 0f);
            var b = new Float3(3f, -50f, 4f);

            Assert.Equal(5f, Float3.DistanceXZ(a, b), 5);
        }

        [Fact]
        public void OperatorSubtraction_IsComponentWise()
        {
            var a = new Float3(5f, 6f, 7f);
            var b = new Float3(1f, 2f, 3f);

            Float3 result = a - b;

            Assert.Equal(new Float3(4f, 4f, 4f), result);
        }

        [Fact]
        public void Division_ByZero_ReturnsZeroRatherThanInfinity()
        {
            var v = new Float3(1f, 2f, 3f);

            Float3 result = v / 0f;

            Assert.Equal(Float3.Zero, result);
        }

        [Fact]
        public void Lerp_ClampsOvershootToOne()
        {
            var a = Float3.Zero;
            var b = Float3.One;

            Assert.Equal(Float3.One, Float3.Lerp(a, b, 5f));
        }

        [Fact]
        public void MoveTowards_DoesNotOvershootTarget()
        {
            var current = Float3.Zero;
            var target = new Float3(1f, 0f, 0f);

            Float3 result = Float3.MoveTowards(current, target, 100f);

            Assert.Equal(target, result);
        }

        [Fact]
        public void RotateY_ByNinetyDegrees_TurnsForwardIntoRight()
        {
            Float3 rotated = Float3.Forward.RotateY(90f);

            Assert.Equal(1f, rotated.X, 4);
            Assert.Equal(0f, rotated.Y, 4);
            Assert.Equal(0f, rotated.Z, 4);
        }
    }
}
