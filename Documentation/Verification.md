# Verification status

This file records what has actually been **executed and observed**, and what has
not. It exists because "the code compiles" and "the tests pass" are not the same
claim as "the game works", and the difference matters.

## What has been run, and passed

| Check | Command | Result |
| --- | --- | --- |
| Core purity gate (C# and C++) | `bash Tools/check-core-purity.sh` | Pass — both cores are engine-free |
| C# core compiles under the original constraints | `bash Tools/test-core.sh` | Pass — netstandard2.1, C# 9, 0 warnings |
| C# core test suite | `bash Tools/test-core.sh` | **563 passed, 0 failed** |
| C++ core compiles (plain compiler, warnings are errors) | `bash Tools/test-core-cpp.sh` | Pass — `g++ -std=c++17 -Wall -Wextra -Werror` |
| C++ core test suite | `bash Tools/test-core-cpp.sh` | **82 passed, 0 failed** |
| Unreal project layout is complete and Unity-free | `bash Tools/check-unreal-layout.sh` | Pass |
| Android build script classifies its environment honestly | `bash Tools/build-android.sh` | **Exits 3** (ENVIRONMENT LIMITATION) with an explanation; produces no APK |
| Android workflow runs and measures the runner | GitHub Actions `android.yml` | See "The Unreal Android build" below |

All of these run without Unreal Engine installed.

### Cross-implementation parity

The C++ core is a port of the C# core, so the two must agree on the values that
make the simulation deterministic. This was checked by running the same seed
through both implementations:

```
C#  DeterministicRng(42):  NextUInt x3 = 492690617, 1919685028, 3561993920
C++ DeterministicRng(42):  NextUInt x3 = 492690617, 1919685028, 3561993920

C#  NextFloat() = 0.114713400     C++ NextFloat() = 0.11471343
C#  StableHash("a") = af63dc4c8601ec8c
C++ StableHash("a") = af63dc4c8601ec8c
```

The C++ suite pins these values as tests (`RngTests.cpp`), so a future change that
breaks the sequence fails the build rather than silently changing how a fight
plays out.

### The test suites

Both suites exercise the **real** game-rule sources — not a copy, not a
re-implementation — so every green test is a statement about the code that ships.

- The C# suite compiles `Core/**/*.cs` directly under `netstandard2.1` with
  `LangVersion 9.0`, the constraints the project originally targeted.
- The C++ suite compiles `Source/ShadowboundCore/**/*.cpp` (except the module
  registration file) with a plain C++ compiler.

The purity gate is proven to fail when it should: a deliberate `using
UnityEngine;` in the C# core, or a deliberate `#include "CoreMinimal.h"` in the
C++ core, trips it.

## What has NOT been run

Be explicit about this, because the gaps are real.

| Not verified | Why | What it would take |
| --- | --- | --- |
| **The Unreal project compiling** | No Unreal Engine is installed in the build environment. | Generate project files and build with UE 5.6. |
| **The Unreal game layer running** | Same reason. Nothing under `Source/Shadowbound/` has ever executed. | Press Play in UE 5.6. |
| **Anything visual** | Same reason. No `.uproject` has been opened, nothing rendered. | Open and play. |
| **The Android APK** | No Unreal Engine, no Android SDK/NDK/JDK. | `bash Tools/build-android.sh` on a UE-capable machine, or the CI `android` job. |
| **Touch controls on a device** | Requires hardware. | Install the APK and play. |
| **Performance on target hardware** | Requires hardware. | Profile on a mid-range phone. |
| **The C# → C++ port parity beyond the engine-free slice** | Only the encounter-layer slice is ported so far; the remaining systems still exist as C# only. See `MIGRATION_PLAN.md`. | Port each slice and its tests. |

**Therefore: the Unreal game has not been demonstrated to be playable.** The game
rules of the ported slice are verified by execution in two languages; the Unreal
layer that presents them is, so far, only written.

## Reproducing the verification

```bash
# Both cores: purity gate, then their test suites.
bash Tools/test-core.sh
bash Tools/test-core-cpp.sh

# Just the boundary rule.
bash Tools/check-core-purity.sh

# The Unreal project is structurally coherent and free of Unity leftovers.
bash Tools/check-unreal-layout.sh

# Attempt the Android build. Without Unreal Engine this fails honestly.
bash Tools/build-android.sh
```
