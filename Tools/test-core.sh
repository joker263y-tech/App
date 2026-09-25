#!/usr/bin/env bash
# Runs the pure-C# core test suite. This is the project's primary quality gate.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$SCRIPT_DIR/.."

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

DOTNET="${DOTNET:-dotnet}"
if ! command -v "$DOTNET" >/dev/null 2>&1; then
    if [ -x "$HOME/.dotnet/dotnet" ]; then
        DOTNET="$HOME/.dotnet/dotnet"
    else
        echo "test-core: FAIL - dotnet SDK not found on PATH" >&2
        exit 1
    fi
fi

echo "==> Core purity gate"
bash "$ROOT/Tools/check-core-purity.sh"

echo ""
echo "==> Building core (netstandard2.1, C# 9 - same constraints as Unity 6)"
"$DOTNET" build "$ROOT/Tests/Shadowbound.Core.Build/Shadowbound.Core.Build.csproj" \
    --nologo -v minimal

echo ""
echo "==> Running core test suite"
"$DOTNET" test "$ROOT/Tests/Shadowbound.Core.Tests/Shadowbound.Core.Tests.csproj" \
    --nologo -v minimal "${@:-}"
