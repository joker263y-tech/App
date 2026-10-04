#!/usr/bin/env bash
# -----------------------------------------------------------------------------
# Verifies the repository is a coherent Unreal Engine 5 project - and only an
# Unreal project.
#
# This is the cheap structural gate: it proves the files a build system needs
# are present and wired to each other, and that nothing from the engine this
# project used to use is still lying around to confuse a build or a reader. It
# does NOT compile anything; that is what Tools/build-android.sh does, and it
# needs Unreal Engine installed.
#
# Exit 0 = layout is coherent. Exit 1 = something is missing or left over.
# -----------------------------------------------------------------------------
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

failures=0

fail() {
    echo "check-unreal-layout: FAIL - $1" >&2
    failures=1
}

# -----------------------------------------------------------------------------
# 1. Project descriptor and modules.
# -----------------------------------------------------------------------------
UPROJECT="$ROOT/Shadowbound.uproject"
if [ ! -f "$UPROJECT" ]; then
    fail "Shadowbound.uproject is missing"
else
    for module in ShadowboundCore Shadowbound; do
        if ! grep -q "\"Name\": \"$module\"" "$UPROJECT"; then
            fail "Shadowbound.uproject does not declare the $module module"
        fi
    done

    if ! grep -q '"EnhancedInput"' "$UPROJECT"; then
        fail "Shadowbound.uproject does not enable the EnhancedInput plugin"
    fi
fi

# -----------------------------------------------------------------------------
# 2. Build rules and targets.
# -----------------------------------------------------------------------------
for file in \
    "Source/Shadowbound.Target.cs" \
    "Source/ShadowboundEditor.Target.cs" \
    "Source/Shadowbound/Shadowbound.Build.cs" \
    "Source/ShadowboundCore/ShadowboundCore.Build.cs"; do
    if [ ! -f "$ROOT/$file" ]; then
        fail "missing build file: $file"
    fi
done

# Module registration: the primary game module, and the core module.
if [ ! -f "$ROOT/Source/Shadowbound/Private/ShadowboundModule.cpp" ]; then
    fail "missing Source/Shadowbound/Private/ShadowboundModule.cpp"
fi

if [ ! -f "$ROOT/Source/ShadowboundCore/Private/ShadowboundCoreModule.cpp" ]; then
    fail "missing Source/ShadowboundCore/Private/ShadowboundCoreModule.cpp"
fi

# Every module needs at least one source file, or Unreal refuses to load it.
for dir in "Source/Shadowbound" "Source/ShadowboundCore"; do
    if ! find "$ROOT/$dir" -name '*.cpp' -type f | grep -q .; then
        fail "$dir has no .cpp sources"
    fi
done

# -----------------------------------------------------------------------------
# 3. Configuration.
# -----------------------------------------------------------------------------
for file in Config/DefaultEngine.ini Config/DefaultGame.ini Config/DefaultInput.ini; do
    if [ ! -f "$ROOT/$file" ]; then
        fail "missing config: $file"
    fi
done

if [ -f "$ROOT/Config/DefaultEngine.ini" ]; then
    if ! grep -q 'GlobalDefaultGameMode=/Script/Shadowbound.ShadowboundGameMode' "$ROOT/Config/DefaultEngine.ini"; then
        fail "DefaultEngine.ini does not point at AShadowboundGameMode"
    fi

    if ! grep -q 'GameInstanceClass=/Script/Shadowbound.ShadowboundGameInstance' "$ROOT/Config/DefaultEngine.ini"; then
        fail "DefaultEngine.ini does not point at UShadowboundGameInstance"
    fi

    if ! grep -q 'bBuildForArm64=True' "$ROOT/Config/DefaultEngine.ini"; then
        fail "DefaultEngine.ini does not target Android ARM64"
    fi
fi

if [ -f "$ROOT/Config/DefaultInput.ini" ]; then
    if ! grep -q 'DefaultPlayerInputClass=/Script/EnhancedInput.EnhancedPlayerInput' "$ROOT/Config/DefaultInput.ini"; then
        fail "DefaultInput.ini does not select the Enhanced Input player input class"
    fi
fi

# -----------------------------------------------------------------------------
# 4. No Unity leftovers. A stray Assets/ or ProjectSettings/ folder would make
#    a reader (and some tooling) believe this is still a Unity project.
# -----------------------------------------------------------------------------
for leftover in Assets Packages ProjectSettings Library Temp Logs; do
    if [ -e "$ROOT/$leftover" ]; then
        fail "Unity leftover present: $leftover/"
    fi
done

if find "$ROOT" -name '*.asmdef' -type f | grep -q .; then
    fail "Unity assembly definition (.asmdef) files are still present"
fi

# -----------------------------------------------------------------------------
# 5. No committed Unreal build artifacts. These are generated, large, and
#    machine-specific; they belong in .gitignore.
# -----------------------------------------------------------------------------
for artifact in Binaries Intermediate Saved DerivedDataCache; do
    if [ -e "$ROOT/$artifact" ]; then
        fail "Unreal build artifact present (should be ignored): $artifact/"
    fi
done

if [ "$failures" -ne 0 ]; then
    exit 1
fi

echo "check-unreal-layout: OK (Unreal project layout is complete and Unity-free)"
