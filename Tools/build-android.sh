#!/usr/bin/env bash
# -----------------------------------------------------------------------------
# Builds the Android ARM64 package through Unreal Automation Tool, from the
# command line, without opening the editor.
#
# This script is deliberately honest about what it can and cannot do. It NEVER
# pretends to have produced an APK, and it never fakes credentials: if Unreal
# Engine is not present it says so and exits non-zero. That is the contract the
# CI job relies on.
#
# Where Unreal Engine can come from (the runner decides):
#
#   * A SELF-HOSTED runner with Unreal Engine installed, exporting
#     UNREAL_ENGINE_PATH=/path/to/UE_5.6 (the repository variable of the same
#     name is read by .github/workflows/ci.yml).
#   * A HOSTED runner is NOT enough on its own: GitHub's hosted images ship no
#     Unreal Engine, and cloning EpicGames/UnrealEngine needs an Epic-linked
#     GitHub account. A paid UE-capable runner image is the other option.
#
# There are no Unity licence secrets anywhere in this build. Unreal does not
# need one.
#
# Usage:  bash Tools/build-android.sh [ArchiveDir]
# Exit 0 only when an APK was actually produced.
# -----------------------------------------------------------------------------
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

PROJECT="$ROOT/Shadowbound.uproject"
ARCHIVE_DIR="${1:-$ROOT/Build/Android}"

if [ ! -f "$PROJECT" ]; then
    echo "build-android: FAIL - Shadowbound.uproject not found at $PROJECT" >&2
    exit 1
fi

# -----------------------------------------------------------------------------
# Locate Unreal Engine.
# -----------------------------------------------------------------------------
find_runuat() {
    local engine_root="$1"

    # Linux / macOS layout.
    if [ -x "$engine_root/Engine/Build/BatchFiles/RunUAT.sh" ]; then
        echo "$engine_root/Engine/Build/BatchFiles/RunUAT.sh"
        return 0
    fi

    # Windows layout (Git Bash).
    if [ -f "$engine_root/Engine/Build/BatchFiles/RunUAT.bat" ]; then
        echo "$engine_root/Engine/Build/BatchFiles/RunUAT.bat"
        return 0
    fi

    return 1
}

RUNUAT=""

for candidate in \
    "${UNREAL_ENGINE_PATH:-}" \
    "${UE_ROOT:-}" \
    "${UE5_ROOT:-}" \
    "/opt/UnrealEngine" \
    "$HOME/UnrealEngine" \
    "/usr/local/UnrealEngine"; do

    if [ -n "$candidate" ] && RUNUAT="$(find_runuat "$candidate")"; then
        break
    fi

    RUNUAT=""
done

if [ -z "$RUNUAT" ]; then
    cat >&2 <<'EOF'
build-android: FAIL - Unreal Engine was not found, so no APK can be built.

This is expected on a machine without Unreal Engine installed (including
GitHub's standard hosted runners). Nothing was faked; no package was produced.

To make this build, provide Unreal Engine 5.6 and point the build at it:

  * Export UNREAL_ENGINE_PATH=/path/to/UE_5.6 (or set the repository variable
    UNREAL_ENGINE_PATH that .github/workflows/ci.yml reads), then rerun this
    script; or
  * Use a self-hosted CI runner with Unreal Engine installed; or
  * Use a paid, UE-capable hosted runner image.

Locations probed: $UNREAL_ENGINE_PATH, $UE_ROOT, $UE5_ROOT, /opt/UnrealEngine,
$HOME/UnrealEngine, /usr/local/UnrealEngine (looking for
Engine/Build/BatchFiles/RunUAT.sh|bat).
EOF
    exit 1
fi

echo "==> Unreal Automation Tool: $RUNUAT"
echo "==> Project: $PROJECT"
echo "==> Archive: $ARCHIVE_DIR"

rm -rf "$ARCHIVE_DIR"
mkdir -p "$ARCHIVE_DIR"

# -----------------------------------------------------------------------------
# BuildCookRun: cook content, build the Android ARM64 binaries, then stage and
# package. The engine's own tool does the work; this script only drives it and
# verifies the result.
# -----------------------------------------------------------------------------
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
    -buildtarget=Shadowbound \
    -nop4

echo ""
echo "==> Verifying an APK was produced"

APK="$(find "$ARCHIVE_DIR" -name '*.apk' -type f -print -quit || true)"

if [ -z "$APK" ]; then
    echo "build-android: FAIL - RunUAT finished but produced no APK under $ARCHIVE_DIR" >&2
    exit 1
fi

echo "build-android: OK - $APK"
