#!/usr/bin/env bash
#
# Parses every C# file under Assets/Scripts and reports syntax errors.
#
# This exists because the Game and Editor assemblies reference UnityEngine and
# therefore cannot be compiled without a Unity installation. Parsing is not
# compiling, but it catches the mistakes that cost the most time to find inside
# an editor: a stray brace, a truncated method, or a language feature newer than
# the C# version Unity 6 accepts.
#
# Purity is not checked here - that is check-core-purity.sh - and existence of
# the APIs called is not checked at all. Only Unity can prove that.
#
# Usage:  bash Tools/check-syntax.sh
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

echo "==> Syntax gate (All C# under Assets/Scripts)"
dotnet run \
  --project "$ROOT/Tools/SyntaxCheck/SyntaxCheck.csproj" \
  --configuration Release \
  -- "$ROOT/Assets/Scripts"
