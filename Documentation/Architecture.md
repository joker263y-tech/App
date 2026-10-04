# Architecture

## The shape of the problem

This is a 3D action RPG targeting mid-range Android. The parts most likely to be
subtly wrong — damage maths, AI decisions, loot distribution, progression curves,
save compatibility — are exactly the parts that are hardest to check by playing.
A wrong armour formula does not crash; it quietly makes late-game combat trivial.

So the rules live in a place where they can be executed and asserted, and the
engine only does what only an engine can do: read input, draw, and play sound.

## Three engines, one core

The project was a Unity 6 project, then Unreal Engine 5, and is now **Godot 4.5**.
The rules were always engine-free, so each migration re-hosted the presentation and
input layers instead of rewriting the game.

```
Core/                 the engine-free C# rules (all systems — the source of truth)
scripts/              the Godot game layer (C#): views, camera, input, HUD, menu, saves
scenes/               the authored Godot scenes
Shadowbound.csproj    compiles scripts/ and references the core
```

The core is written in C#, which Godot runs directly (Godot 4 supports C# on
desktop and Android). This is why the migration reused 12,000 lines of tested game
rules instead of porting them again: the core was already engine-free and already
C#, so it only had to be referenced by the Godot assembly.

## The boundary rule

**Game rules never touch the engine.**

`Tools/check-core-purity.sh` fails if anything under `Core/` names a Godot, Unity
or Unreal API, branches on a `UNITY_`/`GODOT` define, or uses an engine inspector
attribute. The core depends only on the .NET base class library, which is what lets
`Tools/test-core.sh` compile and run the rules **with no engine installed**.

The Godot layer may reference the core. The core references upward to nothing.

```
  Shadowbound  (Godot C#: scripts/, scenes/)  --->  Shadowbound.Core  (engine-free C#)
```

## The simulation model

`Shadowbound.Core.Simulation.EncounterSimulation` is a fixed-timestep loop. Fixed,
not variable, because combat tuning is expressed in seconds and a variable step
makes the same input produce different outcomes on a fast and a slow device. Godot
runs the step from `_PhysicsProcess`, so each physics tick advances the simulation
by a fixed amount and the line-of-sight raycasts happen inside a valid physics pass.

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
`PlayerDriver` (input) and `EnemyBrain` both implement `ICombatantDriver` and both
return a `CombatIntent`. The simulation cannot tell them apart. This is why:

- The player and enemies move, turn, attack and get staggered by identical code.
- An AI bug can be reproduced by replaying player input.
- Nothing in the Godot layer ever writes a position.

`CombatantView` copies the core's position and facing onto its node every physics
step and never assigns them back. Its node carries no collision body, because a
physics body would be a second, conflicting authority over where the Warden is.
Arena walls and pillars are `StaticBody3D` on the "world" layer — they exist for the
line-of-sight raycast, not to push bodies around.

### Determinism

Everything random comes from `DeterministicRng` (PCG32), and there is exactly
**one** stream per session — loot and combat share it. That is deliberate: two
streams would mean a save recorded only one of them, so loading would let combat
rolls replay values loot had already consumed.

Each enemy forks its own sub-stream from a **stable** hash of its id, so changing
one creature's behaviour cannot shift another's. The hash is FNV-1a written by hand
rather than a language built-in, because several runtimes randomise string hashes
per process — using one would mean the same seed played out differently on every
launch.

## Coordinates: where the two worlds meet

The core simulates in its own coordinates: **Y up, facing 0 = +Z**, one unit = one
metre. Godot is **Y up**, also in metres, so positions map one-to-one — there is no
scale factor and no axis swap:

```
ToGodot(Float3 v) = Vector3(v.X, v.Y, v.Z)
```

Rotation is the only conversion. A Godot node's forward is `-Z` while the core's
facing 0 points at `+Z`, so a core facing maps to a Godot Y rotation of
`facing + 180°`. `scripts/CoordinateConvert.cs` is the only place that converts, and
the Godot layer only converts *from* the core.

## Save format

The core ships its own JSON reader and writer, so the save format is testable,
versioned and inspectable:

```
SaveSerializer.Serialize(save)  ->  JSON text
SaveSlotManager                  ->  ISaveStorage (file, memory, or cloud)
SaveMigration.Migrate(save)      ->  upgrades an old file in memory
```

Migration runs on **load**, not on save, so a failed upgrade does not destroy the
only copy. `scripts/GodotSaveStorage.cs` implements `ISaveStorage` on Godot's
`user://saves/`, and writes to a temporary file which is then moved into place, so a
crash mid-write cannot truncate a good save.

## Presentation: authored scenes, generated placeholder art

The scene graph (environment, key light, arena, view container, camera rig, input
reader, HUD, menu) is **authored** in `scenes/Main.tscn` and its children — the
Godot-native way to compose a game. The placeholder *art* is generated at runtime
(boxes and capsules from Godot's primitive meshes, tinted per archetype), so the
committed source of truth for the layout stays reviewable and no binary mesh can
drift out of step with the numbers the core is tuned against. Real art replaces the
placeholders; no game rule changes.

`scripts/Arena.cs` builds the floor, walls, line-of-sight pillars and the gate.
`scripts/Hud.cs` draws the HUD and the on-screen controls directly with Godot
`Control` primitives, and **hit-tests touches against the same rectangles it draws**,
so a button can never be somewhere other than where it looks. `scripts/GameMenu.cs`
uses real `Button` nodes and a pooled, paged row list.

## Where new code goes

| You are adding | Put it in |
| --- | --- |
| A damage formula, an AI decision, a loot rule | `Core/` — and test it in `Tests/Shadowbound.Core.Tests` |
| Reading input, drawing, sound, camera, node lifecycle | `scripts/` |
| A new scene or UI surface | `scenes/` |

If you are tempted to put a rule in the Godot layer because it needs an engine type,
the rule almost certainly wants to be split: the decision belongs in the core, and
the engine type is an output of that decision.
