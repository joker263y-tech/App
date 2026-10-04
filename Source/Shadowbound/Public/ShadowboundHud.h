#pragma once

#include "CoreMinimal.h"
#include "GameFramework/HUD.h"
#include "ShadowboundHud.generated.h"

// -----------------------------------------------------------------------------
// The heads-up display, drawn directly with AHUD primitives.
//
// The Unity build constructed its HUD and touch controls in code rather than
// authored prefabs, and polled pointers manually instead of routing through
// uGUI's event system (an EventSystem object and an input module are silent
// failure points on a device build). This keeps that decision: no UMG widget
// tree, no authored assets - rectangles, text and hit-tests in code.
//
// Layout is computed from the viewport so it works on any phone aspect; the
// ability buttons are also the touch targets the player controller hit-tests
// against (same rectangles, one source of truth).
//
// Drawing uses the AHUD helpers (DrawRect/DrawText), which are the small,
// stable surface of the Canvas API - unlike the UCanvas draw calls, whose
// overloads have changed between engine versions.
// -----------------------------------------------------------------------------
UCLASS()
class SHADOWBOUND_API AShadowboundHud : public AHUD
{
	GENERATED_BODY()

public:
	virtual void DrawHUD() override;

	/// Screen rectangle of ability button `Index` (0..4), in viewport pixels.
	/// Returns false when the index is out of range. Shared with the player
	/// controller, which hit-tests touches against the same rectangles.
	bool GetAbilityButtonRect(int32 Index, FVector2D& OutPosition, FVector2D& OutSize) const;

	/// Records a hit so it can be drawn floating up from where it landed.
	/// Called by the game mode when the encounter reports damage.
	void ReportDamage(const FVector& WorldLocation, float Amount, bool bCritical);

	/// Current viewport size in pixels, or false when there is no viewport yet.
	bool GetViewportSize(FVector2D& OutSize) const;

	static constexpr int32 AbilityButtonCount = 5;

	/// The on-screen move stick's throw radius, in pixels. The controller maps
	/// a drag of this many pixels to a full-strength axis.
	static constexpr float MoveStickRadius = 130.f;

private:
	void DrawVitals();
	void DrawAbilityButtons();
	void DrawMoveStick();
	void DrawFloatingDamage();
	void DrawStatusLine();

	float BarWidth = 320.f;
	float BarHeight = 22.f;
	float ButtonSize = 84.f;
	float ButtonGap = 12.f;
	float Margin = 24.f;

	/// A number that floats upward from where a hit landed, then fades. The
	/// list is tiny and drained every frame, so a plain array is enough.
	struct FDamageNumber
	{
		FVector WorldLocation = FVector::ZeroVector;
		float Amount = 0.f;
		float Age = 0.f;
		bool bCritical = false;
	};

	TArray<FDamageNumber> DamageNumbers;
};
