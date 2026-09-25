# Building and running

## Requirements

| Task | Needs |
| --- | --- |
| Run the core test suite | .NET SDK 8.0 or newer |
| Open the project, press Play | Unity 6 (6000.0 LTS or newer) |
| Produce an APK | Unity 6 **with Android Build Support** (SDK, NDK, JDK) |

The test suite needs no Unity. Unity needs no .NET SDK. They are independent.

## 1. Run the checks (no Unity required)

```bash
bash Tools/test-core.sh          # purity gate, compile, 511 tests
bash Tools/check-syntax.sh       # every C# file parses at C# 9
bash Tools/check-unity-layer.sh  # Game assembly type-checks against Unity
```

`test-core.sh` runs the purity gate, compiles the core against `netstandard2.1`
with C# 9 — Unity 6's exact constraints — and executes the xUnit suite.

`check-unity-layer.sh` type-checks the Game assembly against real Unity
reference assemblies, which is how the runtime path is verified to compile
without opening Unity. It needs network access on the first run, to restore
those reference assemblies.

Expected output:

```
check-core-purity: OK (core is engine-free)
Build succeeded.  0 Warning(s)  0 Error(s)
Passed!  - Failed: 0, Passed: 511, Skipped: 0, Total: 511
check-syntax: OK (46 files parse as C# 9, no syntax errors)
Build succeeded.  0 Warning(s)  0 Error(s)
```

Individually:

```bash
dotnet test Tests/Shadowbound.Core.Tests
```

## 2. Open the project in Unity

1. Unity Hub → **Add** → *Add project from disk* → select this folder.
2. Open with **Unity 6**. The first import resolves packages and takes several
   minutes. The Console should report **no compile errors**.
3. Run **`Shadowbound → Set Up Project`**.

That one menu item does three things:

- Creates `Assets/Scenes/Arena.unity` containing a single `Game` object, and
  registers it as the build scene.
- Configures the Android player: landscape, IL2CPP, ARM64, Vulkan with an
  OpenGL ES 3 fallback, linear colour space, minimum API 24.
- Validates the authored content and logs a summary, flagging any loot entry,
  creature or quest that points at something that does not exist.

4. Press **Play**.

There is no separate "generate assets" step. The game builds itself at runtime —
arena geometry, materials, enemies, camera and HUD — so the scene file stays
almost empty and nothing can drift out of sync with the code.

### Other menu items

| Menu item | Does |
| --- | --- |
| `Shadowbound → Set Up Project` | All three steps below, in order |
| `Shadowbound → Create Playable Scene` | Recreates the scene and build settings |
| `Shadowbound → Configure for Android` | Player settings only |
| `Shadowbound → Validate Content` | Cross-checks all authored content |
| `Shadowbound → Build Android APK` | Builds to `Builds/Shadowbound.apk` |
| `Shadowbound → Log Save Location` | Prints the save directory to the Console |

## 3. Controls

### Keyboard and mouse (Editor, desktop)

| Input | Action |
| --- | --- |
| `W` `A` `S` `D` / arrows | Move, relative to the camera |
| Mouse drag (right button) | Look |
| `Q` / `E` | Turn the camera |
| Left mouse (hold) | Ember Edge |
| `1` – `5` | Abilities 0–4 |
| `Space` | Ashstep |
| `Esc` / `Tab` | Open and close the game menu |

Movement is camera-relative: forward moves the Warden away from the camera, not
along a fixed world axis.

### Getting around

The game opens in the Last Ember Camp, which is a safe hub with nothing to fight.
The opening quest asks you to reach the Grey Wilds, and there are two ways out:

- **Walk into the gate** at the far end of the arena. It leads to the first
  neighbouring region the world graph will allow, preferring somewhere new over
  the way you came.
- **Use the menu's The World section** to travel to any region that is next door
  or that you have already visited, provided its chapter gate is open.

Both paths apply the same rule, and it lives in the tested core rather than in
the menu. Travelling replaces the region's creatures but keeps your character:
level, bag, equipment and health all come with you.

### Touch (Android)

| Control | Action |
| --- | --- |
| Left half of the screen | Movement stick, centred wherever your thumb lands |
| Right half | Drag to look |
| Bottom-right buttons | Abilities 1–5, each showing its cooldown as a radial sweep |
| Top-right button | Open the game menu |

The stick is anchored to the touch point rather than to a fixed spot on screen,
because a fixed position is unusable for anyone holding the device differently.
It has a small dead zone so a resting thumb does not drift the character.

## 4. Build the APK

With the Android module installed:

**`Shadowbound → Build Android APK`**

Output: `Builds/Shadowbound.apk`. On success the Console prints the path, size
and build time. On failure it prints the reason — most commonly that the Android
module (SDK, NDK, JDK) is missing for the installed Unity version, or that the
keystore is not configured.

To install and run on a connected device:

```bash
adb install -r Builds/Shadowbound.apk
```

### Android build configuration chosen

| Setting | Value | Why |
| --- | --- | --- |
| Scripting backend | IL2CPP | Mono is too slow for this workload on device |
| Architecture | ARM64 only | Required for Play Store submission; halves build size |
| Graphics API | Vulkan, then OpenGL ES 3 | Vulkan is faster on the target hardware, GLES3 is the fallback if a driver misbehaves |
| Colour space | Linear | The game is dark; banding in shadows is the first visible artefact |
| Orientation | Landscape, both ways | A third-person action game is unplayable in portrait |
| Minimum API | 24 (Android 7.0) | Covers the target device range without legacy branches |

## Continuous integration (GitHub Actions)

`.github/workflows/ci.yml` runs two jobs:

| Job | When | What it does |
| --- | --- | --- |
| **verify** | Every push and pull request | The three verification gates: core purity + test suite, the C# 9 syntax gate, and the Unity layer type-check. About a minute, no Unity licence needed. |
| **android** | Pushes to `main` and manual dispatch | Builds the real APK with Unity 6 (`6000.0.32f1` from `ProjectVersion.txt`) inside game-ci's container, then uploads `Shadowbound.apk` as a downloadable artifact. 15–40 minutes. |

The android job **skips** (not fails) until Unity licence secrets are configured.
To enable it, add repository secrets under *Settings → Secrets and variables →
Actions*:

| Licence type | Secrets |
| --- | --- |
| Personal (free) | `UNITY_LICENSE` (contents of the `.ulf` licence file), `UNITY_EMAIL`, `UNITY_PASSWORD` |
| Professional | `UNITY_SERIAL`, `UNITY_EMAIL`, `UNITY_PASSWORD` |

Getting the personal licence file needs a one-time manual activation — game-ci's
instructions are at <https://game.ci/docs/github/activation>. Use a Unity
password without special characters; the activation step is known to stumble on
them.

The build runs `Shadowbound.Editor.ProjectSetup.BuildAndroidFromCommandLine`,
which does the same work as **Shadowbound → Build Android APK** but **throws on
content errors or build failure** — a CI job that logs an error and exits
successfully ships a broken APK with a green tick next to it. The APK is signed
with Unity's debug keystore (no custom keystore is configured), which is enough
for `adb install` testing; release signing is a later step.

## The in-game menu

Opened with `Esc` / `Tab`, or the button in the top-right corner of the screen.
The world freezes while it is open, so nothing swings at you while you read it.

The menu is **paged**: ATTRIBUTES, THE WORLD and SAVES open their own screens,
each with a `BACK` row. Everything stays on one screen's worth of rows, so no
action can hide below the bottom edge on a small display.

| Row / page | What it does |
| --- | --- |
| **Resume** | Closes the menu and unfreezes the world. Always the first row, on every page. |
| **Equipped** | Shows each slot and what is in it. Tapping one takes the item off and returns it to the bag. |
| **Carried** | What is in the bag: `Use` rows for consumables with their effects spelled out (e.g. `+120 health, +40 stamina`), and `Wear` rows for equipment with its stat bonuses. Tapping `Use` drinks it; tapping `Wear` puts it on. |
| **Attributes** | One row per stat: its value now, and what one point would buy (`345 -> 360`). Tapping spends a point; the page is highlighted on the main screen while points are unspent. |
| **The World** | Lists every region. Places you can go are tappable; places you cannot are greyed out with the reason, so a story gate is visible rather than looking like a bug. |
| **Saves** | Lists save slots with the profile, level and playtime. Tapping loads; the trailing `[delete]` wording on a slot marks it as loadable. The last row saves to the current slot. |

Swapping equipment is a single transaction: the incoming item leaves the bag and
whatever it replaced goes back in, so nothing is lost even when the bag is full.
Taking an item off when the bag is full leaves it equipped and says so, rather
than destroying it.

Using a consumable spends the item even when it would heal nothing — effects
clamp rather than refuse, so whether a draught is worth it stays the player's
call. Spent attribute points are part of the save and survive quitting and
reloading.

## Saves

Written to `Application.persistentDataPath/saves/<slot>.shadowbound.json` —
`Shadowbound → Log Save Location` prints the exact directory.

Writes go to a temporary file and are then moved into place, so an interrupted
write cannot leave a truncated file where a complete save used to be. Migration
runs on **load**, not on save, so an old file stays on disk in its original form
until the player actually loads and re-saves it.

The current build auto-saves every 60 seconds to slot `slot-1`.
`GameBootstrap.LoadSaveOnStart` controls whether a launch resumes from it.

## Troubleshooting

**Everything is magenta.** The render pipeline asset is missing. URP is in
`Packages/manifest.json`; if `Assets/Settings/` has no pipeline asset, create one
via *Assets → Create → Rendering → URP Asset* and assign it in
*Project Settings → Graphics*.

**Labels are invisible in the HUD.** Unity renamed its built-in font in 2022.2.
`HudController.ResolveFont` tries both names; if neither resolves, the bars and
buttons still work and only the text is missing.

**The camera goes through walls.** `ThirdPersonCamera.ObstructionMask` is empty
by default, which disables collision. Assign a layer to the arena geometry and
set the mask to enable it.

**Input does nothing.** `PlayerInputDriver.DeviceInputEnabled` is set from
`Application.isMobilePlatform`. On a device it is false and the on-screen
controls drive; in the Editor it is true and the keyboard does. If you are
testing touch in the Editor, set it false on the `Game` object.

**The APK refuses to install.** The device needs Android 7.0 or newer and an
ARM64 CPU.
