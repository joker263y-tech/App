#!/usr/bin/env bash
# -----------------------------------------------------------------------------
# Builds the Android ARM64 package through Unreal Automation Tool, from the
# command line, without opening the editor.
#
# Target (matches UE 5.8's documented Android requirements):
#   * Engine       Unreal Engine 5.8
#   * Architecture Android ARM64 (arm64-v8a)
#   * Graphics     Vulkan (see Config/DefaultEngine.ini)
#   * SDK          target API 35 (minimum install API 26)
#   * NDK          r27c
#   * Build-tools  35.0.1
#   * Java         OpenJDK 21.0.3
#
# This script NEVER fakes an APK. It either produces one and validates it, or it
# exits non-zero and says why. There is no placeholder path.
#
# Outcome contract (the workflow classifies on these):
#   exit 0  -> BUILD SUCCESS, and an APK that passed validation
#   exit 1  -> BUILD FAILURE (an engine was present but the build failed)
#   exit 3  -> ENVIRONMENT LIMITATION (no engine / missing Android toolchain)
#
# Usage:  bash Tools/build-android.sh [ArchiveDir]
# -----------------------------------------------------------------------------
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

PROJECT="$ROOT/Shadowbound.uproject"
ARCHIVE_DIR="${1:-$ROOT/Build/Android}"

EXIT_ENV=3

die_env() {
    echo "build-android: ENVIRONMENT LIMITATION - $1" >&2
    exit "$EXIT_ENV"
}

die_build() {
    echo "build-android: BUILD FAILURE - $1" >&2
    exit 1
}

if [ ! -f "$PROJECT" ]; then
    die_build "Shadowbound.uproject not found at $PROJECT"
fi

# -----------------------------------------------------------------------------
# Locate Unreal Engine. A container image places it at /home/ue4/UnrealEngine.
# -----------------------------------------------------------------------------
find_runuat() {
    local engine_root="$1"

    if [ -n "$engine_root" ] && [ -x "$engine_root/Engine/Build/BatchFiles/RunUAT.sh" ]; then
        echo "$engine_root/Engine/Build/BatchFiles/RunUAT.sh"
        return 0
    fi

    if [ -n "$engine_root" ] && [ -f "$engine_root/Engine/Build/BatchFiles/RunUAT.bat" ]; then
        echo "$engine_root/Engine/Build/BatchFiles/RunUAT.bat"
        return 0
    fi

    return 1
}

RUNUAT=""
ENGINE_ROOT=""
for candidate in \
    "${UNREAL_ENGINE_PATH:-}" \
    "${UE_ROOT:-}" \
    "${UE5_ROOT:-}" \
    "/home/ue4/UnrealEngine" \
    "/opt/UnrealEngine" \
    "$HOME/UnrealEngine" \
    "/usr/local/UnrealEngine"; do

    if [ -n "$candidate" ] && RUNUAT="$(find_runuat "$candidate")"; then
        ENGINE_ROOT="$candidate"
        break
    fi
    RUNUAT=""
done

if [ -z "$RUNUAT" ]; then
    cat >&2 <<'EOF'
build-android: ENVIRONMENT LIMITATION - Unreal Engine was not found.

No APK was produced and nothing was faked. Provide Unreal Engine 5.8 by one of:

  * Export UNREAL_ENGINE_PATH=/path/to/UE_5.8 (a self-hosted runner, or a
    GitHub larger runner), or run inside an Unreal Engine container image
    (which installs it at /home/ue4/UnrealEngine); or
  * Set the repository variable UNREAL_ENGINE_PATH that the workflow reads.

Probed: $UNREAL_ENGINE_PATH, $UE_ROOT, $UE5_ROOT, /home/ue4/UnrealEngine,
/opt/UnrealEngine, $HOME/UnrealEngine, /usr/local/UnrealEngine.
EOF
    exit "$EXIT_ENV"
fi

echo "==> Unreal Automation Tool: $RUNUAT"
echo "==> Engine root: $ENGINE_ROOT"
echo "==> Project: $PROJECT"
echo "==> Archive: $ARCHIVE_DIR"

# -----------------------------------------------------------------------------
# Android toolchain. The engine needs these to cross-compile; report what is
# present so a failure is diagnosable from the log alone.
# -----------------------------------------------------------------------------
echo "==> Android toolchain"
for var in ANDROID_HOME ANDROID_SDK_ROOT NDKROOT NDK_ROOT JAVA_HOME; do
    printf '    %-16s %s\n' "$var" "${!var:-<unset>}"
done

MISSING=""
[ -n "${ANDROID_HOME:-}${ANDROID_SDK_ROOT:-}" ] || MISSING="$MISSING ANDROID_HOME/ANDROID_SDK_ROOT"
[ -n "${NDKROOT:-}${NDK_ROOT:-}" ] || MISSING="$MISSING NDKROOT/NDK_ROOT"
[ -n "${JAVA_HOME:-}" ] || MISSING="$MISSING JAVA_HOME"

if [ -n "$MISSING" ]; then
    die_env "Android SDK/NDK/JDK not configured (missing:$MISSING). UE 5.8 needs SDK 35, NDK r27c, build-tools 35.0.1 and OpenJDK 21.0.3."
fi

rm -rf "$ARCHIVE_DIR"
mkdir -p "$ARCHIVE_DIR"

# -----------------------------------------------------------------------------
# BuildCookRun: compile C++ for Android, cook content, stage and package.
# -----------------------------------------------------------------------------
set +e
"$RUNUAT" BuildCookRun \
    -project="$PROJECT" \
    -noP4 \
    -nocompileeditor \
    -utf8output \
    -platform=Android \
    -clientconfig=Development \
    -cook \
    -build \
    -stage \
    -pak \
    -archive \
    -archivedirectory="$ARCHIVE_DIR" \
    -targetplatform=Android \
    -buildtarget=Shadowbound
RUNUAT_STATUS=$?
set -e

if [ "$RUNUAT_STATUS" -ne 0 ]; then
    die_build "RunUAT BuildCookRun exited with status $RUNUAT_STATUS (see its output above)."
fi

# -----------------------------------------------------------------------------
# Validate the APK. A build that reached here but produced nothing usable is a
# failure, not a success.
# -----------------------------------------------------------------------------
APK="$(find "$ARCHIVE_DIR" -name '*.apk' -type f -print -quit || true)"

if [ -z "$APK" ]; then
    die_build "RunUAT reported success but no .apk was produced under $ARCHIVE_DIR."
fi

APK_SIZE_BYTES="$(stat -c '%s' "$APK" 2>/dev/null || stat -f '%z' "$APK")"
APK_SIZE_MB=$(( APK_SIZE_BYTES / 1024 / 1024 ))

if [ "$APK_SIZE_MB" -lt 20 ]; then
    die_build "APK at $APK is only ${APK_SIZE_MB} MB; a real UE Android package is far larger. This is not a valid package."
fi

if command -v unzip >/dev/null 2>&1; then
    if ! unzip -l "$APK" 2>/dev/null | grep -q 'lib/arm64-v8a/'; then
        die_build "APK at $APK has no lib/arm64-v8a contents; it is not an ARM64 package."
    fi

    if ! unzip -l "$APK" 2>/dev/null | grep -q 'AndroidManifest.xml'; then
        die_build "APK at $APK has no AndroidManifest.xml."
    fi
else
    echo "build-android: WARNING - unzip unavailable, skipping internal APK inspection" >&2
fi

echo ""
echo "build-android: BUILD SUCCESS"
echo "APK_PATH=$APK"
echo "APK_SIZE_BYTES=$APK_SIZE_BYTES"
echo "APK_SIZE_MB=$APK_SIZE_MB"
exit 0
