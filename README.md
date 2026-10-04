# SHADOWBOUND: THE LAST NIGHT

An original dark-fantasy 3D action RPG for Android. Third-person, real-time
combat, semi-open world, story-driven PvE.

> **Original work.** Inspired by the broad atmosphere and design principles of
> the dark-fantasy genre. All characters, factions, creatures, regions,
> mythology, terminology, dialogue and visual identity are original creations.
> Nothing is copied from any existing work. See
> [Documentation/Design.md](Documentation/Design.md).

---

## Engine

**Godot 4.5 (.NET / C#)** is the engine of this repository, targeting **Android
ARM64** (target API 35, landscape). This repository *is* the Godot project:
`project.godot` lives at the root.

The project has been migrated twice: **Unity 6 → Unreal Engine 5 → Godot 4.5**.
The game's rules were always engine-free, so each migration has been a re-hosting
of the presentation and input layers rather than a rewrite of the game. The
Unreal layer (`Shadowbound.uproject`, `Source/`, `Config/`) is gone; the original
Unity folders (`Assets/`, `Packages/`, `ProjectSettings/`) are gone too.

---

## Current status

| Area | State |
| --- | --- |
| Engine-free C# core (`Core/`) — combat, AI, items, quests, world, saves | **Done — 563 tests passing** |
| Godot game layer (`scripts/`, `scenes/`) — arena, player, enemies, camera, input, HUD, menu, saves | **Done — builds clean, runs headless** |
| Godot headless smoke test | **Passing** — RNG parity, session boot, a resolved fight, save round-trip |
| Android ARM64 APK | **Produced and validated locally** — see below |
| GitHub Actions CI | `ci.yml` runs the Godot gates; `android.yml` exports the ARM64 APK |

### Read this before assuming it works

The following are verified **by execution**, on a machine with no game engine
installed beyond Godot itself:

- **The game rules:** `bash Tools/test-core.sh` compiles the real core sources and
  runs **563 tests**.
- **The Godot assembly:** `bash Tools/test-godot.sh` builds `Shadowbound.csproj`,
  imports the project, and runs a headless smoke test **inside Godot** that proves
  the deterministic RNG parity, boots a session, resolves a real fight and
  round-trips a save.
- **The main scene:** running `scenes/Main.tscn` headless assembles the game —
  `Shadowbound ready: region 'grey-wilds', 5 hostiles, 5 quests, level 1`.
- **A real APK:** `bash Tools/build-android.sh` produced
  `build/android/shadowbound.apk` (98 MB) locally, containing
  `lib/arm64-v8a/libgodot_android.so`, `assets/.godot/mono/publish/arm64/Shadowbound.dll`
  and `Shadowbound.Core.dll`, with `package=com.shadowbound.thelastnight`,
  `targetSdkVersion=35` and `screenOrientation=landscape`, signed by Godot's debug
  keystore and verified with `apksigner`.

What is **not** verified: the APK has not been installed on a physical device, and
the GitHub Actions workflow has not yet run on GitHub (it was validated by running
the same script locally). `Documentation/Verification.md` states exactly what was
run and what was not.

### Commands to check what can be checked

```bash
bash Tools/check-core-purity.sh   # the core must stay engine-free (Godot/Unity/Unreal)
bash Tools/test-core.sh           # purity gate + 563 core tests
bash Tools/check-godot-project.sh # the Godot project layout is complete and engine-clean
bash Tools/test-godot.sh          # build the C# assembly + headless smoke test
bash Tools/build-android.sh       # export the Android ARM64 APK (needs Godot + Android SDK)
```

---

## Repository layout

```
project.godot            the Godot project descriptor (landscape, C#)
export_presets.cfg       the Android ARM64 export preset
Shadowbound.csproj       the Godot C# assembly (references the core)
Shadowbound.sln          required by Godot's .NET export to bundle the assembly
icon.svg                 the project icon
scenes/                  authored scenes: Main, Arena, Player, Enemy, Hud, GameMenu
scripts/                 the Godot game layer (C#): views, camera, input, HUD, menu, saves
Core/                    the engine-free C# game rules (the source of truth)
Tests/
  Shadowbound.Core.Tests/  xUnit suite for the core (563 tests)
  Shadowbound.Core.Build/  compiles Core/ as a portable netstandard2.1 library
  Godot/                   the headless Godot smoke test
Tools/                   command-line verification and build scripts
Documentation/           architecture, design, building, verification status
```

## The engine boundary

Game rules live in an **engine-free core**; the Godot layer only turns input into
intent and core state into transforms and UI. This is enforced, not conventional:

- `Tools/check-core-purity.sh` fails if the core names any Godot, Unity or Unreal
  API, branches on an engine define, or uses an engine inspector attribute.
- The core depends only on the .NET base class library, which is what lets it be
  compiled and tested with no engine at all.

The payoff: damage maths, AI decisions, loot rolls, progression curves and save
compatibility are verifiable without launching an editor. "This is done" is backed
by a test that actually ran.

The core simulates in its own coordinates (Y up, one unit = one metre, facing 0 =
+Z). Godot is also Y-up and in metres, so positions copy across component for
component; only rotation is converted (`scripts/CoordinateConvert.cs`). The Godot
layer **copies positions from the core — never writes back**. The simulation is the
single authority over where anything is.

See [Documentation/Architecture.md](Documentation/Architecture.md).

---

## Requirements

| Task | Needs |
| --- | --- |
| Run the core test suite | .NET SDK 9.0+ |
| Build and run the game | Godot 4.5 (.NET) |
| Produce an APK | Godot 4.5 (.NET) + Android SDK (build-tools 35.0.1, platform 35) + JDK 17 |

The core test suite needs no engine. Godot needs no Android SDK until you export.

## Documentation

| Document | Contents |
| --- | --- |
| [Architecture.md](Documentation/Architecture.md) | Module boundaries, the engine-free core, simulation model, determinism, coordinate conversion |
| [Design.md](Documentation/Design.md) | The original world, factions, creatures and abilities |
| [Building.md](Documentation/Building.md) | Setup, controls, Android build, troubleshooting |
| [Verification.md](Documentation/Verification.md) | **What has been executed and verified, and what has not** |
