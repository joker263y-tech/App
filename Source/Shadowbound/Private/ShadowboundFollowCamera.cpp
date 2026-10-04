#include "ShadowboundFollowCamera.h"

namespace
{
/// The Unity rig's default distance (6.5 world units) expressed in Unreal
/// units (1 unit = 1 cm for the default scale).
constexpr float FollowDistance = 650.f;

/// The Unity rig's follow damping, which the spring arm's camera lag replaces.
constexpr float FollowLagSpeed = 12.f;
} // namespace

UShadowboundFollowCamera::UShadowboundFollowCamera()
{
	PrimaryComponentTick.bCanEverTick = false;

	TargetArmLength = FollowDistance;
	bUsePawnControlRotation = true;
	bEnableCameraLag = true;
	CameraLagSpeed = FollowLagSpeed;
	bEnableCameraRotationLag = false;

	// Collision probe: pull in when something intrudes between the camera and
	// the Warden, ease back out afterwards (the arm's own interpolation does
	// the easing - instant in, smooth out is the built-in behaviour).
	bDoCollisionTest = true;
	ProbeSize = 12.f;
	ProbeChannel = ECC_Camera;

	// Eye height above the capsule centre: chest-to-head, matching the Unity
	// rig's target offset of 1.6 world units above the feet.
	SetRelativeLocation(FVector(0.f, 0.f, 50.f));

	Camera = CreateDefaultSubobject<UCameraComponent>(TEXT("Camera"));
	Camera->SetupAttachment(this, USpringArmComponent::SocketName);
	Camera->FieldOfView = 55.f;
	Camera->bUsePawnControlRotation = false;
}
