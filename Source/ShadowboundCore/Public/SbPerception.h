#pragma once

#include "SbNumerics.h"

namespace ShadowboundCore
{

// -----------------------------------------------------------------------------
// Everything needed to decide whether one combatant can sense another.
// -----------------------------------------------------------------------------
struct PerceptionQuery
{
	Float3 SelfPosition;
	Float3 SelfForward;
	Float3 TargetPosition;

	/// Maximum sight range while unaware.
	float ViewDistance = 14.f;

	/// Half-angle of the vision cone, in degrees.
	float ViewHalfAngleDegrees = 70.f;

	/// Radius inside which a target is felt regardless of facing or sight.
	/// This is what stops an enemy being walked past from directly behind,
	/// and is the single most important value for making stealth feel fair.
	float ProximityRadius = 3.f;

	bool HasTarget = false;
	bool TargetAlive = false;
	bool HasLineOfSight = false;

	PerceptionQuery() = default;
	PerceptionQuery(const Float3& selfPosition, const Float3& selfForward,
		const Float3& targetPosition, float viewDistance, float viewHalfAngleDegrees,
		float proximityRadius, bool hasTarget, bool targetAlive, bool hasLineOfSight)
		: SelfPosition(selfPosition)
		, SelfForward(selfForward)
		, TargetPosition(targetPosition)
		, ViewDistance(viewDistance)
		, ViewHalfAngleDegrees(viewHalfAngleDegrees)
		, ProximityRadius(proximityRadius)
		, HasTarget(hasTarget)
		, TargetAlive(targetAlive)
		, HasLineOfSight(hasLineOfSight)
	{
	}
};

// -----------------------------------------------------------------------------
// Decides whether an enemy notices a target.
//
// The rules are ordered by how certain they are:
//   1. A dead or absent target is never perceived.
//   2. Inside the proximity radius, a target is felt regardless of facing or
//      line of sight. Enemies have no blind spot at grappling distance.
//   3. Otherwise, a target must be in the vision cone, within range, with
//      line of sight.
//
// Sight is required for anything beyond arm's reach, which is what makes
// approaching from cover meaningful rather than cosmetic.
// -----------------------------------------------------------------------------
class PerceptionModel
{
public:
	static bool CanPerceive(const PerceptionQuery& query)
	{
		if (!query.HasTarget || !query.TargetAlive)
		{
			return false;
		}

		const float distance = Float3::DistanceXZ(query.SelfPosition, query.TargetPosition);

		if (distance <= query.ProximityRadius)
		{
			return true;
		}

		if (!query.HasLineOfSight)
		{
			return false;
		}

		return CoreMath::WithinCone(query.SelfPosition, query.SelfForward, query.TargetPosition,
			query.ViewHalfAngleDegrees, query.ViewDistance);
	}
};

} // namespace ShadowboundCore
