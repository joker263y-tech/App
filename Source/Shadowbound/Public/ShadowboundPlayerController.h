#pragma once

#include "CoreMinimal.h"
#include "GameFramework/PlayerController.h"
#include "InputActionValue.h"

#include "ShadowboundPlayerController.generated.h"

class UInputAction;
class UInputMappingContext;

// -----------------------------------------------------------------------------
// The player's input authority, ported from the Unity PlayerInputDriver.
//
// It owns every input decision - move axis, queued ability, held attack, look
// and touch - and exposes them as plain queries. FSbPlayerDriver reads those
// queries once per simulation step and turns them into a CombatIntent, so this
// class never touches the core directly and never moves anything. That keeps
// the same boundary the Unity build had: input produces intent, the core
// decides what happens.
//
// Input is Enhanced Input, but the mapping context and every action are built
// in C++ rather than committed as assets, so no .uasset is needed and the
// binding is reviewable in a diff - the same reasoning as the runtime-built
// arena.
//
// Look and movement are applied to the CONTROL rotation, not the pawn: the
// body's facing belongs to the core (which turns it toward its movement
// direction), and the follow camera reads the control rotation. The Unity build
// split the two the same way.
// -----------------------------------------------------------------------------
UCLASS()
class SHADOWBOUND_API AShadowboundPlayerController : public APlayerController
{
	GENERATED_BODY()

public:
	AShadowboundPlayerController();

	virtual void BeginPlay() override;
	virtual void PlayerTick(float DeltaTime) override;
	virtual void SetupInputComponent() override;

	// ---- driver API -----------------------------------------------------------

	/// Camera-relative move request: X is right, Y is forward. Zero when idle.
	FVector2D GetMoveAxis() const { return MoveAxis; }

	/// Pops the most recently queued ability index, if any. One-shot: reading
	/// it clears it, so a press cannot fire twice.
	bool ConsumeQueuedAbility(int32& OutIndex);

	/// True while the basic-attack button is held.
	bool IsHeldAttack() const { return bAttackHeld; }

	// ---- on-screen stick (read by the HUD) ------------------------------------

	bool IsTouchMoveActive() const { return bTouchMoveActive; }
	FVector2D GetMoveStickOrigin() const { return TouchMoveOrigin; }
	FVector2D GetMoveStickKnob() const { return TouchMoveKnob; }

	/// Degrees of yaw/pitch per pixel of look input. The Unity driver used 0.12.
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = Input)
	float LookSensitivity = 0.12f;

	/// Keyboard camera-orbit speed, in degrees per second, for Q/E.
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = Input)
	float CameraTurnSpeed = 140.f;

protected:
	void OnMoveForward(const FInputActionValue& Value);
	void OnMoveRight(const FInputActionValue& Value);
	void OnLook(const FInputActionValue& Value);
	void OnCameraTurn(const FInputActionValue& Value);

	void OnAttackStarted(const FInputActionValue& Value);
	void OnAttackCompleted(const FInputActionValue& Value);

	void OnAbility0(const FInputActionValue& Value);
	void OnAbility1(const FInputActionValue& Value);
	void OnAbility2(const FInputActionValue& Value);
	void OnAbility3(const FInputActionValue& Value);
	void OnAbility4(const FInputActionValue& Value);

	/// Queues an ability activation. Later presses overwrite earlier ones; the
	/// core consumes at most one per step, and firing two in a frame is never
	/// what the player meant.
	void QueueAbility(int32 Index);

	UPROPERTY(Transient)
	TObjectPtr<UInputMappingContext> MappingContext;

	UPROPERTY(Transient)
	TObjectPtr<UInputAction> MoveForwardAction;

	UPROPERTY(Transient)
	TObjectPtr<UInputAction> MoveRightAction;

	UPROPERTY(Transient)
	TObjectPtr<UInputAction> LookAction;

	UPROPERTY(Transient)
	TObjectPtr<UInputAction> CameraTurnAction;

	UPROPERTY(Transient)
	TObjectPtr<UInputAction> AttackAction;

	UPROPERTY(Transient)
	TObjectPtr<UInputAction> AbilityActions[5];

private:
	FVector2D MoveAxis = FVector2D::ZeroVector;
	bool bAttackHeld = false;
	int32 QueuedAbility = -1;

	/// Applies a look delta to the control rotation and clamps pitch the way
	/// the Unity driver did (-35 to +60 degrees).
	void ApplyLook(float YawDelta, float PitchDelta);

	// ---- touch ----------------------------------------------------------------

	enum class ETouchRole : uint8
	{
		None,
		Move,
		Look,
		Button
	};

	struct FTouchSlot
	{
		ETouchRole Role = ETouchRole::None;
		FVector2D Origin = FVector2D::ZeroVector;
		FVector2D Last = FVector2D::ZeroVector;
	};

	/// Polls active touches and routes them: an ability button fires on press;
	/// the left half becomes the move stick; the right half becomes look.
	void UpdateTouch(float DeltaTime);

	void BeginTouch(int32 Finger, const FVector2D& Position);
	void MoveTouch(int32 Finger, const FVector2D& Position);
	void EndTouch(int32 Finger);

	TMap<int32, FTouchSlot> Touches;

	bool bTouchMoveActive = false;
	FVector2D TouchMoveOrigin = FVector2D::ZeroVector;
	FVector2D TouchMoveKnob = FVector2D::ZeroVector;
};
