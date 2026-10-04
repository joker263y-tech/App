#!/usr/bin/env bash
# -----------------------------------------------------------------------------
# Compiles and runs the C++ core test suite.
#
# The ported game rules live in Source/ShadowboundCore as engine-free C++:
# no Unreal headers, standard library only. That is what makes it possible to
# compile them with a plain C++ compiler and execute their tests here, without
# Unreal Engine installed - the same standard the C# suite holds the original
# core to.
#
# The only file excluded from the standalone build is ShadowboundCoreModule.cpp
# (module registration), which by design includes one engine header.
#
# Usage:  bash Tools/test-core-cpp.sh
# -----------------------------------------------------------------------------
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$SCRIPT_DIR/.."

CXX="${CXX:-g++}"
if ! command -v "$CXX" >/dev/null 2>&1; then
    echo "test-core-cpp: FAIL - C++ compiler not found (set CXX)" >&2
    exit 1
fi

OUT_DIR="$ROOT/Tests/ShadowboundCore.Cpp/bin"
mkdir -p "$OUT_DIR"

echo "==> Engine-free gate (Source/ShadowboundCore must not include engine headers)"
bash "$SCRIPT_DIR/check-core-purity.sh"

echo ""
echo "==> Building C++ core tests ($CXX -std=c++17, warnings are errors)"

SOURCES=()
while IFS= read -r file; do
    case "$file" in
        *ShadowboundCoreModule.cpp) continue ;; # requires Unreal Engine headers
    esac
    SOURCES+=("$file")
done < <(find "$ROOT/Source/ShadowboundCore" -name '*.cpp' -type f | sort)

while IFS= read -r file; do
    SOURCES+=("$file")
done < <(find "$ROOT/Tests/ShadowboundCore.Cpp" -maxdepth 1 -name '*.cpp' -type f | sort)

"$CXX" -std=c++17 -O1 -Wall -Wextra -Werror \
    -I"$ROOT/Source/ShadowboundCore/Public" \
    -I"$ROOT/Tests/ShadowboundCore.Cpp" \
    "${SOURCES[@]}" \
    -o "$OUT_DIR/shadowbound-core-tests"

echo ""
echo "==> Running C++ core test suite"
"$OUT_DIR/shadowbound-core-tests"
