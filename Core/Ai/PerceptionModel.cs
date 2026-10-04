using Shadowbound.Core.Numerics;

namespace Shadowbound.Core.Ai
{
    /// <summary>Everything needed to decide whether one combatant can sense another.</summary>
    public readonly struct PerceptionQuery
    {
        public readonly Float3 SelfPosition;
        public readonly Float3 SelfForward;
        public readonly Float3 TargetPosition;

        /// <summary>Maximum sight range while unaware.</summary>
        public readonly float ViewDistance;

        /// <summary>Half-angle of the vision cone, in degrees.</summary>
        public readonly float ViewHalfAngleDegrees;

        /// <summary>
        /// Radius inside which a target is felt regardless of facing or sight.
        /// This is what stops an enemy being walked past from directly behind,
        /// and is the single most important value for making stealth feel fair.
        /// </summary>
        public readonly float ProximityRadius;

        public readonly bool HasTarget;
        public readonly bool TargetAlive;
        public readonly bool HasLineOfSight;

        public PerceptionQuery(
            Float3 selfPosition,
            Float3 selfForward,
            Float3 targetPosition,
            float viewDistance,
            float viewHalfAngleDegrees,
            float proximityRadius,
            bool hasTarget,
            bool targetAlive,
            bool hasLineOfSight)
        {
            SelfPosition = selfPosition;
            SelfForward = selfForward;
            TargetPosition = targetPosition;
            ViewDistance = viewDistance;
            ViewHalfAngleDegrees = viewHalfAngleDegrees;
            ProximityRadius = proximityRadius;
            HasTarget = hasTarget;
            TargetAlive = targetAlive;
            HasLineOfSight = hasLineOfSight;
        }
    }

    /// <summary>
    /// Decides whether an enemy notices a target.
    ///
    /// The rules are ordered by how certain they are:
    ///   1. A dead or absent target is never perceived.
    ///   2. Inside the proximity radius, a target is felt regardless of facing
    ///      or line of sight. Enemies have no blind spot at grappling distance.
    ///   3. Otherwise, a target must be in the vision cone, within range, with
    ///      line of sight.
    ///
    /// Sight is required for anything beyond arm's reach, which is what makes
    /// approaching from cover meaningful rather than cosmetic.
    /// </summary>
    public static class PerceptionModel
    {
        public static bool CanPerceive(in PerceptionQuery query)
        {
            if (!query.HasTarget || !query.TargetAlive)
            {
                return false;
            }

            float distance = Float3.DistanceXZ(query.SelfPosition, query.TargetPosition);

            if (distance <= query.ProximityRadius)
            {
                return true;
            }

            if (!query.HasLineOfSight)
            {
                return false;
            }

            return FMath.WithinCone(
                query.SelfPosition,
                query.SelfForward,
                query.TargetPosition,
                query.ViewHalfAngleDegrees,
                query.ViewDistance);
        }
    }
}
