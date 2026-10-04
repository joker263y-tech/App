# Architecture

## The shape of the problem

This is a 3D action RPG targeting mid-range Android. The parts most likely to be
subtly wrong — damage maths, AI decisions, loot distribution, progression curves,
save compatibility — are exactly the parts that are hardest to check by playing.
A wrong armour formula does not crash; it quietly makes late-game combat trivial.

So the rules live in a place where they can be executed and asserted, and the
engine only does what only an engine can do: read input, draw, and play sound.

## The migration, and why there are two cores

The project was a Unity 6 project and is being migrated to **Unreal Engine 5**.
The rules were always engine-free, so the migration is a re-hosting of the
presentation and input layers, not a rewrite of the game.

Because the port is done in reviewed slices rather than as one big-bang rewrite,
both cores exist for now:

```
Core/                       the original engine-free C# rules (all systems)
Source/ShadowboundCore/     the ported engine-free C++ rules (encounter slice)
Source/Shadowbound/         the Unreal game layer (was Assets/Scripts/Game)
```

`MIGRATION_PLAN.md` is the authoritative list of what is ported and what is not.
As each slice is ported, its C# original and tests stay until the C++ slice and
its tests are in place; then the C# can be retired.

## Two boundaries, one rule

The rule is simple: **game rules never touch the engine.**

- **C++ (`Source/ShadowboundCore`).** Depends only on the C++ standard library.
  `Tools/check-core-purity.sh` fails if any file includes anything other than a
  local `Sb*.h` header, or uses Unreal reflection macros. The single exception is
  `ShadowboundCoreModule.cpp`, which exists only to register the module and is
  excluded from the standalone build. This is what lets `Tools/test-core-cpp.sh`
  compile and run the rules **with no Unreal Engine installed**.
- **C# (`Core/`).** The original boundary, still enforced: no `UnityEngine`, no
  `UNITY_` conditionals, no inspector attributes.

The engine layer (`Source/Shadowbound`) may reference both the engine and the core.
The core references upward to nothing.

## Module dependency direction

```
  ShadowboundCore   (engine-free C++: standard library only)
        ^
        |  PublicDependencyNames
        |
    Shadowbound     (Unreal: Core, CoreUObject, Engine, InputCore, EnhancedInput)
```

`ShadowboundCore.Build.cs` depends on `Core` for exactly one reason — module
registration — and no file but the registration file may include an engine header.

## The simulation model

`ShadowboundCore::EncounterSimulation` is a fixed-timestep loop. Fixed, not
variable, because combat tuning is expressed in seconds and a variable step makes
the same input produce different outcomes on a fast and a slow device. Each frame
an accumulator runs as many fixed steps as the elapsed time allows (capped, so a
hitch cannot spiral).

One step runs in a fixed, deliberate order:

1. Advance ability controllers; collect the blows that land this step.
2. Interrupt any cast whose caster was just staggered.
3. Advance status effects, regeneration and knockback decay.
4. Decide intent for every living combatant.
5. Apply movement and turning.
6. Resolve the blows collected in step 1, at the positions reached in step 5.

Resolving **after** movement is what makes a swing land where the target actually
is at the moment of impact, rather than where it was when the animation started.

### One authority over position

The player and the enemies are different only in where their intent comes from.
`FSbPlayerDriver` (input) and `EnemyBrain` both implement `ICombatantDriver` and
both return a `CombatIntent`. The simulation cannot tell them apart. This is why:

- The player and enemies move, turn, attack and get staggered by identical code.
- An AI bug can be reproduced by replaying player input.
- Nothing in the Unreal layer ever writes a position.

`AShadowboundCombatantActor` copies the core's position and facing onto its
transform every frame and never assigns them back. Its capsule is `NoCollision`,
because a physics body would be a second, conflicting authority over where the
Warden is. The core is the only thing that decides.

### Determinism

Everything random comes from `DeterministicRng` (PCG32), and there is exactly
**one** stream per session — loot and combat share it. That is deliberate: two
streams would mean a save recorded only one of them, so loading would let combat
rolls replay values loot had already consumed.

Each enemy forks its own sub-stream from a **stable** hash of its id, so changing
one creature's behaviour cannot shift another's. The hash is FNV-1a written by
hand rather than a language built-in, because several runtimes randomise string
hashes per process — using one would mean the same seed played out differently on
every launch. The C# and C++ implementations are byte-for-byte the same arithmetic
and are verified to agree (see `Verification.md`).

## Coordinates: where the two worlds meet

The core simulates in its own coordinates: **Y up, facing 0 = +Z**, one unit = one
metre. Unreal is **Z up, X forward, yaw 0 = +X**, one unit = one centimetre.

`Source/Shadowbound/Public/ShadowboundConvert.h` is the only place that converts:

```
ToUnreal(Float3 v)  =  FVector(v.Z, v.X, v.Y) * 100
ToCore(FVector v)   =  Float3(v.Y/100, v.Z/100, v.X/100)
```

The mapping is a proper rotation (determinant +1) times the unit scale, with two
useful consequences:

- **Core facing degrees are already Unreal yaw**, so no rotation conversion is
  needed — `FacingToRotator(f) = FRotator(0, f, 0)`.
- The transform round-trips exactly.

Nothing outside that header converts coordinates, and the engine layer only
converts *from* the core.

## Save format

The C# core ships its own JSON reader and writer, because Unity's `JsonUtility` was
an engine type and the core could not use it. The useful side effect is that the
save format is testable, versioned and inspectable.

```
SaveSerializer.Serialize(save)  ->  JSON text
SaveSlotManager                  ->  ISaveStorage (file, memory, or cloud)
SaveMigration.Migrate(save)      ->  upgrades an old file in memory
```

Migration runs on **load**, not on save, so a failed upgrade does not destroy the
only copy. Save serialisation is a later migration phase; the C# implementation of
it is unchanged and still tested.

## Presentation is generated, not authored

There are no committed `.umap` scenes or authored meshes. They are fragile,
unreviewable in a diff, and impossible to verify without opening the editor.

Instead `AShadowboundGameMode` builds the whole game at runtime from engine
primitives: arena, player, enemies, HUD. `AShadowboundPlayerController` builds its
Enhanced Input mapping context and actions in C++, so no `.uasset` input assets are
committed either. The committed source of truth for configuration is code, where
it can be reviewed and reasoned about. Real art replaces the placeholder meshes; no
game rule changes.

## Where new code goes

| You are adding | Put it in |
| --- | --- |
| A damage formula, an AI decision, a loot rule | `Source/ShadowboundCore` (C++) — and test it in `Tests/ShadowboundCore.Cpp` |
| Reading input, drawing, sound, camera, actor lifecycle | `Source/Shadowbound` |
| A rule that already exists in C# and is not yet ported | Port it to `Source/ShadowboundCore` and port its tests too |

If you are tempted to put a rule in the Unreal layer because it needs an engine
type, the rule almost certainly wants to be split: the decision belongs in the
core, and the engine type is an output of that decision.
