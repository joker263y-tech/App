# SHADOWBOUND: THE LAST NIGHT

An original dark-fantasy 3D action RPG for Android. Third-person, real-time
combat, semi-open world, story-driven PvE.

> **Original work.** This project is inspired by the broad atmosphere and
> design principles of the dark-fantasy genre. All characters, factions,
> creatures, regions, mythology, terminology, dialogue and visual identity are
> original creations. Nothing is copied from any existing work.

---

## Current status

| Area | State |
| --- | --- |
| Repository + Unity project scaffolding | Done |
| Pure-C# game-logic core | In progress |
| Core test suite | Running green (16 tests) |
| Unity layer (MonoBehaviour adapters) | Not started |
| Procedural content bootstrap | Not started |
| Android build | Not started |

The game is **not yet playable**. See [Roadmap](#roadmap).

---

## Why the code is split in two

The project is deliberately divided into a **pure C# core** and a **thin Unity
layer**. This is an architectural decision, not a stylistic one.

```
Assets/Scripts/Core/   ->  game rules. No UnityEngine. Fully testable.
Assets/Scripts/Game/   ->  MonoBehaviours. Translate input into core intent,
                           and core output into transforms, VFX and audio.
Assets/Scripts/Editor/ ->  tooling that generates scenes, materials and prefabs.
```

The boundary is **enforced by the compiler**, not by convention:

- `Assets/Scripts/Core/Shadowbound.Core.asmdef` sets `"noEngineReferences": true`.
  Unity will refuse to compile the core if any file touches `UnityEngine`.
- `Tools/check-core-purity.sh` reproduces that rule for the command-line build,
  so the same violation fails here too.
- `Tests/Shadowbound.Core.Build` compiles the core sources against
  `netstandard2.1` with `LangVersion 9.0` — Unity 6's exact API surface and
  language level. C# 10+ syntax or a .NET-10-only API fails in the test harness
  instead of failing later in the editor.

The payoff: combat maths, AI decisions, loot rolls, progression curves and save
serialisation are all verifiable without opening Unity. A "feature is done"
claim can be backed by an actually executed test.

---

## Requirements

- **Unity 6** (6000.0 LTS or newer) with Android build support
- **.NET SDK 8.0+** — only needed to run the core test suite
- **Android SDK / NDK** — only needed to produce an APK

The test suite needs no Unity installation. Unity needs no .NET SDK.

---

## Running the core test suite

```bash
bash Tools/test-core.sh
```

This runs the purity gate, compiles the core under Unity-equivalent
constraints, and executes the xUnit suite. Alternatively:

```bash
dotnet test Tests/Shadowbound.Core.Tests
```

## Opening the project in Unity

1. Open **Unity Hub** → *Add* → *Add project from disk* → select this folder.
2. Open with **Unity 6**. The first import will resolve packages and may take
   several minutes.
3. Run the content bootstrap to generate materials, prefabs and scenes:
   **`Shadowbound → Bootstrap → Generate All Content`**
   (see `Assets/Scripts/Editor/ContentBootstrap.cs`).

Hand-authored `.unity` and `.prefab` YAML files are intentionally **not**
committed. They are fragile, unreviewable in diffs, and impossible to verify
without the editor. Instead the editor tooling builds content deterministically
from code, so scenes are reproducible and reviewable.

## Building the Android APK

1. In Unity: **File → Build Settings**, confirm the scene list is populated by
   the bootstrap step, and switch platform to **Android**.
2. Player settings are configured by the bootstrap for a mid-range Android
   target (ARM64, IL2CPP, URP mobile quality tiers).
3. **Build** to produce the APK.

---

## Roadmap

1. **Core foundation** — maths, deterministic RNG, stats, damage, status
   effects. *(in progress)*
2. **Combat** — abilities, cooldowns, hit resolution, enemy AI.
3. **RPG systems** — items, inventory, equipment, loot tables, progression.
4. **Story structure** — quests, chapters, world region graph.
5. **Persistence** — JSON serialisation, save slots, versioned migration.
6. **Headless encounter simulation** — proves the full combat loop end to end.
7. **Unity layer** — player controller, third-person camera, enemy agents, HUD.
8. **Content bootstrap** — procedural materials, prefabs, arena, menu.
9. **Android build + performance pass.**

---

## Repository layout

```
Assets/
  Scripts/Core/        pure C# game logic (no engine references)
  Scripts/Game/        Unity adapters (MonoBehaviours)
  Scripts/Editor/      content generation tooling
  Settings/            URP assets (generated)
  Scenes/              scenes (generated)
Packages/              Unity package manifest
ProjectSettings/       Unity project configuration
Tools/                 command-line build and verification scripts
Tests/                 .NET test projects for the core
Documentation/         architecture and design notes
Builds/                build output (git-ignored)
```

## Note on committed configuration

Only `ProjectSettings/ProjectVersion.txt` is hand-authored. Everything else in
`ProjectSettings/` is generated by Unity on first open, and the settings that
actually matter (render pipeline, quality tiers, tags, layers, build scenes) are
applied by the editor bootstrap. Editing Unity's generated YAML by hand is
error-prone and effectively unverifiable, so the project treats code as the
source of truth for configuration.
