#include "ShadowboundGameMode.h"

#include "Engine/World.h"
#include "GameFramework/PlayerController.h"

#include "SbCombatant.h"
#include "SbContent.h"
#include "SbDamage.h"
#include "SbEncounter.h"
#include "SbVitals.h"

#include "ShadowboundArena.h"
#include "ShadowboundCombatantActor.h"
#include "ShadowboundConvert.h"
#include "ShadowboundEnemyCharacter.h"
#include "ShadowboundGameInstance.h"
#include "ShadowboundHud.h"
#include "ShadowboundPlayerCharacter.h"
#include "ShadowboundPlayerController.h"

namespace
{
/// How long a defeated non-boss body stays on the ground before it is removed.
constexpr float CorpseLingerSeconds = 1.6f;

/// One creature in a region's spawn plan: archetype, position (core X/Z), level.
struct FSpawnEntry
{
	const TCHAR* Archetype;
	float X;
	float Z;
	int32 Level;
};

// The plans below are the Unity GameBootstrap plans, copied verbatim. Region
// travel is phase 2, but the data lives here already so the plan can be reused
// unchanged once the world graph is ported.
const FSpawnEntry WildsPlan[] = {
	{ TEXT("hollow-walker"), 8.f, 12.f, 1 },
	{ TEXT("hollow-walker"), -9.f, 14.f, 1 },
	{ TEXT("hollow-walker"), 0.f, 18.f, 2 },
	{ TEXT("cinder-hound"), 14.f, -6.f, 3 },
	{ TEXT("cinder-hound"), -14.f, -8.f, 3 },
};

const FSpawnEntry RuinsPlan[] = {
	{ TEXT("hollow-walker"), 6.f, 20.f, 4 },
	{ TEXT("hollow-walker"), -6.f, 20.f, 4 },
	{ TEXT("veilwarden"), 9.f, -20.f, 6 },
	{ TEXT("veilwarden"), -9.f, -20.f, 6 },
};

const FSpawnEntry WardPlan[] = {
	{ TEXT("cinder-hound"), 12.f, 8.f, 7 },
	{ TEXT("cinder-hound"), -12.f, 8.f, 7 },
	{ TEXT("veilwarden"), 0.f, -18.f, 8 },
};

const FSpawnEntry SanctumPlan[] = {
	{ TEXT("ashen-sentinel"), 0.f, -18.f, 10 },
};

struct FPlan
{
	const FSpawnEntry* Entries;
	int32 Count;
};

FPlan PlanForRegion(const FString& RegionId)
{
	if (RegionId == TEXT("grey-wilds"))
	{
		return { WildsPlan, UE_ARRAY_COUNT(WildsPlan) };
	}

	if (RegionId == TEXT("hollowed-ruins"))
	{
		return { RuinsPlan, UE_ARRAY_COUNT(RuinsPlan) };
	}

	if (RegionId == TEXT("sunken-ward"))
	{
		return { WardPlan, UE_ARRAY_COUNT(WardPlan) };
	}

	if (RegionId == TEXT("umbral-sanctum"))
	{
		return { SanctumPlan, UE_ARRAY_COUNT(SanctumPlan) };
	}

	// The camp and anything unknown spawn nothing: putting the wrong creatures
	// in an unrecognised region would be worse than putting none.
	return { nullptr, 0 };
}

FLinearColor TintFromArchetype(const ShadowboundCore::Content::EnemyArchetype& Archetype)
{
	return FLinearColor(Archetype.TintRgb[0], Archetype.TintRgb[1], Archetype.TintRgb[2]);
}
} // namespace

AShadowboundGameMode::AShadowboundGameMode()
{
	PrimaryActorTick.bCanEverTick = true;

	PlayerControllerClass = AShadowboundPlayerController::StaticClass();
	HUDClass = AShadowboundHud::StaticClass();

	// The player body is spawned by this class and possessed explicitly, so the
	// engine's default pawn spawning is switched off - a default pawn would be
	// a second body with a second authority over position.
	DefaultPawnClass = nullptr;
}

void AShadowboundGameMode::InitGame(const FString& MapName, const FString& Options, FString& ErrorMessage)
{
	Super::InitGame(MapName, Options, ErrorMessage);

	UShadowboundGameInstance* Game = GetShadowboundGameInstance();
	if (Game != nullptr)
	{
		// Build the playthrough as early as possible, so the world size and the
		// occlusion provider are settled before anything is spawned.
		Game->EnsurePlaythrough();

		Occlusion.World = GetWorld();
		if (Game->Encounter != nullptr)
		{
			Game->Encounter->SetOcclusion(&Occlusion);
		}
	}
}

void AShadowboundGameMode::StartPlay()
{
	Super::StartPlay();

	Assemble();
}

void AShadowboundGameMode::Assemble()
{
	UShadowboundGameInstance* Game = GetShadowboundGameInstance();
	if (Game == nullptr || !Game->IsPlaythroughReady())
	{
		return;
	}

	APlayerController* BaseController = GetWorld() != nullptr
		? GetWorld()->GetFirstPlayerController()
		: nullptr;

	AShadowboundPlayerController* Controller = Cast<AShadowboundPlayerController>(BaseController);

	// The driver reads its input from the controller; attach it now that the
	// controller exists.
	if (Game->PlayerDriver != nullptr)
	{
		Game->PlayerDriver->SetOwner(Controller);
	}

	FActorSpawnParameters Params;
	Params.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;

	if (UWorld* World = GetWorld())
	{
		Arena = World->SpawnActor<AShadowboundArena>(
			FVector::ZeroVector, FRotator::ZeroRotator, Params);
	}

	SpawnPlayerView();

	PopulateRegion(Game->StartingRegion);

	if (Controller != nullptr && PlayerView != nullptr)
	{
		Controller->Possess(PlayerView);
	}

	// Listen to the encounter only after the views exist, so the first events
	// always have somewhere to land.
	DamagedBinding = Game->Encounter->DamageDealt.Bind(this, &AShadowboundGameMode::HandleDamageThunk);
	DiedBinding = Game->Encounter->Died.Bind(this, &AShadowboundGameMode::HandleDeathThunk);
}

void AShadowboundGameMode::SpawnPlayerView()
{
	UShadowboundGameInstance* Game = GetShadowboundGameInstance();
	UWorld* World = GetWorld();
	if (Game == nullptr || Game->PlayerCombatant == nullptr || World == nullptr)
	{
		return;
	}

	FActorSpawnParameters Params;
	Params.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;

	PlayerView = World->SpawnActor<AShadowboundPlayerCharacter>(
		ShadowboundConvert::ToUnreal(Game->PlayerCombatant->Position()),
		FRotator::ZeroRotator, Params);

	if (PlayerView != nullptr)
	{
		// The Warden's placeholder tint and scale, from the Unity GameBootstrap.
		PlayerView->BindCombatant(Game->PlayerCombatant.get(), 1.05f,
			FLinearColor(0.86f, 0.78f, 0.62f));
	}
}

void AShadowboundGameMode::PopulateRegion(const FString& RegionId)
{
	UShadowboundGameInstance* Game = GetShadowboundGameInstance();
	UWorld* World = GetWorld();
	if (Game == nullptr || Game->Encounter == nullptr || World == nullptr)
	{
		return;
	}

	const FPlan Plan = PlanForRegion(RegionId);

	FActorSpawnParameters Params;
	Params.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;

	for (int32 Index = 0; Index < Plan.Count; ++Index)
	{
		const FSpawnEntry& Entry = Plan.Entries[Index];

		const ShadowboundCore::Content::EnemyArchetype* Archetype =
			ShadowboundCore::Content::FindArchetype(TCHAR_TO_UTF8(Entry.Archetype));
		if (Archetype == nullptr)
		{
			continue;
		}

		// Instance ids are namespaced by region, so the same archetype in two
		// regions gets two independent RNG streams rather than one shared roll.
		const FString InstanceId = RegionId + TEXT("/") + Entry.Archetype +
			TEXT("-") + FString::FromInt(Index);

		const ShadowboundCore::Float3 Position(Entry.X, 0.f, Entry.Z);

		std::unique_ptr<ShadowboundCore::Combatant> Combatant =
			Archetype->Create(TCHAR_TO_UTF8(*InstanceId), Position, Entry.Level);

		ShadowboundCore::Combatant* Raw = Combatant.get();
		Game->EnemyCombatants.push_back(std::move(Combatant));

		Game->Encounter->AddEnemy(Raw, Archetype->Abilities, Archetype->Brain,
			Archetype->AttackAbilityIndex);

		AShadowboundEnemyCharacter* View = World->SpawnActor<AShadowboundEnemyCharacter>(
			ShadowboundConvert::ToUnreal(Position), FRotator::ZeroRotator, Params);

		if (View != nullptr)
		{
			View->BindCombatant(Raw, Archetype->BodyScale, TintFromArchetype(*Archetype));
			EnemyViews.Add(View);
		}
	}
}

void AShadowboundGameMode::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);

	UShadowboundGameInstance* Game = GetShadowboundGameInstance();
	if (Game == nullptr || Game->Encounter == nullptr)
	{
		return;
	}

	// The simulation runs first: input becomes intent, intent becomes movement,
	// and blows land at the positions reached. Only then does anything copy the
	// result onto a transform. Views never write back to the core.
	Game->Encounter->Update(DeltaSeconds);

	if (PlayerView != nullptr)
	{
		PlayerView->SyncFromCore(DeltaSeconds);
	}

	for (TObjectPtr<AShadowboundEnemyCharacter>& View : EnemyViews)
	{
		if (View != nullptr)
		{
			View->SyncFromCore(DeltaSeconds);
		}
	}

	ExpireDeadViews(DeltaSeconds);
}

void AShadowboundGameMode::ExpireDeadViews(float DeltaSeconds)
{
	for (int32 Index = DeadViews.Num() - 1; Index >= 0; --Index)
	{
		DeadTimers[Index] -= DeltaSeconds;

		if (DeadTimers[Index] > 0.f)
		{
			continue;
		}

		if (AShadowboundCombatantActor* View = DeadViews[Index])
		{
			if (AShadowboundEnemyCharacter* Enemy = Cast<AShadowboundEnemyCharacter>(View))
			{
				EnemyViews.Remove(Enemy);
			}

			View->Destroy();
		}

		DeadViews.RemoveAt(Index);
		DeadTimers.RemoveAt(Index);
	}
}

void AShadowboundGameMode::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	UShadowboundGameInstance* Game = GetShadowboundGameInstance();

	if (Game != nullptr && Game->Encounter != nullptr)
	{
		// The encounter outlives this game mode, so the handlers must come off
		// with it. A stale `this` firing next frame would be a use-after-free.
		Game->Encounter->DamageDealt.Unbind(DamagedBinding);
		Game->Encounter->Died.Unbind(DiedBinding);
	}

	Super::EndPlay(EndPlayReason);
}

void AShadowboundGameMode::HandleDamageThunk(void* Context,
	ShadowboundCore::Participant* /*Attacker*/, ShadowboundCore::Combatant* Victim,
	const ShadowboundCore::DamageResult& Result)
{
	static_cast<AShadowboundGameMode*>(Context)->OnDamage(Victim, Result);
}

void AShadowboundGameMode::HandleDeathThunk(void* Context, ShadowboundCore::Combatant* Victim,
	ShadowboundCore::Participant* /*Participant*/)
{
	static_cast<AShadowboundGameMode*>(Context)->OnDeath(Victim);
}

void AShadowboundGameMode::OnDamage(ShadowboundCore::Combatant* Victim,
	const ShadowboundCore::DamageResult& Result)
{
	if (Victim == nullptr || Result.IsZero())
	{
		return;
	}

	if (AShadowboundHud* Hud = GetShadowboundHud())
	{
		// Above the body, so the number does not sit inside the mesh.
		const FVector Location = ShadowboundConvert::ToUnreal(Victim->Position())
			+ FVector(0.f, 0.f, 180.f);

		Hud->ReportDamage(Location, Result.Applied, Result.Critical);
	}
}

void AShadowboundGameMode::OnDeath(ShadowboundCore::Combatant* Victim)
{
	if (Victim == nullptr)
	{
		return;
	}

	// A boss is the centre of the fight; its body stays. The player is
	// persistent: destroying its body would remove the camera and leave the
	// game unplayable, so it is left where it fell (phase 1 has no respawn; the
	// core's reset path is the seed for one).
	if (Victim->IsBoss || Victim->IsPersistent)
	{
		return;
	}

	AShadowboundCombatantActor* View = FindView(Victim);
	if (View == nullptr || DeadViews.Contains(View))
	{
		return;
	}

	DeadViews.Add(View);
	DeadTimers.Add(CorpseLingerSeconds);
}

AShadowboundCombatantActor* AShadowboundGameMode::FindView(ShadowboundCore::Combatant* Combatant) const
{
	if (PlayerView != nullptr && PlayerView->GetCombatant() == Combatant)
	{
		return PlayerView;
	}


	for (const TObjectPtr<AShadowboundEnemyCharacter>& View : EnemyViews)
	{
		if (View != nullptr && View->GetCombatant() == Combatant)
		{
			return View;
		}
	}

	return nullptr;
}

AShadowboundHud* AShadowboundGameMode::GetShadowboundHud() const
{
	if (UWorld* World = GetWorld())
	{
		if (APlayerController* Controller = World->GetFirstPlayerController())
		{
			return Cast<AShadowboundHud>(Controller->GetHUD());
		}
	}

	return nullptr;
}

UShadowboundGameInstance* AShadowboundGameMode::GetShadowboundGameInstance() const
{
	return GetWorld() != nullptr
		? GetWorld()->GetGameInstance<UShadowboundGameInstance>()
		: nullptr;
}
