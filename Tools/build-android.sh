#!/usr/bin/env bash
# -----------------------------------------------------------------------------
# Exports the Godot Android ARM64 package from the command line, without opening
# the editor.
#
# Target (matches the project's existing Android settings):
#   * Engine       Godot 4.5 (Mono/.NET build, because the game logic is C#)
#   * Architecture Android ARM64 (arm64-v8a)
#   * Orientation  Landscape (a project setting)
#   * Package      com.shadowbound.thelastnight
#
# Unlike the engine this project used to target, Godot and its export templates
# are freely downloadable, so a real APK on a stock GitHub runner is achievable.
# This script still NEVER fakes one: it either produces an APK that passes
# validation, or it exits non-zero and says why.
#
# Outcome contract (the workflow classifies on these):
#   exit 0  -> BUILD SUCCESS, and an APK that passed validation
#   exit 1  -> BUILD FAILURE (the toolchain was present but the export failed)
#   exit 3  -> ENVIRONMENT LIMITATION (Godot or the Android toolchain is absent)
#
# Usage:  bash Tools/build-android.sh [OutputApk]
# -----------------------------------------------------------------------------
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

OUTPUT="${1:-$ROOT/build/android/shadowbound.apk}"
EXIT_ENV=3

# A release APK needs a release keystore. This project ships none, so the default
# is a debug-signed APK, which is installable and needs no secret. Set
# SHADOWBOUND_RELEASE=1 (with a keystore configured) to export a release build.
EXPORT_MODE="${SHADOWBOUND_EXPORT_MODE:-debug}"

die_env() {
    echo "build-android: ENVIRONMENT LIMITATION - $1" >&2
    exit "$EXIT_ENV"
}

die_build() {
    echo "build-android: BUILD FAILURE - $1" >&2
    exit 1
}

# -----------------------------------------------------------------------------
# Locate the Godot editor binary.
# -----------------------------------------------------------------------------
GODOT="${GODOT_BIN:-}"
if [ -z "$GODOT" ]; then
    for candidate in godot godot4 Godot; do
        if command -v "$candidate" >/dev/null 2>&1; then
            GODOT="$candidate"
            break
        fi
    done
fi

if [ -z "$GODOT" ] || ! command -v "$GODOT" >/dev/null 2>&1; then
    die_env "Godot was not found. Set GODOT_BIN=/path/to/godot (the Mono/.NET build of Godot 4.5)."
fi

echo "==> Godot: $GODOT"
"$GODOT" --version || die_env "Could not run $GODOT --version."

# -----------------------------------------------------------------------------
# Android toolchain. Godot 4.5 needs the Android SDK, a JDK (17 recommended)
# and the Android export templates for the installed Godot version.
# -----------------------------------------------------------------------------
echo "==> Android toolchain"
for var in ANDROID_HOME ANDROID_SDK_ROOT JAVA_HOME NDK_HOME; do
    printf '    %-16s %s\n' "$var" "${!var:-<unset>}"
done

ANDROID_SDK="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}"
if [ -z "$ANDROID_SDK" ]; then
    die_env "ANDROID_HOME/ANDROID_SDK_ROOT is not set. Godot needs the Android SDK (platform-tools, build-tools 35.0.1, platform 35, cmdline-tools)."
fi

if [ ! -d "$ANDROID_SDK" ]; then
    die_env "ANDROID_HOME points at '$ANDROID_SDK', which does not exist."
fi

if [ -z "${JAVA_HOME:-}" ]; then
    die_env "JAVA_HOME is not set. Godot needs a JDK (OpenJDK 17 recommended)."
fi

if [ ! -d "$ROOT/scenes" ] || [ ! -f "$ROOT/project.godot" ]; then
    die_build "the Godot project is incomplete (project.godot or scenes/ is missing)."
fi

# -----------------------------------------------------------------------------
# Godot keeps the Android SDK and JDK paths in its EDITOR settings, not in the
# project. A stale value there would make the export fail for a reason that has
# nothing to do with this repository, so point them where this build found them.
# -----------------------------------------------------------------------------
set_editor_setting() {
    local key="$1"
    local value="$2"

    if grep -q "^${key} = " "$SETTINGS_FILE"; then
        sed -i "s#^${key} = .*#${key} = \"${value}\"#" "$SETTINGS_FILE"
    else
        printf '%s = "%s"\n' "$key" "$value" >> "$SETTINGS_FILE"
    fi
}

GODOT_CONFIG_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/godot"
mkdir -p "$GODOT_CONFIG_DIR"

# Editor mode materialises editor_settings-4.x.tres if it does not exist yet.
"$GODOT" --headless --path "$ROOT" --import >/dev/null 2>&1 || true

SETTINGS_FILE="$(find "$GODOT_CONFIG_DIR" -maxdepth 1 -name 'editor_settings-*.tres' 2>/dev/null | head -1 || true)"

if [ -n "$SETTINGS_FILE" ]; then
    set_editor_setting "export/android/android_sdk_path" "$ANDROID_SDK"

    if [ -n "${JAVA_HOME:-}" ]; then
        set_editor_setting "export/android/java_sdk_path" "$JAVA_HOME"
    fi

    echo "==> Godot editor settings: $SETTINGS_FILE (Android SDK: $ANDROID_SDK)"
else
    echo "build-android: WARNING - could not locate Godot's editor settings; relying on them being correct already." >&2
fi

# -----------------------------------------------------------------------------
# Export. Godot refuses to export without the matching export templates, which
# is reported as a build failure here so it is not mistaken for a missing SDK.
# -----------------------------------------------------------------------------
mkdir -p "$(dirname "$OUTPUT")"
rm -f "$OUTPUT"

echo "==> Importing project resources"
set +e
"$GODOT" --headless --path "$ROOT" --import
IMPORT_STATUS=$?
set -e

if [ "$IMPORT_STATUS" -ne 0 ]; then
    die_build "godot --import exited with status $IMPORT_STATUS."
fi

if [ "$EXPORT_MODE" = "release" ]; then
    echo "==> Exporting Android release APK (ARM64)"
    EXPORT_FLAG="--export-release"
else
    echo "==> Exporting Android debug APK (ARM64, debug-signed)"
    EXPORT_FLAG="--export-debug"
fi

set +e
"$GODOT" --headless --path "$ROOT" "$EXPORT_FLAG" "Android" "$OUTPUT"
EXPORT_STATUS=$?
set -e

if [ "$EXPORT_STATUS" -ne 0 ]; then
    die_build "godot --export-release exited with status $EXPORT_STATUS (see its output above; missing export templates or SDK packages are the usual cause)."
fi

# -----------------------------------------------------------------------------
# Validate the APK. A build that reached here but produced nothing usable is a
# failure, not a success.
# -----------------------------------------------------------------------------
if [ ! -f "$OUTPUT" ]; then
    die_build "Godot reported success but no .apk was produced at $OUTPUT."
fi

APK_SIZE_BYTES="$(stat -c '%s' "$OUTPUT" 2>/dev/null || stat -f '%z' "$OUTPUT")"
APK_SIZE_MB=$(( APK_SIZE_BYTES / 1024 / 1024 ))

if [ "$APK_SIZE_BYTES" -lt 1048576 ]; then
    die_build "APK at $OUTPUT is only ${APK_SIZE_BYTES} bytes; a real Godot ARM64 package is far larger."
fi

if command -v unzip >/dev/null 2>&1; then
    # Captured once and matched with bash patterns: piping `unzip -l` into
    # `grep -q` would make grep close the pipe early, and under `pipefail` that
    # SIGPIPE would be read as a failure of the listing itself.
    APK_LISTING="$(unzip -l "$OUTPUT" 2>/dev/null || true)"

    case "$APK_LISTING" in
        *"lib/arm64-v8a/"*) ;;
        *) die_build "APK at $OUTPUT has no lib/arm64-v8a contents; it is not an ARM64 package." ;;
    esac

    case "$APK_LISTING" in
        *"AndroidManifest.xml"*) ;;
        *) die_build "APK at $OUTPUT has no AndroidManifest.xml." ;;
    esac
else
    echo "build-android: WARNING - unzip unavailable, skipping internal APK inspection" >&2
fi

echo ""
echo "build-android: BUILD SUCCESS"
echo "APK_PATH=$OUTPUT"
echo "APK_SIZE_BYTES=$APK_SIZE_BYTES"
echo "APK_SIZE_MB=$APK_SIZE_MB"
exit 0
