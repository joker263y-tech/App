#include "ShadowboundGameInstance.h"

#include "SbContent.h"
#include "SbRng.h"

void UShadowboundGameInstance::Init()
{
	Super::Init();

	// The playthrough itself is created lazily by the game mode's InitGame,
	// because that is the first moment a world exists to put it in.
}

void UShadowboundGameInstance::EnsurePlaythrough()
{
	if (IsPlaythroughReady())
	{
		return;
	}

	using namespace ShadowboundCore;

	PlayerCombatant = Content::CreatePlayer();
	PlayerAbilities = Content::BuildPlayerAbilities();

	// The Warden walks in from the south, facing into the region - the Unity
	// build's Arrive() placed the player the same way when a region opened.
	PlayerCombatant->SetPosition(Float3(0.f, 0.f, -(ArenaHalfExtent - 8.f)));
	PlayerCombatant->FaceImmediately(Float3(0.f, 0.f, 1.f));

	// The driver's controller is attached later, when the game mode possesses
	// the player, because no controller exists while the playthrough is built.
	PlayerDriver = std::make_unique<FSbPlayerDriver>(nullptr);

	// One RNG stream for the whole session: loot and combat share it, exactly
	// as the C# GameSession did - two streams would diverge after a load.
	Encounter = std::make_unique<EncounterSimulation>(
		DeterministicRng(static_cast<std::uint64_t>(Seed)),
		WorldBounds::Square(ArenaHalfExtent));

	// The player and the enemies share one movement path; the only difference
	// is what drives them.
	Encounter->AddDriven(PlayerCombatant.get(), PlayerAbilities, PlayerDriver.get(),
		/*isPlayer*/ true);
}
