#include "ShadowboundPlayerDriver.h"

#include "ShadowboundPlayerController.h"

ShadowboundCore::CombatIntent FSbPlayerDriver::Decide(float DeltaTime,
	ShadowboundCore::Combatant& Self,
	ShadowboundCore::EncounterSimulation& World)
{
	using namespace ShadowboundCore;

	CombatIntent Intent = CombatIntent::None();

	AShadowboundPlayerController* Controller = Owner.Get();
	if (Controller == nullptr)
	{
		return Intent;
	}

	// ---- camera-relative movement ------------------------------------------------
	const FVector2D Axis = Controller->GetMoveAxis();

	if (Axis.SizeSquared() > 0.0004f)
	{
		// The camera yaw IS the core's facing convention (see
		// ShadowboundConvert), so forward for a yaw of Y degrees is exactly
		// CoreMath's DegreesToDirection(Y).
		const float YawDegrees = static_cast<float>(Controller->GetControlRotation().Yaw);
		const Float3 CameraForward = Float3::DegreesToDirection(YawDegrees);
		const Float3 CameraRight = Float3(CameraForward.Z, 0.f, -CameraForward.X);

		Float3 WorldDirection =
			(CameraForward * Axis.Y) + (CameraRight * Axis.X);

		const Float3 Flat = WorldDirection.FlattenedXZ();
		if (Flat != Float3::Zero)
		{
			Intent.MoveDirection = Flat;
			Intent.SpeedScale = 1.f;
		}
	}

	// ---- abilities ---------------------------------------------------------------
	int32 QueuedAbility = -1;
	if (Controller->ConsumeQueuedAbility(QueuedAbility))
	{
		Intent.ActivateAbility = true;
		Intent.AbilityIndex = QueuedAbility;
	}
	else if (Controller->IsHeldAttack())
	{
		// A held basic attack keeps swinging without repeated presses - the
		// same loop the Unity build had on a held left mouse button.
		Intent.ActivateAbility = true;
		Intent.AbilityIndex = 0;
	}

	(void)Self;
	(void)World;
	return Intent;
}
