#include "ShadowboundPlayerController.h"

#include "Engine/World.h"
#include "GameFramework/HUD.h"
#include "GameFramework/PlayerCameraManager.h"

#include "EnhancedInputComponent.h"
#include "EnhancedInputSubsystems.h"
#include "InputAction.h"
#include "InputMappingContext.h"
#include "InputModifiers.h"

#include "ShadowboundHud.h"

namespace
{
/// The Unity driver clamped pitch to this window; keeping it means the camera
/// behaves the same on both engines.
constexpr float MinPitch = -35.f;
constexpr float MaxPitch = 60.f;

/// The half of the screen that owns the move stick; a touch on the other half
/// drives the camera. Splitting the screen is exactly what the Unity HUD did.
constexpr float MoveScreenFraction = 0.5f;

/// Adds a negating modifier so a key drives the opposite axis direction.
void AddNegate(class UObject* Outer, FEnhancedActionKeyMapping& Mapping)
{
	Mapping.Modifiers.Add(NewObject<UInputModifierNegate>(Outer));
}
} // namespace

AShadowboundPlayerController::AShadowboundPlayerController()
{
	bShowMouseCursor = false;
	bEnableClickEvents = false;
	bEnableMouseOverEvents = false;

	// The follow camera reads the control rotation, and the driver reads its
	// yaw for camera-relative movement - so the controller is the one place
	// those angles live.
	bAutoManageActiveCameraTarget = false;
}

void AShadowboundPlayerController::BeginPlay()
{
	Super::BeginPlay();

	// Pitch limits are enforced by the camera manager's own clamp, so every
	// path that writes rotation respects them.
	if (PlayerCameraManager != nullptr)
	{
		PlayerCameraManager->ViewPitchMin = MinPitch;
		PlayerCameraManager->ViewPitchMax = MaxPitch;
	}

	// Register the context once the local player exists. Adding a context that
	// is already present is harmless, so this is safe on a level reload.
	if (MappingContext != nullptr)
	{
		if (ULocalPlayer* LocalPlayer = GetLocalPlayer())
		{
			if (UEnhancedInputLocalPlayerSubsystem* Subsystem =
				LocalPlayer->GetSubsystem<UEnhancedInputLocalPlayerSubsystem>())
			{
				Subsystem->AddMappingContext(MappingContext, /*Priority*/ 0);
			}
		}
	}
}

void AShadowboundPlayerController::SetupInputComponent()
{
	Super::SetupInputComponent();

	// Actions are created here rather than in the constructor: the constructor
	// also runs for the class default object, where NewObject is not safe.
	MappingContext = NewObject<UInputMappingContext>(this, TEXT("IMC_Shadowbound"));

	auto MakeAction = [this](const TCHAR* Name, EInputActionValueType Type)
	{
		UInputAction* Action = NewObject<UInputAction>(this, Name);
		Action->ValueType = Type;
		return Action;
	};

	MoveForwardAction = MakeAction(TEXT("IA_MoveForward"), EInputActionValueType::Axis1D);
	MoveRightAction = MakeAction(TEXT("IA_MoveRight"), EInputActionValueType::Axis1D);
	LookAction = MakeAction(TEXT("IA_Look"), EInputActionValueType::Axis2D);
	CameraTurnAction = MakeAction(TEXT("IA_CameraTurn"), EInputActionValueType::Axis1D);
	AttackAction = MakeAction(TEXT("IA_Attack"), EInputActionValueType::Boolean);

	for (int32 Index = 0; Index < 5; ++Index)
	{
		AbilityActions[Index] = MakeAction(
			*FString::Printf(TEXT("IA_Ability%d"), Index), EInputActionValueType::Boolean);
	}

	// ---- movement -------------------------------------------------------------
	MappingContext->MapKey(MoveForwardAction, EKeys::W);
	AddNegate(this, MappingContext->MapKey(MoveForwardAction, EKeys::S));
	MappingContext->MapKey(MoveForwardAction, EKeys::Gamepad_LeftY);

	MappingContext->MapKey(MoveRightAction, EKeys::D);
	AddNegate(this, MappingContext->MapKey(MoveRightAction, EKeys::A));
	MappingContext->MapKey(MoveRightAction, EKeys::Gamepad_LeftX);

	// ---- look -----------------------------------------------------------------
	MappingContext->MapKey(LookAction, EKeys::Mouse2D);
	MappingContext->MapKey(LookAction, EKeys::Gamepad_Right2D);

	// ---- camera orbit ---------------------------------------------------------
	AddNegate(this, MappingContext->MapKey(CameraTurnAction, EKeys::Q));
	MappingContext->MapKey(CameraTurnAction, EKeys::E);

	// ---- attack ---------------------------------------------------------------
	MappingContext->MapKey(AttackAction, EKeys::LeftMouseButton);

	// ---- abilities ------------------------------------------------------------
	const FKey AbilityKeys[5] = {
		EKeys::One, EKeys::Two, EKeys::Three, EKeys::Four, EKeys::Five
	};
	for (int32 Index = 0; Index < 5; ++Index)
	{
		MappingContext->MapKey(AbilityActions[Index], AbilityKeys[Index]);
	}

	// Space is the dash on touch and keyboard alike (Ashstep is index 2).
	MappingContext->MapKey(AbilityActions[2], EKeys::SpaceBar);

	UEnhancedInputComponent* EnhancedInput = Cast<UEnhancedInputComponent>(InputComponent);
	if (EnhancedInput == nullptr)
	{
		return;
	}

	EnhancedInput->BindAction(MoveForwardAction, ETriggerEvent::Triggered, this,
		&AShadowboundPlayerController::OnMoveForward);
	EnhancedInput->BindAction(MoveRightAction, ETriggerEvent::Triggered, this,
		&AShadowboundPlayerController::OnMoveRight);
	EnhancedInput->BindAction(LookAction, ETriggerEvent::Triggered, this,
		&AShadowboundPlayerController::OnLook);
	EnhancedInput->BindAction(CameraTurnAction, ETriggerEvent::Triggered, this,
		&AShadowboundPlayerController::OnCameraTurn);

	EnhancedInput->BindAction(AttackAction, ETriggerEvent::Started, this,
		&AShadowboundPlayerController::OnAttackStarted);
	EnhancedInput->BindAction(AttackAction, ETriggerEvent::Completed, this,
		&AShadowboundPlayerController::OnAttackCompleted);

	EnhancedInput->BindAction(AbilityActions[0], ETriggerEvent::Started, this,
		&AShadowboundPlayerController::OnAbility0);
	EnhancedInput->BindAction(AbilityActions[1], ETriggerEvent::Started, this,
		&AShadowboundPlayerController::OnAbility1);
	EnhancedInput->BindAction(AbilityActions[2], ETriggerEvent::Started, this,
		&AShadowboundPlayerController::OnAbility2);
	EnhancedInput->BindAction(AbilityActions[3], ETriggerEvent::Started, this,
		&AShadowboundPlayerController::OnAbility3);
	EnhancedInput->BindAction(AbilityActions[4], ETriggerEvent::Started, this,
		&AShadowboundPlayerController::OnAbility4);
}

void AShadowboundPlayerController::PlayerTick(float DeltaTime)
{
	// The move axis is rebuilt from scratch every frame. Enhanced Input fires a
	// Triggered event while a key is held, but not one when it is released, so
	// clearing here - before the input stack is processed - is what stops the
	// player coasting after letting go.
	MoveAxis = FVector2D::ZeroVector;

	Super::PlayerTick(DeltaTime);

	// Touch is polled rather than event-driven: the on-screen controls are
	// rectangles we hit-test ourselves, and a per-frame poll keeps them working
	// without a second input stack.
	UpdateTouch(DeltaTime);
}

// ================================ input handlers ============================

void AShadowboundPlayerController::OnMoveForward(const FInputActionValue& Value)
{
	MoveAxis.Y = Value.Get<float>();
}

void AShadowboundPlayerController::OnMoveRight(const FInputActionValue& Value)
{
	MoveAxis.X = Value.Get<float>();
}

void AShadowboundPlayerController::OnLook(const FInputActionValue& Value)
{
	const FVector2D Axis = Value.Get<FVector2D>();

	// Screen Y grows downward; pitch grows upward, hence the negated term.
	ApplyLook(Axis.X * LookSensitivity, -Axis.Y * LookSensitivity);
}

void AShadowboundPlayerController::OnCameraTurn(const FInputActionValue& Value)
{
	const float DeltaSeconds = GetWorld() != nullptr ? GetWorld()->GetDeltaSeconds() : 0.f;
	ApplyLook(Value.Get<float>() * CameraTurnSpeed * DeltaSeconds, 0.f);
}

void AShadowboundPlayerController::OnAttackStarted(const FInputActionValue& /*Value*/)
{
	bAttackHeld = true;
}

void AShadowboundPlayerController::OnAttackCompleted(const FInputActionValue& /*Value*/)
{
	bAttackHeld = false;
}

void AShadowboundPlayerController::OnAbility0(const FInputActionValue& /*Value*/) { QueueAbility(0); }
void AShadowboundPlayerController::OnAbility1(const FInputActionValue& /*Value*/) { QueueAbility(1); }
void AShadowboundPlayerController::OnAbility2(const FInputActionValue& /*Value*/) { QueueAbility(2); }
void AShadowboundPlayerController::OnAbility3(const FInputActionValue& /*Value*/) { QueueAbility(3); }
void AShadowboundPlayerController::OnAbility4(const FInputActionValue& /*Value*/) { QueueAbility(4); }

void AShadowboundPlayerController::QueueAbility(int32 Index)
{
	if (Index >= 0 && Index < 5)
	{
		QueuedAbility = Index;
	}
}

bool AShadowboundPlayerController::ConsumeQueuedAbility(int32& OutIndex)
{
	if (QueuedAbility < 0)
	{
		return false;
	}

	OutIndex = QueuedAbility;
	QueuedAbility = -1;
	return true;
}

void AShadowboundPlayerController::ApplyLook(float YawDelta, float PitchDelta)
{
	FRotator Rotation = GetControlRotation();
	Rotation.Yaw += YawDelta;
	Rotation.Pitch = FMath::Clamp(Rotation.Pitch + PitchDelta, MinPitch, MaxPitch);
	Rotation.Roll = 0.f;
	SetControlRotation(Rotation);
}

// ================================= touch ====================================

void AShadowboundPlayerController::UpdateTouch(float /*DeltaTime*/)
{
	for (int32 Finger = 0; Finger < ETouchIndex::MAX_TOUCHES; ++Finger)
	{
		float LocationX = 0.f;
		float LocationY = 0.f;
		bool bPressed = false;
		GetInputTouchState(static_cast<ETouchIndex::Type>(Finger), LocationX, LocationY, bPressed);

		const FVector2D Position(LocationX, LocationY);
		const bool bWasPressed = Touches.Contains(Finger);

		if (bPressed && !bWasPressed)
		{
			BeginTouch(Finger, Position);
		}
		else if (bPressed && bWasPressed)
		{
			MoveTouch(Finger, Position);
		}
		else if (!bPressed && bWasPressed)
		{
			EndTouch(Finger);
		}
	}
}

void AShadowboundPlayerController::BeginTouch(int32 Finger, const FVector2D& Position)
{
	// An ability button wins over everything: the buttons overlap the camera
	// half of the screen, and a player pressing one means to attack, not to
	// turn the camera.
	if (AShadowboundHud* Hud = Cast<AShadowboundHud>(GetHUD()))
	{
		for (int32 Index = 0; Index < AShadowboundHud::AbilityButtonCount; ++Index)
		{
			FVector2D ButtonPosition;
			FVector2D ButtonSize;
			if (!Hud->GetAbilityButtonRect(Index, ButtonPosition, ButtonSize))
			{
				continue;
			}

			if (Position.X >= ButtonPosition.X && Position.X <= ButtonPosition.X + ButtonSize.X &&
				Position.Y >= ButtonPosition.Y && Position.Y <= ButtonPosition.Y + ButtonSize.Y)
			{
				FTouchSlot Slot;
				Slot.Role = ETouchRole::Button;
				Slot.Origin = Position;
				Slot.Last = Position;
				Touches.Add(Finger, Slot);

				QueueAbility(Index);
				return;
			}
		}
	}

	int32 ViewportSizeX = 0;
	int32 ViewportSizeY = 0;
	GetViewportSize(ViewportSizeX, ViewportSizeY);
	const float SplitX = static_cast<float>(ViewportSizeX) * MoveScreenFraction;

	FTouchSlot Slot;
	Slot.Origin = Position;
	Slot.Last = Position;

	if (Position.X <= SplitX)
	{
		Slot.Role = ETouchRole::Move;
		bTouchMoveActive = true;
		TouchMoveOrigin = Position;
		TouchMoveKnob = Position;
	}
	else
	{
		Slot.Role = ETouchRole::Look;
	}

	Touches.Add(Finger, Slot);
}

void AShadowboundPlayerController::MoveTouch(int32 Finger, const FVector2D& Position)
{
	FTouchSlot* Slot = Touches.Find(Finger);
	if (Slot == nullptr)
	{
		return;
	}

	switch (Slot->Role)
	{
	case ETouchRole::Move:
	{
		TouchMoveKnob = Position;

		const FVector2D Delta = Position - TouchMoveOrigin;
		FVector2D Axis = Delta / AShadowboundHud::MoveStickRadius;

		if (Axis.SizeSquared() > 1.f)
		{
			Axis.Normalize();
		}

		// Touch movement is camera-relative exactly like the keyboard: the
		// driver turns this axis into a world direction using the camera yaw.
		MoveAxis = Axis;
		break;
	}

	case ETouchRole::Look:
	{
		const FVector2D Delta = Position - Slot->Last;
		Slot->Last = Position;
		ApplyLook(Delta.X * LookSensitivity, -Delta.Y * LookSensitivity);
		break;
	}

	default:
		break;
	}
}

void AShadowboundPlayerController::EndTouch(int32 Finger)
{
	FTouchSlot* Slot = Touches.Find(Finger);
	if (Slot == nullptr)
	{
		return;
	}

	if (Slot->Role == ETouchRole::Move)
	{
		// Only clear the stick when the finger that owned it lifts; another
		// finger on the screen must not stop the player walking.
		bTouchMoveActive = false;
		TouchMoveOrigin = FVector2D::ZeroVector;
		TouchMoveKnob = FVector2D::ZeroVector;
	}

	Touches.Remove(Finger);
}
