# Verification status

This file records what has actually been **executed and observed**, and what has
not. It exists because "the code compiles" and "the tests pass" are not the same
claim as "the game works", and the difference matters.

## What has been run, and passed

| Check | Command | Result |
| --- | --- | --- |
| Core purity gate | `bash Tools/check-core-purity.sh` | Pass — core is engine-free |
| Core compiles under Unity's constraints | `bash Tools/test-core.sh` | Pass — netstandard2.1, C# 9, 0 warnings |
| Core test suite | `bash Tools/test-core.sh` | **536 passed, 0 failed** |
| Every C# file parses at C# 9 | `bash Tools/check-syntax.sh` | Pass — 47 files, no syntax errors |
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
calls had been confirmed to exist with the signature used. It has since caught
two more: `ModifierOp` has no member called `Percent` or `Multiply` (they are
`PercentAdditive` and `PercentMultiplicative`), which a menu label would have
tripped over.

## The runtime-path audit

"Compiles and is tested" is not "is reachable while playing". Auditing the project
against that standard found three features that were fully implemented, fully
tested, and **impossible to reach in a running game**. None of them failed a test,
because every test exercised them directly rather than through the path a player
takes.

### 1. Quest rewards were never granted, and the story could not advance

A quest only becomes startable once its prerequisite has been **turned in**.
Turning in is a separate step from completing, and nothing in the project ever
performed it. So:

- The second quest onward was permanently `Locked`, and its reward was never
  granted. The authored story was unreachable past the opening scene.
- Every individual quest test still passed, because each examined one quest in
  isolation and a single quest does not need a turn-in to be tested.

Fixed in `GameSession.AdvanceQuests`, with `QuestAdvancementTests` driving the
chain end to end: finish a quest, get paid, watch the next one appear. The old
behaviour is asserted too, as `WithoutAutoAdvance_TheStoryStallsAfterTheOpeningQuest`,
so it cannot come back silently.

This change also altered eight existing tests, all of which had encoded the
broken flow. Each was confirmed to be a stale expectation rather than a
regression, and they now disable automatic advancement so they keep testing the
plumbing they are about.

### 2. Nothing could ever be equipped

`EquipmentLoadout` was complete and tested, wired into saving, and called by
nothing at runtime. The Warden's Blade handed over by the second quest went into
the bag and stayed there. `GameSession.TryEquipFromInventory` closes it, and the
menu gives the player a way to ask.

The swap is deliberately one transaction in the core rather than an equip
followed by a stow in the UI, because the two-step version destroys the replaced
item whenever the bag is full - which is exactly when a player finds an upgrade.
`EquippingIntoAFullBag_StillKeepsTheReplacedItem` pins that down.

### 3. The save and equipment screens were unreachable on a phone

The menu opened with `Esc` or `Tab`. Android has no keyboard. The menu, the save
slots and the equipment screen would all have been present and invisible in the
build this project targets. Fixed with an on-screen button, plus a `RESUME` row
that is always first - a menu you can open but not close is worse than no menu.

This one was caught while writing the code rather than by the audit, which is the
argument for asking the question of every change rather than once.

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
