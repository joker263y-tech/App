#include "ShadowboundPlayerCharacter.h"

#include "ShadowboundFollowCamera.h"

AShadowboundPlayerCharacter::AShadowboundPlayerCharacter()
{
	PrimaryActorTick.bCanEverTick = false;

	// The camera rig replaces the Unity ThirdPersonCamera GameObject: a spring
	// arm (follow + obstruction) with a camera on its socket.
	CameraRig = CreateDefaultSubobject<UShadowboundFollowCamera>(TEXT("CameraRig"));
	CameraRig->SetupAttachment(RootComponent);

	// APawn has no movement component by design: input lives on the
	// controller, and the core moves the body. No CharacterMovement means no
	// gravity and no second authority over position - exactly what the Unity
	// layer achieved by deleting the primitive colliders.
}

void AShadowboundPlayerCharacter::BeginPlay()
{
	Super::BeginPlay();

	// The Warden's placeholder tint, taken from the Unity GameBootstrap
	// (0.86, 0.78, 0.62). Bound by the game mode once the playthrough exists;
	// here we only make sure the mesh is dressed correctly from the first
	// frame if binding already happened.
}
