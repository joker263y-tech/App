#include "ShadowboundHud.h"

#include "Engine/Canvas.h"
#include "Engine/Engine.h"
#include "Engine/GameViewportClient.h"
#include "GameFramework/PlayerController.h"
#include "UnrealClient.h"

#include "SbAbilities.h"
#include "SbCombatant.h"
#include "SbEncounter.h"
#include "SbVitals.h"

#include "ShadowboundGameInstance.h"
#include "ShadowboundPlayerController.h"

namespace
{
/// How long a damage number stays on screen before it is dropped.
constexpr float DamageNumberLifetime = 0.9f;

/// How far upward a damage number travels over its life, in pixels.
constexpr float DamageNumberRise = 46.f;

FLinearColor WithAlpha(const FLinearColor& Color, float Alpha)
{
	FLinearColor Out = Color;
	Out.A = Alpha;
	return Out;
}

FString FormatWhole(float Value)
{
	return FString::Printf(TEXT("%d"), FMath::RoundToInt(Value));
}
} // namespace

bool AShadowboundHud::GetViewportSize(FVector2D& OutSize) const
{
	if (PlayerOwner != nullptr)
	{
		int32 SizeX = 0;
		int32 SizeY = 0;
		PlayerOwner->GetViewportSize(SizeX, SizeY);

		if (SizeX > 0 && SizeY > 0)
		{
			OutSize = FVector2D(static_cast<float>(SizeX), static_cast<float>(SizeY));
			return true;
		}
	}

	if (GEngine != nullptr && GEngine->GameViewport != nullptr &&
		GEngine->GameViewport->Viewport != nullptr)
	{
		const FIntPoint Size = GEngine->GameViewport->Viewport->GetSizeXY();
		if (Size.X > 0 && Size.Y > 0)
		{
			OutSize = FVector2D(static_cast<float>(Size.X), static_cast<float>(Size.Y));
			return true;
		}
	}

	return false;
}

bool AShadowboundHud::GetAbilityButtonRect(int32 Index, FVector2D& OutPosition, FVector2D& OutSize) const
{
	if (Index < 0 || Index >= AbilityButtonCount)
	{
		return false;
	}

	FVector2D ViewportSize;
	if (!GetViewportSize(ViewportSize))
	{
		return false;
	}

	// The five buttons sit in a row along the bottom edge, in reading order,
	// so the player's thumb finds the basic attack (index 0) on the left and
	// the defensive ward on the right. The Unity build laid them out the same
	// way and hit-tested the same rectangles.
	const float RowWidth = (ButtonSize * AbilityButtonCount)
		+ (ButtonGap * (AbilityButtonCount - 1));

	const float RowX = ViewportSize.X - Margin - RowWidth;
	const float RowY = ViewportSize.Y - Margin - ButtonSize;

	OutPosition = FVector2D(RowX + (Index * (ButtonSize + ButtonGap)), RowY);
	OutSize = FVector2D(ButtonSize, ButtonSize);
	return true;
}

void AShadowboundHud::ReportDamage(const FVector& WorldLocation, float Amount, bool bCritical)
{
	if (Amount <= 0.5f)
	{
		return;
	}

	FDamageNumber Number;
	Number.WorldLocation = WorldLocation;
	Number.Amount = Amount;
	Number.Age = 0.f;
	Number.bCritical = bCritical;

	// A short cap keeps a large cleave from filling the queue with numbers
	// that would all draw on top of each other anyway.
	if (DamageNumbers.Num() >= 24)
	{
		DamageNumbers.RemoveAt(0);
	}

	DamageNumbers.Add(Number);
}

void AShadowboundHud::DrawHUD()
{
	Super::DrawHUD();

	if (Canvas == nullptr)
	{
		return;
	}

	DrawVitals();
	DrawAbilityButtons();
	DrawMoveStick();
	DrawFloatingDamage();
	DrawStatusLine();
}

void AShadowboundHud::DrawVitals()
{
	UShadowboundGameInstance* Game = GetWorld() != nullptr
		? GetWorld()->GetGameInstance<UShadowboundGameInstance>()
		: nullptr;

	if (Game == nullptr || Game->PlayerCombatant == nullptr)
	{
		return;
	}

	const ShadowboundCore::Combatant& Player = *Game->PlayerCombatant;

	const float HealthFraction = FMath::Clamp(Player.Vitals().HealthFraction(), 0.f, 1.f);
	const float StaminaFraction = FMath::Clamp(Player.Vitals().StaminaFraction(), 0.f, 1.f);

	const float HealthY = Margin;
	const float StaminaY = Margin + BarHeight + 6.f;

	// Health: a dark trough with a red fill, so an empty bar is still visible.
	DrawRect(FLinearColor(0.05f, 0.04f, 0.04f, 0.75f), Margin, HealthY, BarWidth, BarHeight);
	DrawRect(FLinearColor(0.62f, 0.16f, 0.16f, 0.95f), Margin, HealthY, BarWidth * HealthFraction, BarHeight);

	// Stamina: the resource every ability spends.
	DrawRect(FLinearColor(0.04f, 0.05f, 0.06f, 0.75f), Margin, StaminaY, BarWidth, BarHeight);
	DrawRect(FLinearColor(0.30f, 0.52f, 0.58f, 0.95f), Margin, StaminaY, BarWidth * StaminaFraction, BarHeight);

	DrawText(FormatWhole(Player.Vitals().Health()) + TEXT(" / ") + FormatWhole(Player.Vitals().MaxHealth()),
		FLinearColor::White, Margin + 8.f, HealthY + 2.f, nullptr, 0.9f);

	DrawText(FormatWhole(Player.Vitals().Stamina()) + TEXT(" / ") + FormatWhole(Player.Vitals().MaxStamina()),
		FLinearColor::White, Margin + 8.f, StaminaY + 2.f, nullptr, 0.9f);
}

void AShadowboundHud::DrawAbilityButtons()
{
	UShadowboundGameInstance* Game = GetWorld() != nullptr
		? GetWorld()->GetGameInstance<UShadowboundGameInstance>()
		: nullptr;

	if (Game == nullptr || Game->Encounter == nullptr || Game->PlayerCombatant == nullptr)
	{
		return;
	}

	// The controller's per-frame state lives on the encounter participant, but
	// cooldowns are per-ability and available without a participant lookup via
	// the player's own controller. The participant lookup is the single source
	// of truth for readiness.
	ShadowboundCore::Participant* PlayerParticipant =
		Game->Encounter->FindParticipant(Game->PlayerCombatant.get());

	if (PlayerParticipant == nullptr)
	{
		return;
	}

	for (int32 Index = 0; Index < AbilityButtonCount; ++Index)
	{
		FVector2D Position;
		FVector2D Size;
		if (!GetAbilityButtonRect(Index, Position, Size))
		{
			continue;
		}

		const float Cooldown = FMath::Clamp(PlayerParticipant->Abilities.CooldownFraction(Index), 0.f, 1.f);
		const bool bReady = PlayerParticipant->Abilities.IsReady(Index)
			&& !PlayerParticipant->Abilities.IsBusy();

		const FLinearColor Base = bReady
			? FLinearColor(0.22f, 0.20f, 0.26f, 0.80f)
			: FLinearColor(0.10f, 0.09f, 0.12f, 0.80f);

		DrawRect(Base, Position.X, Position.Y, Size.X, Size.Y);

		// Cooldown sweep: a dark curtain that shrinks to nothing as the ability
		// comes back. The Unity HUD used a radial sweep; a fill is its square
		// equivalent and carries the same information.
		if (Cooldown > 0.f)
		{
			DrawRect(FLinearColor(0.f, 0.f, 0.f, 0.55f),
				Position.X, Position.Y + Size.Y * (1.f - Cooldown), Size.X, Size.Y * Cooldown);
		}

		// Ability name, truncated to the button. Display names are short by
		// design ("Umbra Lance" is the longest at eleven characters).
		const ShadowboundCore::AbilityDefinition* Ability = PlayerParticipant->Abilities[Index];
		const FString Label = Ability != nullptr
			? FString(ANSI_TO_TCHAR(Ability->DisplayName.c_str()))
			: FString::Printf(TEXT("%d"), Index + 1);

		DrawText(Label, bReady ? FLinearColor::White : FLinearColor(0.6f, 0.6f, 0.6f, 1.f),
			Position.X + 6.f, Position.Y + Size.Y * 0.5f - 8.f, nullptr, 0.8f);
	}
}

void AShadowboundHud::DrawMoveStick()
{
	AShadowboundPlayerController* Controller = Cast<AShadowboundPlayerController>(PlayerOwner);

	FVector2D ViewportSize;
	if (!GetViewportSize(ViewportSize))
	{
		return;
	}

	const float RestX = Margin + MoveStickRadius;
	const float RestY = ViewportSize.Y - Margin - MoveStickRadius;

	// Base pad. Drawn as a translucent rectangle rather than a circle because
	// AHUD's primitive surface is rectangular and this project authors no
	// textures - the shape is placeholder art, the touch region is the real
	// thing.
	const FVector2D BaseCenter = (Controller != nullptr && Controller->IsTouchMoveActive())
		? Controller->GetMoveStickOrigin()
		: FVector2D(RestX, RestY);

	DrawRect(FLinearColor(0.14f, 0.14f, 0.18f, 0.35f),
		BaseCenter.X - MoveStickRadius, BaseCenter.Y - MoveStickRadius,
		MoveStickRadius * 2.f, MoveStickRadius * 2.f);

	// Knob, clamped inside the pad exactly the way the axis itself is clamped.
	FVector2D Knob = (Controller != nullptr && Controller->IsTouchMoveActive())
		? Controller->GetMoveStickKnob()
		: BaseCenter;

	const FVector2D Delta = Knob - BaseCenter;
	if (Delta.SizeSquared() > MoveStickRadius * MoveStickRadius)
	{
		Knob = BaseCenter + Delta.GetSafeNormal() * MoveStickRadius;
	}

	const float KnobRadius = MoveStickRadius * 0.35f;
	DrawRect(FLinearColor(0.85f, 0.85f, 0.9f, 0.55f),
		Knob.X - KnobRadius, Knob.Y - KnobRadius, KnobRadius * 2.f, KnobRadius * 2.f);
}

void AShadowboundHud::DrawFloatingDamage()
{
	if (DamageNumbers.Num() == 0)
	{
		return;
	}

	const float DeltaSeconds = GetWorld() != nullptr
		? GetWorld()->GetDeltaSeconds()
		: 0.f;

	for (int32 Index = DamageNumbers.Num() - 1; Index >= 0; --Index)
	{
		FDamageNumber& Number = DamageNumbers[Index];
		Number.Age += DeltaSeconds;

		if (Number.Age >= DamageNumberLifetime)
		{
			DamageNumbers.RemoveAt(Index);
			continue;
		}

		FVector Screen;
		if (!Project(Number.WorldLocation, Screen))
		{
			continue;
		}

		const float Alpha = FMath::Clamp(1.f - (Number.Age / DamageNumberLifetime), 0.f, 1.f);
		const float Rise = DamageNumberRise * (Number.Age / DamageNumberLifetime);

		const FLinearColor Color = Number.bCritical
			? WithAlpha(FLinearColor(1.f, 0.85f, 0.3f, 1.f), Alpha)
			: WithAlpha(FLinearColor(1.f, 0.35f, 0.28f, 1.f), Alpha);

		DrawText(FormatWhole(Number.Amount), Color,
			Screen.X, Screen.Y - Rise, nullptr, Number.bCritical ? 1.2f : 0.95f);
	}
}

void AShadowboundHud::DrawStatusLine()
{
	UShadowboundGameInstance* Game = GetWorld() != nullptr
		? GetWorld()->GetGameInstance<UShadowboundGameInstance>()
		: nullptr;

	if (Game == nullptr || Game->Encounter == nullptr)
	{
		return;
	}

	const FString Line = FString::Printf(TEXT("%s  -  %d hostiles"),
		*Game->StartingRegion, Game->Encounter->HostilesRemaining());

	DrawText(Line, FLinearColor(0.8f, 0.8f, 0.85f, 0.9f),
		Margin, Margin + (BarHeight + 6.f) * 2.f + 6.f, nullptr, 0.95f);
}
