# Building and running

## Requirements

| Task | Needs |
| --- | --- |
| Run the C# core test suite | .NET SDK 8.0 or newer |
| Run the C++ core test suite | A C++17 compiler (`g++` or `clang++`) |
| Open, play and build the game | **Unreal Engine 5.6** with the Android platform installed |
| Produce an APK | Unreal Engine 5.6 + Android SDK/NDK/JDK |

The core test suites need no Unreal Engine. Unreal Engine needs no .NET SDK. They
are independent.

## 1. Run the checks (no Unreal Engine required)

```bash
bash Tools/test-core.sh           # C# purity gate, compile, 563 tests
bash Tools/test-core-cpp.sh       # C++ purity gate, compile, 82 tests
bash Tools/check-core-purity.sh   # both cores must stay engine-free
bash Tools/check-unreal-layout.sh # project layout is complete and Unity-free
```

`test-core.sh` runs the purity gate, compiles the C# core against `netstandard2.1`
with C# 9 — the constraints the project originally targeted — and runs the xUnit
suite.

`test-core-cpp.sh` runs the purity gate, builds `Source/ShadowboundCore` (except
the module-registration file) with `g++ -std=c++17 -Wall -Wextra -Werror`, and runs
the C++ suite. It needs nothing but a C++ compiler, which is the point of keeping
the core engine-free.

Expected output:

```
check-core-purity: OK (C# core in Core/ and C++ core in Source/ShadowboundCore are engine-free)
Passed!  - Failed: 0, Passed: 563, Skipped: 0, Total: 563
Passed!  - Failed: 0, Passed: 82, Skipped: 0, Total: 82
check-unreal-layout: OK (Unreal project layout is complete and Unity-free)
```

## 2. Open the project in Unreal Engine

1. Install **Unreal Engine 5.6** with the **Android** platform (Epic Games
   Launcher → Android platform components).
2. Right-click `Shadowbound.uproject` → **Generate Visual Studio project files**
   (or use your IDE's equivalent on Linux/macOS).
3. Build the C++ modules, then open `Shadowbound.uproject`.
4. Press **Play**.

There is no authored map. `Config/DefaultEngine.ini` boots the engine's empty
`/Engine/Maps/Entry`, and `AShadowboundGameMode` builds the whole game at runtime
in code: arena, player, enemies, HUD. This is deliberate — the committed source of
truth for the game's construction stays reviewable in a diff, and no binary scene
can drift out of step with the code. Real art replaces the placeholder engine
meshes; no game rule changes.

### What boots

The game opens in the **Grey Wilds**, the first combat region: three Hollow
Walkers and two Cinder Hounds in the arena. Region travel is a later migration
phase (see `MIGRATION_PLAN.md`), so the safe hub and the other regions are not
reachable yet.

## 3. Controls

### Keyboard and mouse

| Input | Action |
| --- | --- |
| `W` `A` `S` `D` / left stick | Move, relative to the camera |
| Mouse / right stick | Look |
| `Q` / `E` | Turn the camera |
| Left mouse (hold) | Ember Edge |
| `1` – `5` | Abilities 0–4 |
| `Space` | Ashstep |

Movement is camera-relative: forward moves the Warden away from the camera, not
along a fixed world axis.

### Touch (Android)

| Control | Action |
| --- | --- |
| Left half of the screen | Movement stick, centred wherever your thumb lands |
| Right half | Drag to look |
| Bottom-right buttons | Abilities 1–5, each showing its cooldown as a fill |

The stick is anchored to the touch point rather than a fixed spot, because a fixed
position is unusable for anyone holding the device differently. The same
rectangles are used to draw the buttons and to hit-test touches — one source of
truth, in code (there is no UMG widget tree).

## 4. Build the Android APK

The build is driven entirely from the command line, without opening the editor:

```bash
bash Tools/build-android.sh            # archive to Build/Android by default
bash Tools/build-android.sh /tmp/out   # or choose the archive directory
```

The script locates Unreal Engine (from `UNREAL_ENGINE_PATH`, `UE_ROOT`,
`UE5_ROOT`, or a few common install paths), then runs:

```
RunUAT BuildCookRun -project=Shadowbound.uproject -platform=Android \
  -clientconfig=Development -cook -build -stage -pak -archive ...
```

It succeeds **only if an APK was actually produced**; otherwise it exits non-zero.
On a machine without Unreal Engine it prints how to provide one and exits 1 rather
than pretending to have built anything.

### Android configuration chosen

Set in `Config/DefaultEngine.ini` under
`[/Script/AndroidRuntimeSettings.AndroidRuntimeSettings]`:

| Setting | Value | Why |
| --- | --- | --- |
| Architecture | ARM64 only (`bBuildForArm64=True`, `bBuildForX86=False`) | Required for Play Store submission; halves build size |
| Graphics API | Vulkan (`bSupportsVulkan=True`) | Faster on the target hardware |
| Orientation | Landscape | A third-person action game is unplayable in portrait |
| Minimum API | 24 (Android 7.0) | Covers the target device range without legacy branches |
| Max aspect ratio | 2.4 | Modern notched/elongated phones |

## Continuous integration (GitHub Actions)

`.github/workflows/ci.yml` runs two jobs:

| Job | When | What it does |
| --- | --- | --- |
| **verify** | Every push and pull request | The four engine-free gates: purity, 563 C# tests, 82 C++ tests, Unreal layout. About a minute. |
| **android** | Pushes to `main` and manual dispatch | Runs `Tools/build-android.sh` (RunUAT `BuildCookRun`) when Unreal Engine is available, then uploads the APK. |

There are **no Unity licence secrets** anywhere in the workflow — Unreal does not
need one.

The `android` job needs Unreal Engine on the runner. GitHub's standard hosted
runners ship none, and cloning Epic's source needs an Epic-linked account. To make
the job build, set the repository variable `UNREAL_ENGINE_PATH` to a UE 5.6 install
on:

- a **self-hosted runner** with Unreal Engine installed, or
- a **UE-capable hosted runner image**.

Until then the job reports clearly that it has no engine and produces nothing: a
normal push **skips** with a notice (no fake artifact), while a manual
`workflow_dispatch` **fails loudly**, because someone explicitly asked it to build.

## Troubleshooting

**The project will not open / modules fail to compile.** Unreal 5.6 is required
(`EngineAssociation` in `Shadowbound.uproject`). Generate project files first, then
build the `Shadowbound` target.

**"Missing module ShadowboundCore".** Build the C++ modules before opening the
editor; the module is compiled, not shipped.

**Nothing renders, or geometry is magenta.** The placeholder art comes from
`/Engine/BasicShapes` and the default shape material's `Color` parameter. If a
future material renames that parameter, the tint becomes a no-op — the shape and
the game rules are unaffected.

**The camera goes through walls.** The follow camera's spring arm uses the
`ECC_Camera` probe channel; make sure the arena geometry blocks it (the default
engine shapes do).

**`Tools/build-android.sh` says it cannot find Unreal Engine.** That is the honest
answer on a normal machine. Point `UNREAL_ENGINE_PATH` at a UE 5.6 install, or run
it on a machine that has one.
