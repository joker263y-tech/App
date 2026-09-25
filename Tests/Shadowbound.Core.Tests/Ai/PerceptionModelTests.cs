using Shadowbound.Core.Ai;
using Shadowbound.Core.Numerics;
using Xunit;

namespace Shadowbound.Core.Tests.Ai
{
    public class PerceptionModelTests
    {
        private static readonly Float3 Origin = Float3.Zero;
        private static readonly Float3 Forward = new Float3(0f, 0f, 1f);

        private static PerceptionQuery Query(
            Float3 targetPosition,
            float viewDistance = 14f,
            float viewHalfAngle = 70f,
            float proximityRadius = 3f,
            bool hasTarget = true,
            bool targetAlive = true,
            bool lineOfSight = true)
        {
            return new PerceptionQuery(
                Origin,
                Forward,
                targetPosition,
                viewDistance,
                viewHalfAngle,
                proximityRadius,
                hasTarget,
                targetAlive,
                lineOfSight);
        }

        [Fact]
        public void CanPerceive_WithNoTarget_IsFalse()
        {
            Assert.False(PerceptionModel.CanPerceive(Query(new Float3(0f, 0f, 5f), hasTarget: false)));
        }

        [Fact]
        public void CanPerceive_WithDeadTarget_IsFalse()
        {
            Assert.False(PerceptionModel.CanPerceive(Query(new Float3(0f, 0f, 5f), targetAlive: false)));
        }

        [Fact]
        public void CanPerceive_TargetAheadInOpenGround_IsTrue()
        {
            Assert.True(PerceptionModel.CanPerceive(Query(new Float3(0f, 0f, 8f))));
        }

        [Fact]
        public void CanPerceive_TargetBehindAndOutOfProximity_IsFalse()
        {
            Assert.False(PerceptionModel.CanPerceive(Query(new Float3(0f, 0f, -8f))));
        }

        [Fact]
        public void CanPerceive_TargetBehindButVeryClose_IsTrue()
        {
            // Enemies have no blind spot at grappling distance, so a player
            // cannot park behind them indefinitely.
            Assert.True(PerceptionModel.CanPerceive(Query(new Float3(0f, 0f, -2f))));
        }

        [Fact]
        public void CanPerceive_CloseTargetBehindAWall_IsStillTrue()
        {
            // Proximity sensing deliberately ignores line of sight.
            Assert.True(PerceptionModel.CanPerceive(Query(new Float3(0f, 0f, -2f), lineOfSight: false)));
        }

        [Fact]
        public void CanPerceive_TargetAheadWithoutLineOfSight_IsFalse()
        {
            Assert.False(PerceptionModel.CanPerceive(Query(new Float3(0f, 0f, 8f), lineOfSight: false)));
        }

        [Fact]
        public void CanPerceive_TargetAheadButBeyondViewDistance_IsFalse()
        {
            Assert.False(PerceptionModel.CanPerceive(Query(new Float3(0f, 0f, 40f))));
        }

        [Fact]
        public void CanPerceive_TargetJustOutsideTheCone_IsFalse()
        {
            // 60 degrees off-axis with a 45 degree half-angle.
            var position = new Float3(8f, 0f, 4.6f);
            Assert.False(PerceptionModel.CanPerceive(
                Query(position, viewHalfAngle: 45f, proximityRadius: 1f)));
        }

        [Fact]
        public void CanPerceive_TargetJustInsideTheCone_IsTrue()
        {
            var position = new Float3(4f, 0f, 8f);
            Assert.True(PerceptionModel.CanPerceive(
                Query(position, viewHalfAngle: 45f, proximityRadius: 1f)));
        }

        [Fact]
        public void CanPerceive_IgnoresHeightDifference()
        {
            // A target on a ledge directly above is at horizontal distance zero.
            Assert.True(PerceptionModel.CanPerceive(Query(new Float3(0f, 30f, 0f))));
        }

        [Fact]
        public void CanPerceive_WiderConeExtendsAwareness()
        {
            var position = new Float3(8f, 0f, 8f);

            Assert.False(PerceptionModel.CanPerceive(Query(position, viewHalfAngle: 20f, proximityRadius: 1f)));
            Assert.True(PerceptionModel.CanPerceive(Query(position, viewHalfAngle: 90f, proximityRadius: 1f)));
        }
    }
}
