# Building and running

## Requirements

| Task | Needs |
| --- | --- |
| Run the C# core test suite | .NET SDK 8.0 or newer |
| Run the C++ core test suite | A C++17 compiler (`g++` or `clang++`) |
| Open, play and build the game | **Unreal Engine 5.8** with the Android platform installed |
| Produce an APK | Unreal Engine 5.8 + Android SDK 35 / NDK r27c / build-tools 35.0.1 / OpenJDK 21.0.3 |

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

1. Install **Unreal Engine 5.8** with the **Android** platform (Epic Games
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
| Target API | 35 (`TargetSDKVersion=35`) | Google Play requires target API 35 for new apps and updates; UE 5.7+ supports it |
| Minimum API | 26 (`MinSDKVersion=26`) | UE 5.8's minimum install API for shipping projects |
| Max aspect ratio | 2.4 | Modern notched/elongated phones |

The **toolchain** (NDK r27c, build-tools 35.0.1, OpenJDK 21.0.3, SDK 35) is
supplied by the build environment, not by this file. UE 5.8's documented Android
requirements are exactly these versions.

## Continuous integration (GitHub Actions)

Two workflows:

| Workflow | When | What it does |
| --- | --- | --- |
| **`ci.yml`** | Every push and pull request | The four engine-free gates: purity, 563 C# tests, 82 C++ tests, Unreal layout. About a minute. |
| **`android.yml`** | Pushes to `main` (build inputs) and manual dispatch | Packages Android ARM64 inside an Unreal Engine container image via `RunUAT BuildCookRun`, and classifies the outcome. |

There are **no Unity licence secrets** anywhere.

### How `android.yml` classifies its result

Every run reports exactly one of:

| Verdict | Meaning | Job status |
| --- | --- | --- |
| **BUILD SUCCESS** | `Tools/build-android.sh` produced an APK that passed validation (contains `lib/arm64-v8a`, has an `AndroidManifest.xml`, plausible size). | passes |
| **BUILD FAILURE** | An engine was present but compile/cook/package failed. | fails |
| **ENVIRONMENT LIMITATION** | The runner cannot host the build (no engine image entitlement, or too little disk/RAM). | fails on manual dispatch, warns on push |

It never creates a placeholder APK, and success requires a real, validated one.

### What the free runner actually reports (measured)

Don't trust the datasheet over the measurement. GitHub's reference lists standard
runners as 4 vCPU / 16 GB RAM / 14 GB SSD, but the real `android.yml` run on this
public repo measured:

| Resource | Measured | Estimated requirement |
| --- | --- | --- |
| Disk (`/dev/root`) | 145 G total, **97 GB free** after reclaim | ~90 GB (≈38 GB engine image + ~10 GB Android SDK/NDK/JDK + ~20–40 GB cook/build intermediates) |
| RAM | 15 GiB total, 14 GiB available | 16 GB |
| CPU | 4 vCPU | 4 |
| Job limit | 6 hours | — |

So disk and CPU met the estimate, and RAM is just under the 16 GB guideline. The
limitation that actually stopped the run was **image entitlement**, and the run
reported it as the sole reason:

```
ENVIRONMENT LIMITATION — Epic's official image is private and no usable
GHCR_TOKEN (read:packages, Epic-linked account) was supplied
```

- Epic's image `ghcr.io/epicgames/unreal-engine` is **private**. It needs a
  GitHub account linked to an Epic account plus a token with `read:packages`
  (the `GHCR_TOKEN` secret). Without it the image cannot be pulled, so no build
  starts. The `Log in to GitHub Container Registry` step is deliberately run
  regardless of disk, so this is a *tested* fact rather than an inference.
- The remaining risks with a free runner are RAM (15 GiB), CPU (4) and the
  **6-hour** job limit: UE Android cooking is CPU-heavy and may not finish.
- `actions/cache` is capped at **10 GB per repository**, so caching cannot carry
  a 38 GB image — that route is closed regardless.

The `preflight` job measures the runner and writes the numbers to the job summary,
and the `package` job lists every unmet prerequisite, so the limitation is
demonstrated rather than asserted.

### How to make it build

**Path A — free, browser only (try this first).** Unlock the engine image and run
on the standard free runner:

1. Link your GitHub account to an Epic Games account and accept the Unreal Engine
   EULA (browser only, free): <https://www.unrealengine.com/en-US/ue-on-github>.
2. Create a GitHub Personal Access Token with the **`read:packages`** scope.
3. Add it as the repository secret **`GHCR_TOKEN`**
   (*Settings → Secrets and variables → Actions → New repository secret*).
4. Run the **“Shadowbound — Unreal Android”** workflow via *Run workflow*
   (*Actions → the workflow → Run workflow*), leaving `runner` as `ubuntu-latest`.

The run then pulls Epic's image and does the real Compile → Cook → Package →
Validate. Whether a free runner (4 vCPU / 15 GiB / 6 h) finishes a UE Android
cook is genuinely uncertain — that is an experiment Path A answers, with real
logs, and the workflow will report BUILD SUCCESS or BUILD FAILURE honestly.

**Path B — reliable, paid.** Unlock the image as above, then run the workflow with
`runner` set to a **GitHub larger runner** (needs GitHub Team/Enterprise and
billing):

| Larger runner | Disk | RAM | Notes |
| --- | --- | --- | --- |
| `ubuntu-4core` | 150 GB | 16 GB | disk fine, slow |
| `ubuntu-8core` | 300 GB | 32 GB | recommended |
| `ubuntu-16core` | 600 GB | 64 GB | fastest |

**Path C — self-hosted runner.** Any machine with UE 5.8 and ~100 GB free disk,
registered as a self-hosted runner, with `UNREAL_ENGINE_PATH` set. This needs a
machine, so it does not meet the “no PC” constraint.

`GHCR_TOKEN` is needed by every path: the engine image is not public, and that is
the one thing standing between the repository and a real build today.

## Troubleshooting

**The project will not open / modules fail to compile.** Unreal 5.8 is required
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
