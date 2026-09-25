# Verification status

This file records what has actually been **executed and observed**, and what has
not. It exists because "the code compiles" and "the tests pass" are not the same
claim as "the game works", and the difference matters.

## What has been run, and passed

| Check | Command | Result |
| --- | --- | --- |
| Core purity gate | `bash Tools/check-core-purity.sh` | Pass — core is engine-free |
| Core compiles under Unity's constraints | `bash Tools/test-core.sh` | Pass — netstandard2.1, C# 9, 0 warnings |
| Core test suite | `bash Tools/test-core.sh` | **511 passed, 0 failed** |
| Every C# file parses at C# 9 | `bash Tools/check-syntax.sh` | Pass — 46 files, no syntax errors |
| **Unity layer type-checks against real Unity assemblies** | `bash Tools/check-unity-layer.sh` | Pass — **0 errors, 0 warnings** |

All three commands run without a Unity installation.

### The test suite

The suite exercises the **real** core sources. The test project compiles
`Assets/Scripts/Core/**/*.cs` directly — not a copy, not a re-implementation — so
every green test is a statement about the code that ships.

The harness is also proven to fail when it should: a deliberate
`using UnityEngine;` in a core file makes the purity gate fail, and a deliberate
`record` type or C# 10 syntax makes the netstandard2.1 build fail.

Coverage spans numerics, determinism, stats, damage and mitigation, vitals, status
effects, abilities, attack resolution, enemy AI, items and loot, progression,
quests, chapters, the world graph, JSON serialisation and save migration, plus two
integration tests: a headless encounter driven to completion, and a full session
where a kill turns into loot, experience, journal progress and a save that
round-trips.

### The Unity layer type-check

`Tests/Shadowbound.UnityCheck` supplies real Unity reference assemblies — via the
`OpenMod.UnityEngine.Redist` package, which includes `UnityEngine.CoreModule`,
`UnityEngine.UIModule`, `UnityEngine.PhysicsModule`, `UnityEngine.UI` and
`UnityEngine.TextRenderingModule` — and compiles the actual sources from
`Assets/Scripts/Game` against them.

The new Input System ships with no reference assembly, so
`Tests/Shadowbound.UnityCheck/Shims/` supplies its public surface. Those shim
types are written to match the package's documented API, and they live outside
`Assets/` so Unity never sees them.

**This proved its worth immediately.** Type-checking the Game assembly for the
first time found two genuine compile errors that a hand review had missed:

1. `GameBootstrap` declared `BuildHud` as **both** a public field and a private
   method — `CS0102`. The Unity build would not have started.
2. `FileSaveStorage` declared a `Directory` property, which **shadowed
   `System.IO.Directory`** in the same class — three `CS1061` errors. Renamed to
   `DirectoryPath`.

By the time it reported clean, every Unity API and every core API the Game layer
calls had been confirmed to exist with the signature used.

### Content validation moved into the tested core

Content cross-checking used to live in the editor tooling, where it could only run
by opening Unity — so the checking itself was never checked. It now lives in
`Shadowbound.Core.Content.ContentValidator`, and 25 tests inject a specific fault
each (a loot entry naming a missing item, an out-of-range attack index, a quest
targeting a creature that does not exist, a prerequisite cycle, an unreachable
region, and so on) and assert the validator names it.

That distinction matters: a validator that reports "clean" is worthless unless it
is also proven to fail when something is wrong. The clean result on the shipped
content now means something.

## What has NOT been run

Be explicit about this, because the gaps are real.

| Not verified | Why | What it would take |
| --- | --- | --- |
| **The `Editor` assembly compiling** | No `UnityEditor` reference assembly is available. | Open the project in Unity 6 and read the Console. |
| **Runtime behaviour of anything** | No Unity installation. Nothing in the project has ever executed. | Press Play. |
| **Anything visual** | Same reason. No rendering. | Press Play. |
| **The Android build** | No Unity, no Android SDK, no NDK, no JDK. No APK can be produced without them. | `Shadowbound → Build Android APK` with the Android module installed. |
| **Touch controls on a device** | Requires hardware. | Install the APK and play. |
| **Performance on target hardware** | Requires hardware. | Profile on a mid-range phone. |
| **Compiling on Unity 6 specifically** | The reference assemblies are Unity **2021.3**. Code that compiles here also compiles on Unity 6 *unless* Unity 6 removed an API. | Open in Unity 6. |

**Therefore: this project has not been demonstrated to be playable.** The game
rules are verified by execution. The Game assembly is verified to compile. Nothing
has been run, seen, or installed.

## Reproducing the verification

```bash
# The core: purity gate, compile under Unity's constraints, 511 tests.
bash Tools/test-core.sh

# Just the assembly boundary rule.
bash Tools/check-core-purity.sh

# Parse every C# file at Unity's language level.
bash Tools/check-syntax.sh

# Type-check the Game assembly against real Unity reference assemblies.
bash Tools/check-unity-layer.sh
```

Inside Unity 6, after the project imports:

1. Open the Console — there should be no compile errors.
2. **Shadowbound → Set Up Project**
3. **Shadowbound → Validate Content** — reports every authored item, creature,
   loot table, quest, chapter and region, and flags any reference that points at
   something that does not exist.
4. Press **Play**.

## Note on the reference assemblies

`OpenMod.UnityEngine.Redist` is an unofficial third-party redistribution of Unity
assemblies. It is used **only** by `Tests/Shadowbound.UnityCheck`, which Unity
never compiles, so nothing in the shipped game depends on it. Restoring it needs
network access on the first run.
