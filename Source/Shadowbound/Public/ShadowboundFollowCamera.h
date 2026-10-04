#pragma once

#include "CoreMinimal.h"
#include "Camera/CameraComponent.h"
#include "GameFramework/SpringArmComponent.h"
#include "ShadowboundFollowCamera.generated.h"

// -----------------------------------------------------------------------------
// Third-person follow camera, the Unreal replacement for the Unity
// ThirdPersonCamera MonoBehaviour.
//
// Rotation comes from the controller's control rotation (yaw from the look
// input, pitch clamped by the controller), because movement is
// camera-relative: the input layer needs to know which way the camera is
// facing to decide what "forward" means. Keeping the angles on the controller
// stops the two from disagreeing.
//
// The spring arm supplies what the Unity script did by hand: obstruction
// push-in (its collision probe) and smoothed following (its camera lag).
// -----------------------------------------------------------------------------
UCLASS(ClassGroup = (Camera), meta = (BlueprintSpawnableComponent))
class SHADOWBOUND_API UShadowboundFollowCamera : public USpringArmComponent
{
	GENERATED_BODY()

public:
	UShadowboundFollowCamera();

	UCameraComponent* GetCamera() const { return Camera; }

	/// Distance behind the target, in cm. The Unity rig used 6.5 world units.
	float GetFollowDistance() const { return TargetArmLength; }

private:
	UPROPERTY(VisibleAnywhere, Category = Camera)
	TObjectPtr<UCameraComponent> Camera;
};
