# SHADOWBOUND: THE LAST NIGHT

An original dark-fantasy 3D action RPG for Android. Third-person, real-time
combat, semi-open world, story-driven PvE.

> **Original work.** Inspired by the broad atmosphere and design principles of
> the dark-fantasy genre. All characters, factions, creatures, regions,
> mythology, terminology, dialogue and visual identity are original creations.
> Nothing is copied from any existing work. See
> [Documentation/Design.md](Documentation/Design.md).

---

## Current status

| Area | State |
| --- | --- |
| Repository + Unity 6 project scaffolding | Done |
| Pure-C# game-logic core (combat, AI, items, quests, world, saves) | **Done — 563 tests passing** |
| Headless encounter + session integration | Done, tested |
| Authored content (creatures, items, quests, chapters, regions) | Done — validated by a tested validator |
| Unity layer (input, camera, views, HUD, menu, saves) | **Type-checks against real Unity assemblies** — never executed |
| In-game menu (equipment, consumables, attributes, save/load, resume) | Written — never executed |
| Editor tooling (scene setup, Android config) | Written — **not compiled** (no UnityEditor reference assembly) |
| Android APK | **Not produced** — requires Unity with the Android module |

### Read this before assuming it works

The **game rules are verified by execution**: `Tools/test-core.sh` compiles the
real core sources under Unity 6's exact constraints and runs 563 tests against
them. That part is not a claim, it is an observation.

The **Game assembly is verified to compile**, against real Unity reference
assemblies, with 0 errors and 0 warnings. Type-checking it for the first time
immediately found genuine compile errors a hand review had missed — a member
declared as both a field and a method, and a property shadowing
`System.IO.Directory`.

An audit against the standard "is the runtime path actually connected?" found
seven things that were fully built, fully tested, and **completely unreachable
while playing**: quest rewards were never granted and the story could not advance
past its first quest; no item could ever be equipped; the world's regions could
not be travelled to at all, so two quest objectives were in places the player
could not go; the menu was keyboard-only on a platform with no keyboard;
consumables could never be used; attribute points could never be spent (and would
have vanished on reload even if they could); and the menu drew most of its rows
below the edge of the screen. All seven are fixed, and the audits that found them
— including further defects the travel and save tests then exposed — are written
up in `Documentation/Verification.md`.

**Nothing has been executed, seen, or installed.** There is no Unity in the
environment this was built in. No APK exists. Nothing visual has been rendered,
and the Editor assembly has not been compiled at all.

`Documentation/Verification.md` states exactly what was run and what was not, and
lists the defects each check caught. Read it before trusting anything here.

### Three commands to check what can be checked

```bash
bash Tools/test-core.sh         # purity gate + 563 tests
bash Tools/check-syntax.sh      # every C# file parses at C# 9
bash Tools/check-unity-layer.sh # Game assembly type-checks against Unity
```

```
check-core-purity: OK (core is engine-free)
Passed!  - Failed: 0, Passed: 563, Skipped: 0, Total: 563
check-syntax: OK (47 files parse as C# 9, no syntax errors)
Build succeeded.  0 Warning(s)  0 Error(s)
```

---

## Getting it running

```bash
# 1. Verify everything that can be verified without an engine.
bash Tools/test-core.sh            # core: purity, Unity constraints, 563 tests
bash Tools/check-syntax.sh         # all C# parses at Unity's language level
bash Tools/check-unity-layer.sh    # Game assembly type-checks against Unity
```

```
# 2. Open this folder in Unity 6 (6000.0 LTS+), then run the menu item:
#      Shadowbound -> Set Up Project
#    It creates the scene, registers it for the build, configures the Android
#    player, and validates the content.
#
# 3. Press Play.      Keyboard + mouse in the Editor.
#    Build the APK:    Shadowbound -> Build Android APK
```

Full instructions, controls and troubleshooting:
[Documentation/Building.md](Documentation/Building.md).

---

## Why the code is split in two

The project is divided into a **pure C# core** and a **thin Unity layer**. This
is an architectural decision, not a stylistic one.

```
Assets/Scripts/Core/   ->  game rules. No UnityEngine. Fully testable.
Assets/Scripts/Game/   ->  MonoBehaviours. Turn input into core intent,
                           and core output into transforms, VFX and audio.
Assets/Scripts/Editor/ ->  tooling: scene setup, Android config, validation.
```

The boundary is **enforced by the compiler**, in three independent places:

- `Shadowbound.Core.asmdef` sets `"noEngineReferences": true`. Unity refuses to
  compile the core if any file touches `UnityEngine`.
- `Tools/check-core-purity.sh` reproduces that rule on the command line, so a
  violation fails before Unity is opened.
- `Tests/Shadowbound.Core.Build` compiles the core sources against
  `netstandard2.1` with `LangVersion 9.0` — Unity 6's exact API surface and
  language level. C# 10 syntax or a .NET-10-only API fails here instead of
  failing later in the editor.

The payoff: damage maths, AI decisions, loot rolls, progression curves and save
compatibility are verifiable without launching Unity. "This feature is done" can
be backed by a test that actually ran.

Core never references the engine, and the engine is never the only authority over
game state — the simulation owns every position, and the Unity layer only copies.
See [Documentation/Architecture.md](Documentation/Architecture.md).

---

## Requirements

- **Unity 6** (6000.0 LTS or newer) — to open, play and build
- **Unity Android Build Support** (SDK, NDK, JDK) — to produce an APK
- **.NET SDK 8.0+** — only to run the core test suite

The test suite needs no Unity. Unity needs no .NET SDK.

---

## Repository layout

```
Assets/
  Scripts/Core/        pure C# game logic — no engine references
  Scripts/Game/        Unity layer: input, camera, views, HUD, save storage
  Scripts/Editor/      scene setup, Android configuration, content validation
  Scenes/              generated by Shadowbound -> Set Up Project
Packages/              Unity package manifest (URP, Input System, UGUI)
ProjectSettings/       Unity project configuration
Tools/                 command-line verification scripts
Tests/                 .NET projects: core tests, plus a Unity-layer type-check
Documentation/         architecture, design, building, verification status
Builds/                APK output (git-ignored)
```

## Note on committed configuration

Only `ProjectSettings/ProjectVersion.txt` is hand-authored. Everything else in
`ProjectSettings/` is generated by Unity on first open, and the settings that
actually matter are applied by the editor tooling.

No `.unity` scenes, `.prefab` files or `.asset` materials are committed. They are
fragile, unreviewable in a diff, and impossible to verify without the editor.
Instead `GameBootstrap` assembles the whole game at runtime from primitives —
session, arena, player, enemies, camera, HUD — so the scene on disk stays almost
empty and nothing can drift out of sync with the code. Real art replaces the
primitive shapes in `GameBootstrap.CreateView`; no game rule changes.

## Documentation

| Document | Contents |
| --- | --- |
| [Architecture.md](Documentation/Architecture.md) | Assembly boundaries, simulation model, determinism, save format |
| [Design.md](Documentation/Design.md) | The original world, factions, creatures and abilities |
| [Building.md](Documentation/Building.md) | Setup, controls, Android build, troubleshooting |
| [Verification.md](Documentation/Verification.md) | **What has been executed and verified, and what has not** |
