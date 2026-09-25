# Verification status

This file records what has actually been **executed and observed**, and what has
not. It exists because "the code compiles" and "the tests pass" are not the same
claim as "the game works", and the difference matters.

## What has been run, and passed

| Check | Command | Result |
| --- | --- | --- |
| Core purity gate | `bash Tools/check-core-purity.sh` | Pass — core is engine-free |
| Core compiles under Unity's constraints | `bash Tools/test-core.sh` | Pass — netstandard2.1, C# 9, 0 warnings |
| Core test suite | `bash Tools/test-core.sh` | **482 passed, 0 failed** |
| Every C# file parses at C# 9 | `bash Tools/check-syntax.sh` | Pass — 45 files, no syntax errors |

The syntax gate uses Roslyn to parse all 45 files under `Assets/Scripts`, including
the Unity layer. It is a **parse, not a compile**: it proves the files are valid
C# 9 and contain no unbalanced braces or truncated methods, and it catches a
language feature newer than Unity 6 accepts. It proves nothing about whether the
APIs those files call actually exist.

The test suite exercises the **real** core sources. The test project compiles
`Assets/Scripts/Core/**/*.cs` directly — not a copy, not a re-implementation — so
every green test is a statement about the code that ships.

The harness is also proven to fail when it should: a deliberate
`using UnityEngine;` in a core file makes the purity gate fail, and a deliberate
`record` type or C# 10 syntax makes the netstandard2.1 build fail.

### What the 482 tests actually cover

- **Numerics** — vector maths, clamping, angle wrapping, cone tests.
- **Determinism** — the same seed yields the same sequence; forked streams are
  independent; state survives a restore.
- **Stats** — flat, percent and multiplicative modifiers compose in a defined
  order; derived stats recompute; removal restores the original value.
- **Damage** — armour mitigation, penetration, crits, resistances, the
  interaction between them, and that mitigation scales a crit and a normal hit
  equally (so armour does not erode a crit's *relative* advantage).
- **Vitals** — damage, healing, stamina, death, and that a corpse does not
  regenerate back to life.
- **Status effects** — stacking rules, damage over time, refresh, expiry,
  cooldown-rate interaction.
- **Abilities** — wind-up, commitment, interruption by stagger, cooldown,
  stamina cost, and the self-stagger cost of a whiffed heavy attack.
- **Attack resolution** — arcs, target selection, multi-hit.
- **Enemy AI** — the state machine, vision cone, hearing, memory, disengagement,
  return-to-home, and that hurting one enemy alerts nearby allies.
- **Items** — inventory stacking and limits, equipment swapping, stat
  application and removal, loot table weights, guaranteed and weighted rolls.
- **Progression** — experience curves, level thresholds, attribute points.
- **Quests** — objective kinds, sequential and parallel objectives, completion
  firing exactly once, prerequisites, chapter re-evaluation.
- **World** — the region graph, bidirectional links, gated access.
- **Serialisation** — the JSON reader/writer, round-tripping, migration chains,
  and rejection of malformed input.
- **Integration** — a headless encounter driven to completion, and a full
  session: kill an enemy → loot lands in the bag → experience is awarded →
  the journal advances → the save round-trips and restores the generator so
  subsequent rolls continue the same sequence.

## What has NOT been run

Be explicit about this, because the gaps are real.

| Not verified | Why | What it would take |
| --- | --- | --- |
| **The `Game` and `Editor` assemblies compiling** | There is no Unity installation in the environment this was built in. `Game` references `UnityEngine`, so `dotnet` cannot compile it. | Open the project in Unity 6 and check the Console. |
| **Anything visual** | Same reason. No Unity, no rendering. | Press Play. |
| **The Android build** | No Unity, no Android SDK, no NDK, no JDK. No APK can be produced without them. | `Shadowbound → Build Android APK` in Unity with the Android module installed. |
| **Touch controls on a real device** | Requires a device. | Install the APK and play. |
| **Performance on target hardware** | Requires a device. | Profile on a mid-range phone. |

**Therefore: this project has not been demonstrated to be playable.** The game
rules have been demonstrated to be correct, by execution. The presentation layer
has been written and cross-checked against the core's actual API surface, but it
has never been compiled or run.

## How the engine layer was checked without an engine

Since the Unity assemblies could not be compiled, every core API they call was
read from the core source and matched by hand — method names, parameter order,
return types and accessibility. Mismatches found and fixed this way included:

- `QuestDefinition.DisplayName` → the real member is `Title`.
- `QuestState.Progress(id)` → the real member is `ProgressOf(id)`.
- `LootTable.Entries` → loot is stored as separate `Guaranteed` and `Weighted`
  arrays.
- `GameContent` archetype ids are `const string` fields, not an enum.

This process also found three genuine defects **inside the Unity layer**, fixed
before ever being compiled:

1. `HudController` mutated a `Dictionary` while enumerating it, which throws
   `InvalidOperationException` at runtime — the look control would have killed
   the game on the first drag.
2. The experience bar was anchored with equal min and max anchors and a width of
   zero, so it would have been invisible.
3. The on-screen stick's handle was positioned with a fixed pixel scale, which
   drifts away from the thumb on any screen that is not exactly 16:9.

That is a useful signal, but it is not a substitute for compiling. Treat the
engine layer as **unverified until Unity reports no errors**.

## Reproducing the verification

```bash
# Everything that can be checked without Unity.
bash Tools/test-core.sh

# Just the boundary rule.
bash Tools/check-core-purity.sh

# Parse every C# file, including the Unity layer, at Unity's language level.
bash Tools/check-syntax.sh
```

Inside Unity 6, after the project imports:

1. Open the Console — there should be no compile errors.
2. **Shadowbound → Set Up Project**
3. **Shadowbound → Validate Content** — reports every authored item, creature,
   loot table, quest, chapter and region, and flags any reference that points at
   something that does not exist.
4. Press **Play**.
