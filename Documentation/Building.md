# Building and running

## Requirements

| Task | Needs |
| --- | --- |
| Run the core test suite | .NET SDK 9.0 or newer |
| Build and run the game | **Godot 4.5 (.NET)** — the Mono/.NET editor build |
| Produce an APK | Godot 4.5 (.NET) + Android SDK (build-tools 35.0.1, platform 35, platform-tools) + OpenJDK 17 |

The core test suite needs no engine. Godot needs no Android SDK until you export.

## 1. Run the checks (no Godot required)

```bash
bash Tools/check-core-purity.sh   # the core must stay engine-free
bash Tools/test-core.sh           # purity gate, compile, 563 tests
bash Tools/check-godot-project.sh # project layout is complete and engine-clean
```

`test-core.sh` runs the purity gate, compiles the core as a portable library, and
runs the xUnit suite. Expected output:

```
check-core-purity: OK (C# core in Core/ is engine-free)
Passed!  - Failed: 0, Passed: 563, Skipped: 0, Total: 563
check-godot-project: OK (Godot 4.5 project layout is complete and engine-clean)
```

## 2. Build and run in Godot

1. Install **Godot 4.5 (.NET)** from <https://godotengine.org/download> (the .NET
   build, not the standard build).
2. Open `project.godot`. Godot builds the C# solution automatically; you can also
   build it directly with `dotnet build Shadowbound.csproj`.
3. Press **F5**. The game boots in the **Grey Wilds**.

### Headless (no display)

```bash
godot --headless --path . --import                # import resources
godot --headless --path . --quit-after 300        # run the main scene briefly
godot --headless --path . res://Tests/Godot/GodotSmoke.tscn   # the smoke test
```

`Tools/test-godot.sh [path/to/godot]` runs all three.

## 3. Controls

### Keyboard and mouse

| Input | Action |
| --- | --- |
| `W` `A` `S` `D` | Move, relative to the camera |
| Mouse motion | Look |
| `Q` / `E` | Turn the camera |
| Left mouse (hold) | Ember Edge |
| `1` – `5` | Abilities 0–4 |
| `Space` | Ashstep |
| `Esc` / `Tab` | Open / close the menu |

Movement is camera-relative: forward moves the Warden away from the camera, not
along a fixed world axis.

### Touch (Android)

| Control | Action |
| --- | --- |
| Left half of the screen | Movement stick, centred wherever your thumb lands |
| Right half | Drag to look |
| Bottom-right buttons | Abilities 1–5, each showing its cooldown as a fill |
| `MENU`, top right | Open the menu (a phone has no Escape key) |

The stick is anchored to the touch point rather than a fixed spot, because a fixed
position is unusable for anyone holding the device differently. The same rectangles
are used to draw the buttons and to hit-test touches — one source of truth.

### The menu

Opened with `Esc`/`Tab` or the on-screen `MENU` button; it pauses the simulation
while open. Pages: equipment and carried items (equip/unequip, use consumables),
**attributes** (spend points), **the world** (travel to any region the world graph
allows), and **saves** (save/load). It is the piece that makes the systems
reachable: without it there would be no way to wear an item, drink a draught, spend
a point or resume a save.

## 4. Build the Android APK

The build runs entirely from the command line, without opening the editor:

```bash
bash Tools/build-android.sh                 # -> build/android/shadowbound.apk
GODOT_BIN=/path/to/godot bash Tools/build-android.sh
```

The script locates Godot (`GODOT_BIN`, or `godot` on `PATH`), points Godot's editor
settings at the `ANDROID_HOME` SDK, imports the project, exports the Android preset,
and **validates the APK** (size, `lib/arm64-v8a`, `AndroidManifest.xml`). It exits 0
only when a real APK passed validation.

Outcome contract (the workflow classifies on these):

| Exit | Meaning |
| --- | --- |
| 0 | **BUILD SUCCESS** — a validated APK was produced |
| 1 | **BUILD FAILURE** — the toolchain was present but the export failed |
| 3 | **ENVIRONMENT LIMITATION** — Godot or the Android SDK is missing |

### What was actually produced (measured)

Running the script on a machine with Godot 4.5 (.NET) and the Android SDK produced:

```
build-android: BUILD SUCCESS
APK_PATH=build/android/shadowbound.apk
APK_SIZE_MB=98
```

```
package: name='com.shadowbound.thelastnight' versionCode='1' versionName='1.0.0'
sdkVersion:'24'  targetSdkVersion:'35'
launchable-activity: name='com.godot.game.GodotApp' label='Shadowbound: The Last Night'
android:screenOrientation = 0   (landscape)
lib/arm64-v8a/libgodot_android.so  (+25 more arm64 libraries)
assets/.godot/mono/publish/arm64/Shadowbound.dll
assets/.godot/mono/publish/arm64/Shadowbound.Core.dll
Signer #1 certificate DN: CN=Godot, OU=Godot Engine, O=Stichting Godot, C=NL
```

### Android configuration chosen

Set in `project.godot` and `export_presets.cfg`:

| Setting | Value | Why |
| --- | --- | --- |
| Package | `com.shadowbound.thelastnight` | Preserved from the previous engines |
| Architecture | ARM64 only (`arm64-v8a=true`, others false) | Required for Play Store submission; halves build size |
| Orientation | Landscape (`display/window/handheld/orientation=0`) | A third-person action game is unplayable in portrait |
| Target API | 35 | Preserved from the previous engines; Google Play requires it for new apps |
| Texture compression | ETC2/ASTC (`import_etc2_astc=true`) | **Required** — the Android exporter refuses the project without it |
| Signing | Debug keystore (default) | An installable APK with no secret. Set `SHADOWBOUND_EXPORT_MODE=release` (with a release keystore configured) for a release build |
| Minimum API | Godot's template default is 24 | The previous engines used 26. To restore 26, enable a Gradle build (`gradle_build/use_gradle_build=true`) and set `gradle_build/min_sdk="26"` — Godot rejects that override without a Gradle build. |

## Continuous integration (GitHub Actions)

Two workflows:

| Workflow | When | What it does |
| --- | --- | --- |
| **`ci.yml`** | Every push and pull request | The Godot gates: core purity, 563 core tests, project layout, C# build, headless smoke test. |
| **`android.yml`** | Pushes to `main` and manual dispatch | Installs Godot + export templates + the Android SDK, exports the ARM64 APK, and classifies the outcome. |

There are **no engine licence secrets** anywhere — Godot and its export templates
are free to download, which is what makes a real build on a stock runner possible.

### How `android.yml` classifies its result

| Verdict | Meaning | Job status |
| --- | --- | --- |
| **BUILD SUCCESS** | `Tools/build-android.sh` produced an APK that passed validation. | passes, APK uploaded |
| **BUILD FAILURE** | The toolchain was present but the export failed. | fails |
| **ENVIRONMENT LIMITATION** | The runner lacked Godot or the Android SDK. | warns |

> **Not yet run on GitHub.** The workflow is written and the identical script
> produced a real APK locally, but no GitHub Actions run has happened yet. Treat its
> first run as the thing that proves it.

## Troubleshooting

**"C# project targets 'net8.0' but the export template only supports 'net9.0'."**
Godot 4.5's Android export template bundles .NET 9. `Shadowbound.csproj` targets
`net9.0` for exactly this reason; do not lower it.

**"Cannot export project with preset 'Android' due to configuration errors:
Exporting to Android when using C#/.NET is experimental."** That line is only a
warning; the real cause is one of the others and is usually either
`rendering/textures/vram_compression/import_etc2_astc` being off, or the Android SDK
path in *Godot's editor settings* being wrong. `Tools/build-android.sh` sets the
latter from `ANDROID_HOME` for you.

**"This project contains C# files but no solution file was found."** Godot's .NET
export needs `Shadowbound.sln` to bundle the assembly. It is committed; do not
delete it.

**Godot scans `Core/` as project resources.** `Core/.gdignore` and `Tests/.gdignore`
tell Godot to skip those directories — they are a compiled library and a test
harness, not game resources.

**The build cannot find Godot.** Set `GODOT_BIN` to the Mono/.NET editor binary.
