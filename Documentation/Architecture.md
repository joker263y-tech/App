# Architecture

## The shape of the problem

This is a 3D action RPG targeting mid-range Android. The parts most likely to be
subtly wrong — damage maths, AI decisions, loot distribution, progression curves,
save compatibility — are exactly the parts that are hardest to check by playing.
A wrong armour formula does not crash; it quietly makes late-game combat trivial.
A mis-serialised save field does not crash; it loses a player's equipment three
hours in.

So the rules live in a place where they can be executed and asserted, and the
engine only does what only an engine can do: read input, draw, and play sound.

## Two assemblies, one compiler-enforced boundary

```
Assets/Scripts/Core/    Shadowbound.Core    game rules. Zero engine references.
Assets/Scripts/Game/    Shadowbound.Game    MonoBehaviours and presentation.
Assets/Scripts/Editor/  Shadowbound.Editor  menu tooling.
```

The boundary is not a convention that erodes over time. It is enforced in three
independent places:

1. **`Shadowbound.Core.asmdef` sets `"noEngineReferences": true`.** Unity will
   refuse to compile the core assembly the moment any file in it names a
   `UnityEngine` type. A developer cannot accidentally couple the rules to the
   engine; the build stops them.
2. **`Tools/check-core-purity.sh`** reproduces the same rule on the command line,
   so a violation fails before Unity is ever opened.
3. **`Tests/Shadowbound.Core.Build`** compiles the real core sources against
   `netstandard2.1` with `LangVersion 9.0` — Unity 6's actual API surface and
   language level. A C# 10 feature or a .NET-10-only method fails here rather
   than in the editor.

## Dependency direction

```
        Core  (no engine)
          ^
          |  referenced by
          |
     Game (Unity)  --->  UnityEngine, Input System, UGUI
          ^
          |
     Editor (Unity) --->  UnityEditor
```

Core never references upward. This is what makes the rules testable: the test
harness is just another consumer of Core, with no engine in the loop.

## The simulation model

`EncounterSimulation` is a fixed-timestep loop. Fixed, not variable, because
combat tuning is expressed in seconds and a variable step makes the same input
produce different outcomes on a fast and a slow device.

One step runs in a fixed, deliberate order:

1. Advance ability controllers; collect the blows that land this step.
2. Interrupt any cast whose caster was just staggered.
3. Advance status effects, regeneration and knockback decay.
4. Decide intent for every living combatant.
5. Apply movement and turning.
6. Resolve the blows collected in step 1, at the positions reached in step 5.

Resolving **after** movement is what makes a swing land where the target
actually is at the moment of impact, rather than where it was when the animation
started.

### One authority over position

The player and the enemies are different only in where their intent comes from.
`PlayerInputDriver` and `EnemyBrain` both implement `ICombatantDriver` and both
return a `CombatIntent`. The simulation cannot tell them apart. This is why:

- The player and enemies move, turn, attack and get staggered by identical code.
- An AI bug can be reproduced by replaying player input.
- Nothing in the Unity layer ever writes a position.

`CombatantView` copies `Combatant.Position` onto a transform and never assigns
it. The core is the only thing that decides where anything is.

### Determinism

Everything random comes from `DeterministicRng` (PCG32), and there is exactly
**one** stream per session — loot and combat share it. That is deliberate: two
streams would mean a save recorded only one of them, so loading would let combat
rolls replay values loot had already consumed and the two would silently diverge
from the run they were meant to reproduce.

Each enemy forks its own sub-stream from a **stable** hash of its id, so changing
one creature's behaviour cannot shift another's. The hash is FNV-1a written by
hand rather than `string.GetHashCode`, because .NET randomises string hashes per
process — using the built-in would mean the same seed played out differently on
every launch.

## Save format

Unity's `JsonUtility` is `UnityEngine`, so the core is not allowed to use it. The
core ships its own reader and writer instead, which has the useful side effect
that the save format is testable, versioned and inspectable.

```
SaveSerializer.Serialize(save)  ->  JSON text
SaveSlotManager                  ->  ISaveStorage (file, memory, or cloud)
SaveMigration.Migrate(save)      ->  upgrades an old file in memory
```

Migration runs on **load**, not on save. An old file stays on disk in its
original form until the player actually loads and re-saves it, so a failed
upgrade does not destroy the only copy.

`ISaveStorage` keeps the core free of file APIs. `FileSaveStorage` in the Game
assembly supplies the real implementation, writing to a temporary file and moving
it into place so an interrupted write cannot leave a truncated file where a
complete save used to be.

## Presentation is generated, not authored

There are no committed `.unity` scenes, `.prefab` files, or `.asset` materials.
They are fragile, unreviewable in a diff, and impossible to verify without
opening the editor.

Instead:

- `GameBootstrap` builds the whole game at runtime from primitives: session,
  arena, player, enemies, camera, HUD. Drop it on one GameObject and press Play.
- `ProjectSetup` (menu: **Shadowbound**) writes the scene file, registers it as
  the build scene, and configures the Android player.

The committed source of truth for configuration is code, where it can be
reviewed and reasoned about. Real art replaces the primitive shapes in
`GameBootstrap.CreateView`; nothing else has to change.

## Where new code goes

| You are adding | Put it in |
| --- | --- |
| A damage formula, an AI decision, a loot rule | `Core` — and test it |
| Reading input, drawing, sound, camera | `Game` |
| A menu item, an asset generator, a validator | `Editor` |

If you are tempted to put a rule in `Game` because it needs an engine type, the
rule almost certainly wants to be split: the decision belongs in `Core`, and the
engine type is an output of that decision.
