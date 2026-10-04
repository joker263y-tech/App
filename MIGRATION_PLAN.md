# MIGRATION PLAN — Unity → Unreal Engine 5

**Project:** SHADOWBOUND: THE LAST NIGHT
**Target:** Unreal Engine 5, Android (ARM64), built from the repository root.
**Rule this document exists to enforce:** nothing is claimed migrated until its
Unreal replacement exists and has been verified by the checks that can actually
run. See [Verification contract](#verification-contract).

---

## 1. Inventory

Every file in the repository, classified by how it relates to Unity.

### 1.1 Unity-specific — replaced, then removed

These exist only because the engine was Unity. Each one is removed **after** its
Unreal replacement exists in this repository (the deletion order in §6 enforces
this).

| File / folder | Role under Unity | Unreal replacement |
| --- | --- | --- |
| `Assets/Scripts/Game/GameBootstrap.cs` | MonoBehaviour that assembled session, arena, player, enemies, camera, HUD at runtime | `Source/Shadowbound/…/SbGameMode` + `SbGameInstance` + `SbArena` |
| `Assets/Scripts/Game/Player/PlayerInputDriver.cs` | `ICombatantDriver`: device input → `CombatIntent` | `SbPlayerDriver` + `ASbPlayerController` (Enhanced Input, programmatic mapping context, touch layer) |
| `Assets/Scripts/Game/Player/ThirdPersonCamera.cs` | Follow camera driven by yaw/pitch, obstruction push-in, shake | `USbFollowCameraComponent` (spring arm + camera, control rotation, collision probe) |
| `Assets/Scripts/Game/Combat/CombatantView.cs` | Mirrors core `Combatant` onto a transform; hit flash | `ASbCombatantActor` (copies core position/rotation; hit flash; never writes back) |
| `Assets/Scripts/Game/Ui/HudController.cs` | Code-built UGUI HUD + on-screen touch controls | `ASbHud` (AHUD canvas draw: bars, cooldowns, touch stick + buttons) |
| `Assets/Scripts/Game/Ui/GameMenu.cs` | Paged in-game menu (equipment, attributes, world, saves) | **Phase 3** — UMG widget built from C++ (not part of phase 1) |
| `Assets/Scripts/Game/Save/FileSaveStorage.cs` | Save files under `Application.persistentDataPath` | **Phase 2** — `ISaveStorage` implementation over `FPlatformProcess`/`FPaths` (core format is engine-free and unchanged) |
| `Assets/Scripts/Editor/ProjectSetup.cs` | UnityEditor menu: scene creation, Android player settings, build entry | `Config/*.ini` (Android player settings) + `Tools/build-android.sh` (RunUAT command-line build) |
| `Assets/Scripts/Core/Shadowbound.Core.asmdef` | Unity assembly boundary (`noEngineReferences`) | C++ module boundary: `ShadowboundCore` module has zero engine includes, enforced by `Tools/check-core-purity.sh` + standalone compile in `Tools/test-core-cpp.sh` |
| `Assets/Scripts/Game/Shadowbound.Game.asmdef`, `Assets/Scripts/Editor/Shadowbound.Editor.asmdef` | Unity assemblies | deleted with their code |
| `Packages/manifest.json` | Unity package manifest (URP, Input System, UGUI) | `Shadowbound.uproject` plugin list (Enhanced Input) |
| `ProjectSettings/ProjectVersion.txt` + `ProjectSettings/` | Unity project settings | `Config/Default*.ini` |
| `Tests/Shadowbound.UnityCheck/` | Type-check Game assembly against Unity reference assemblies | nothing — the Unity layer no longer exists |
| `Tools/check-unity-layer.sh` | Type-check driver for the above | nothing |
| `.github/workflows/unity-activation.yml`, `ci.yml` Unity jobs | Unity licence activation + game-ci APK build | `.github/workflows/ci.yml` verify + Unreal Android job (RunUAT) |
| `Assets/` folder itself | Unity asset root | gone; engine-free core lives under `Core/` |

### 1.2 Reusable — kept verbatim (not Unity code)

| File / folder | Why it is kept |
| --- | --- |
| `Assets/Scripts/Core/**` → moved to **`Core/**`** | The pure game-rules library: combat, damage, vitals, statuses, abilities, AI, items, loot, quests, chapters, progression, world graph, RNG, JSON saves, content + validator. Zero engine references by construction. This *is* the game design, expressed executably. |
| `Tests/Shadowbound.Core.Tests/**` (563 tests) | Runs on plain .NET, no engine. Kept green through the migration. |
| `Tests/Shadowbound.Core.Build/**` | Compiles the real core sources under the exact constraints of the previous target; kept as a compile gate. |
| `Tools/test-core.sh`, `Tools/check-core-purity.sh`, `Tools/check-syntax.sh`, `Tools/SyntaxCheck/` | Engine-free verification gates. Paths updated to the new core location; the purity gate is extended to cover the new C++ core too. |
| `Documentation/Design.md` | The original world, factions, creatures, abilities — engine-independent by nature. |
| `Documentation/Architecture.md`, `Verification.md`, `Building.md`, `README.md`, `NOTICE.md` | Updated for the new engine, same content decisions. |
| Game design numbers (stats, abilities, creature archetypes, spawn plans, world graph, quests) | Ported **as data** into the C++ core (`Source/ShadowboundCore/…/SbContent`) and kept in the C# core until the rest is ported. |

---

## 2. What moves, what is replaced, what stays

### 2.1 Moves to Unreal C++ **now** (phase 1 — this migration)

The **encounter layer** of the core: everything needed for
Player → Camera → World → Enemy → Combat to be a real game loop driven by the
same rules the C# suite verifies.

| C# core (`Shadowbound.Core`) | Unreal C++ (`ShadowboundCore` module, engine-free) |
| --- | --- |
| `Numerics/FMath.cs`, `Numerics/Float3.cs` | `SbNumerics.h` (`CoreMath`, `Float3`) |
| `Random/DeterministicRng.cs` | `SbRng.h/.cpp` (PCG32 + FNV-1a, byte-identical algorithm) |
| `Stats/StatId.cs`, `StatModifier.cs`, `StatSet.cs` | `SbStats.h/.cpp` |
| `Combat/DamageType.cs`, `DamageCalculator.cs` | `SbDamage.h/.cpp` |
| `Combat/Vitals.cs` | `SbVitals.h/.cpp` |
| `Combat/StatusEffect.cs`, `StatusEffectSystem.cs` | `SbStatusEffects.h/.cpp` |
| `Combat/AbilityDefinition.cs`, `AbilityController.cs` | `SbAbilities.h/.cpp` |
| `Combat/Combatant.cs` | `SbCombatant.h/.cpp` |
| `Combat/AttackResolver.cs` | `SbAttackResolver.h/.cpp` |
| `Ai/PerceptionModel.cs`, `EnemyBrain.cs` | `SbPerception.h/.cpp`, `SbEnemyBrain.h/.cpp` |
| `Simulation/EncounterSimulation.cs` (fixed-timestep loop, `CombatIntent`, drivers) | `SbEncounter.h/.cpp` |
| `Content/GameContent.cs` — player kit, creature archetypes, brain tuning (numbers) | `SbContent.h/.cpp` (phase-1 slice: player + all four creature stat/ability/brain blocks) |

**How the port is done** — hand-ported, not machine-converted. The redesign
points, in the words of the request:

* No C# → C++ auto-conversion. Each file is re-expressed against C++ idioms and
  re-verified by its own tests.
* Engine-touching concepts are absent from the core by design: `UnityEngine`,
  `MonoBehaviour`, `GameObject`, `Transform`, Unity UI and Unity Input never
  appear. The core talks in `Float3`, and the Unreal layer converts at the
  boundary (`Float3(x,y,z)` Y-up ⇄ `FVector(x,y,z)` Z-up).
* Events (`event Action<…>`) become a small engine-free multicast
  `TEvent<…>` (`SbEvent.h`) — no `delegate`/`DECLARE_DYNAMIC_MULTICAST`
  (those are engine types).
* `object source` attribution becomes typed `const Combatant*`.
* `IReadOnlyList<T>` parameters become `const std::vector<T>&`.
* The simulation keeps its architecture: **one authority over position** — the
  Unreal actors copy from the core and never write to it, exactly as
  `CombatantView` did.

### 2.2 Replaced by Unreal systems (redesigned, not translated)

| Concern | Unity form | Unreal form |
| --- | --- | --- |
| Entry point / assembly of the game | `GameBootstrap` on a GameObject in a scene | `ASbGameMode` (default game mode in `Config`) + `USbGameInstance` (persistent playthrough state) |
| Scene assets | none committed; built at runtime | none committed; `ASbArena` builds floor, walls, pillars, gate, light from engine primitives at runtime (same philosophy: presentation generated, not authored) |
| Input | Unity Input System polled in `PlayerInputDriver` | Enhanced Input: `UInputMappingContext`/`UInputAction` **created in C++** (no authored `.uasset`), keyboard/mouse + touch (twin-zone stick, look drag, HUD buttons) |
| Camera | `ThirdPersonCamera` MonoBehaviour | `USbFollowCameraComponent` (USpringArmComponent + UCameraComponent; obstruction handling from the arm's collision probe) |
| Views | `CombatantView` copying transforms | `ASbCombatantActor` copying transforms (same never-writes-back rule) |
| HUD / touch controls | `HudController` code-built UGUI | `ASbHud` (AHUD draw + manual touch hit-testing — same manual hit-test approach, no widget tree needed yet) |
| Player input driver | `PlayerInputDriver : ICombatantDriver` | `SbPlayerDriver : ShadowboundCore::ICombatantDriver` (camera-relative movement from control rotation, queued/held abilities) |
| Android player settings | `ProjectSetup.ConfigureForAndroid` (C# editor code) | `Config/DefaultGame.ini` `[/Script/AndroidRuntimeSettings.AndroidRuntimeSettings]` (ARM64, landscape, min SDK 24, Vulkan) |
| APK build | Unity menu / game-ci | `Tools/build-android.sh` → `RunUAT BuildCookRun … -platform=Android` (no editor UI) |
| Assembly boundary | asmdef `noEngineReferences` | module boundary: `ShadowboundCore` (no engine headers, enforced by gate script) vs `Shadowbound` (gameplay glue) |

### 2.3 Stays in the C# core for now (phased out later, one slice at a time)

These are engine-free today, are covered by the 563-test suite, and are **not**
deleted — deleting them would delete gameplay logic that has no Unity
dependency. Each later phase ports one of them to `ShadowboundCore` and its
tests before anything C# is removed:

1. `Items` + `LootTable` + `EquipmentLoadout` (phase 2)
2. `Progression` + `Quests` + `ChapterTracker` (phase 2)
3. `World/WorldGraph` + region travel rules (phase 2)
4. `Serialization` (JSON, save, migration, slots) + `FileSaveStorage` port (phase 2)
5. `Content` remainder: items, loot tables, regions, quests, chapters +
   `ContentValidator` (phase 2/3)
6. `Simulation/GameSession` — the playthrough wiring (phase 2)

Until a slice is ported **and its tests are ported and green**, the C# original
stays. The end state removes `Core/` entirely; phase 1 does not.

> Note on the `Assets/` folder: the C# core is moved out of `Assets/` (a Unity
> concept) to `Core/`. Nothing Unity-specific remains in the tree after §6.

---

## 3. Phase 1 deliverable (this change)

Runnable minimum, from the repository root, no editor required:

```
Player (Warden, real stats)  →  camera-relative movement, 5 authored abilities
Camera (third person, obstruction-aware)
World/Arena (runtime-built: floor, walls, LOS pillars, gate, light, fog)
Enemies (Grey Wilds spawn plan: 3× Hollow Walker, 2× Cinder Hound, real stats/brains)
Combat (fixed-timestep encounter: windup/recovery, cone hits, armour curve,
        crits, resistances, statuses (burn), knockback, stagger, AI state machine,
        death → rewards hooks, corpse cleanup, encounter reset)
```

Plus:

* `Shadowbound.uproject` at repository root with two C++ modules.
* `Config/` — Android (ARM64, Vulkan, landscape, minSDK 24), default map +
  game mode, Enhanced Input defaults.
* GitHub Actions: engine-free `verify` job (all runnable gates) and an
  `android` job that runs `Tools/build-android.sh` and **fails with an explicit
  explanation** when no Unreal Engine is available (hosted runners do not ship
  one — see §5).
* C++ core test suite runnable without Unreal Editor: `Tools/test-core-cpp.sh`.

---

## 4. Repository layout after phase 1

```
Shadowbound.uproject          Unreal project (root)
Config/                       Android + engine + input configuration
Source/
  Shadowbound.Target.cs       Game target
  ShadowboundEditor.Target.cs Editor target
  ShadowboundCore/            ENGINE-FREE C++ rules (ported core slice + future ports)
  Shadowbound/                UE game module: GameMode, GameInstance, character,
                              controller, camera, arena, HUD, driver
Core/                         C# rules library (engine-free; still tested; shrinks each phase)
Tests/
  Shadowbound.Core.Tests/     563 C# tests (kept, green)
  Shadowbound.Core.Build/     C# compile gate (kept)
  ShadowboundCore.Cpp/        C++ tests for the ported slice (new)
Tools/                        verification + build scripts (no Unity)
Documentation/                design/architecture/build/verification (updated)
.github/workflows/            ci.yml: verify + Unreal Android
```

---

## 5. CI: how GitHub Actions gets Unreal Engine (investigated)

Facts checked against the current state of GitHub-hosted runners:

* **GitHub-hosted runners do not ship Unreal Engine**, and the engine plus its
  build products do not fit comfortably in a job on a free hosted runner. Any
  workflow that pretends otherwise fails at `RunUAT` with a confusing error.
* Cloning `EpicGames/UnrealEngine` from GitHub requires an Epic-linked GitHub
  account that has been granted repository access, and then an engine build —
  it is a legitimate route, but it needs real credentials and a large runner.

Therefore the `android` job is written to be **honest**:

1. It looks for an engine in this order:
   * repository variable `UNREAL_ENGINE_PATH` (the path on the runner),
   * `UE_ROOT` environment variable of the runner,
   * well-known locations (`/opt/unreal-engine`, `/Users/Shared/Epic Games/*`,
     `C:\Program Files\Epic Games\*` on a Windows self-hosted runner).
2. If `RunUAT` is found → run
   `BuildCookRun -project=…Shadowbound.uproject -platform=Android -cook -build
   -stage -package -archive -architectures=ARM64 …` and upload the APK artifact.
3. If no engine is found → **the job fails with a message that states the
   problem and the three real routes** (self-hosted runner with UE installed;
   set `UNREAL_ENGINE_PATH`; use a UE-capable hosted runner service). No
   skipping, no placeholder secrets, no claim that UE exists on the runner.

No Unity, no Unity licence, no Unity activation anywhere in CI. The old
`unity-activation.yml` workflow is deleted.

---

## 6. Deletion order (Unity files)

Unity files are removed only after the corresponding Unreal replacement exists
in the repository:

1. Create `Shadowbound.uproject`, `Config/`, `Source/**` (game module) — phase-1 runtime.
2. Create `Source/ShadowboundCore/**` and make `Tools/test-core-cpp.sh` pass
   (the ported slice compiles and its tests run in this environment).
3. Move `Assets/Scripts/Core` → `Core/`, point the existing C# harness at it,
   and confirm all 563 C# tests still pass.
4. **Then** delete: `Assets/Scripts/Game/**`, `Assets/Scripts/Editor/**`,
   `Assets/**` leftovers (asmdefs), `Packages/`, `ProjectSettings/`,
   `Tests/Shadowbound.UnityCheck/**`, `Tools/check-unity-layer.sh`,
   `.github/workflows/unity-activation.yml`, and the Unity jobs in `ci.yml`.
5. Update `.gitignore`, `README.md`, `Documentation/*`, `.devcontainer` labels.

Deletion is recorded in the final report (§7).

## 7. Verification contract

What **can** be verified in an environment without Unreal Engine, and therefore
what will be run and reported:

| Check | Command | Claim it supports |
| --- | --- | --- |
| C# core purity + compile + 563 tests | `bash Tools/test-core.sh` | gameplay logic still intact and green |
| C++ core gate (no engine includes) | `bash Tools/check-core-purity.sh` | module boundary holds |
| C# syntax gate | `bash Tools/check-syntax.sh` | remaining C# parses |
| C++ core tests (compiled with g++, no UE) | `bash Tools/test-core-cpp.sh` | the ported slice compiles and behaves |
| Untracked-Unity leftovers | `bash Tools/check-unreal-layout.sh` | project/module/manifest consistency |
| Unreal build / cook / APK | `bash Tools/build-android.sh` | **only if an engine exists** — otherwise it fails loudly, and that failure is reported as-is |

**No Unreal build or APK will be claimed unless actual build output appears.**
In the environment this plan was executed in, there is no Unreal Engine
installed, so §7's last row is expected to fail with its explicit message; that
failure is the honest result, and it is reported, not hidden.
