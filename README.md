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

**Unreal Engine 5.6** is the primary engine of this repository, targeting
**Android ARM64**. This repository *is* the Unreal project: `Shadowbound.uproject`
and `Source/` live at the root, and there is no separate engine folder.

The project was previously a Unity 6 project. It has been migrated to Unreal; the
full audit, the system-by-system mapping and the phase plan are in
[MIGRATION_PLAN.md](MIGRATION_PLAN.md). The Unity assets, packages and project
settings have been removed.

---

## Current status

| Area | State |
| --- | --- |
| Unreal project (`Shadowbound.uproject`, `Source/`, `Config/`) | Written — **never compiled** (no Unreal Engine in the build environment) |
| Engine-free C# core (`Core/`) — combat, AI, items, quests, world, saves | **Done — 563 tests passing** |
| Engine-free C++ core (`Source/ShadowboundCore/`) — ported combat/encounter slice | **Done — 82 tests passing** |
| Unreal game layer (`Source/Shadowbound/`) — arena, player, enemies, HUD, input, game mode | Written — **never compiled or run** |
| Android build (RunUAT `BuildCookRun`) | Scripted (`Tools/build-android.sh`) — **no APK has been produced** |
| GitHub Actions CI | `verify` gates run; the `android` job builds only where Unreal Engine is available |

### Read this before assuming it works

Two things are verified **by execution**, here, on a machine with no game engine
installed:

- The C# core rules: `bash Tools/test-core.sh` compiles the real sources and runs
  **563 tests** against them.
- The C++ core rules: `bash Tools/test-core-cpp.sh` compiles them with a plain
  C++ compiler (`-std=c++17 -Wall -Wextra -Werror`) and runs **82 tests**. The two
  implementations are proven to agree on the RNG stream and the stable hash.

The **Unreal game layer has not been compiled or run.** No Unreal Engine is
installed in the environment this was written in, so there is no `.uproject`
compile, no Play session, no APK, and nothing rendered. `Tools/build-android.sh`
reports this honestly and exits non-zero rather than pretending otherwise.

`Documentation/Verification.md` states exactly what was run and what was not.
Read it before trusting anything here.

### Commands to check what can be checked

```bash
bash Tools/test-core.sh          # C# purity gate + 563 tests
bash Tools/test-core-cpp.sh      # C++ purity gate + 82 tests (no engine needed)
bash Tools/check-core-purity.sh  # both cores must stay engine-free
bash Tools/check-unreal-layout.sh# the Unreal project layout is complete & Unity-free
bash Tools/build-android.sh      # RunUAT BuildCookRun; fails honestly without UE
```

---

## Repository layout

```
Shadowbound.uproject     the Unreal project descriptor
Config/                  DefaultEngine.ini (Android, GameMode), DefaultInput.ini, DefaultGame.ini
Source/
  ShadowboundCore/       engine-free C++ game logic (standard library only)
  Shadowbound/           the Unreal game layer: actors, controller, HUD, game mode
Core/                    the original engine-free C# core (kept until each slice is ported)
Tests/
  Shadowbound.Core.Tests/     xUnit suite for the C# core (563 tests)
  Shadowbound.Core.Build/     compiles Core/ under Unity-6-era constraints (C# 9, netstandard2.1)
  ShadowboundCore.Cpp/        the C++ core test suite (82 tests)
Tools/                   command-line verification and build scripts
Documentation/           architecture, design, building, verification status
MIGRATION_PLAN.md        the audit and plan this migration follows
```

## The engine boundary

Game rules live in an **engine-free core**; the engine layer only turns input into
intent and core state into transforms, VFX and audio. This is enforced, not
conventional:

- `Tools/check-core-purity.sh` fails if the C# core names any Unity API, or the
  C++ core includes anything other than its own `Sb*.h` headers (or uses Unreal
  reflection macros). The one exception is `ShadowboundCoreModule.cpp`, which
  exists only to register the module and is excluded from the standalone build.
- The C++ core depends only on the C++ standard library, which is what lets it be
  compiled and tested by `Tools/test-core-cpp.sh` **without Unreal Engine**.

The payoff: damage maths, AI decisions, loot rolls, progression curves and save
compatibility are verifiable without launching an editor. "This is done" is
backed by a test that actually ran.

The core simulates in its own coordinates (Y up, one unit = one metre). The
Unreal layer converts through `Source/Shadowbound/Public/ShadowboundConvert.h`,
and **copies positions from the core — never writes back**. The simulation is the
single authority over where anything is.

See [Documentation/Architecture.md](Documentation/Architecture.md).

---

## Requirements

| Task | Needs |
| --- | --- |
| Run the core test suites | .NET SDK 8.0+ (C#) and a C++17 compiler (C++) |
| Open, play and build the game | Unreal Engine 5.6 with Android platform support |
| Produce an APK | Unreal Engine 5.6 + Android SDK/NDK/JDK |

The core test suites need no Unreal Engine. Unreal Engine needs no .NET SDK.

## Documentation

| Document | Contents |
| --- | --- |
| [Architecture.md](Documentation/Architecture.md) | Module boundaries, the engine-free core, simulation model, determinism, coordinate conversion |
| [Design.md](Documentation/Design.md) | The original world, factions, creatures and abilities |
| [Building.md](Documentation/Building.md) | Setup, controls, Android build, troubleshooting |
| [Verification.md](Documentation/Verification.md) | **What has been executed and verified, and what has not** |
| [MIGRATION_PLAN.md](MIGRATION_PLAN.md) | The Unity → Unreal audit, mapping and phase plan |
