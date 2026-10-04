#!/usr/bin/env bash
# Runs the Godot-side checks: build the C# assembly, import the project, and run
# the headless smoke test that exercises the core through the engine.
#
# Usage:  bash Tools/test-godot.sh [/path/to/godot]
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

GODOT="${1:-${GODOT_BIN:-}}"
if [ -z "$GODOT" ]; then
    for candidate in godot godot4 Godot; do
        if command -v "$candidate" >/dev/null 2>&1; then
            GODOT="$candidate"
            break
        fi
    done
fi

if [ -z "$GODOT" ] || ! command -v "$GODOT" >/dev/null 2>&1; then
    echo "test-godot: FAIL - Godot not found. Pass the path as the first argument or set GODOT_BIN." >&2
    exit 1
fi

echo "==> Building Godot C# assembly"
dotnet build "$ROOT/Shadowbound.csproj" --nologo -v minimal

echo ""
echo "==> Importing Godot project"
"$GODOT" --headless --path "$ROOT" --import

echo ""
echo "==> Running headless smoke test"
"$GODOT" --headless --path "$ROOT" res://Tests/Godot/GodotSmoke.tscn
