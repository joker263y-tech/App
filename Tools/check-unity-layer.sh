#!/usr/bin/env bash
#
# Type-checks the Unity layer without a Unity installation.
#
# The Game assembly references UnityEngine, so dotnet cannot normally compile it.
# Tests/Shadowbound.UnityCheck supplies real Unity reference assemblies (via the
# OpenMod.UnityEngine.Redist package) and a shim for the new Input System - which
# has no reference assembly - then compiles the real sources from
# Assets/Scripts/Game against them.
#
# What this proves: every Unity API and every core API the Game layer calls exists
# with the signature used, and the code type-checks.
#
# What it does not prove: that the code behaves correctly at runtime, or that it
# compiles on Unity 6 specifically. The reference assemblies are Unity 2021.3, so
# code that passes here also compiles on Unity 6 unless Unity 6 removed an API.
#
# The Editor assembly is not covered: no UnityEditor reference assembly is
# available. Its content validation now lives in the tested core instead.
#
# This needs network access on first run, to restore the reference assemblies.
#
# Usage:  bash Tools/check-unity-layer.sh
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

echo "==> Unity layer type-check (real Unity reference assemblies)"
dotnet build "$ROOT/Tests/Shadowbound.UnityCheck/Shadowbound.UnityCheck.csproj" \
  --configuration Debug \
  --nologo \
  --verbosity quiet
