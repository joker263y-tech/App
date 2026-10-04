#!/usr/bin/env bash
# -----------------------------------------------------------------------------
# Enforces the architectural boundary of the Shadowbound core - in BOTH
# languages it is written in.
#
# The core is deterministic, engine-free game logic. It is written twice during
# this migration: the original C# under Core/ (still the source of the ported
# tests) and the ported C++ under Source/ShadowboundCore. Both must stay free of
# engine dependencies, or the whole reason the core exists - that its rules can
# be compiled and tested without an engine - is lost.
#
#   * C#: no UnityEngine/UnityEditor, no UNITY_ conditional compilation, no
#     Unity inspector attributes.
#   * C++: every quoted include must be a local Sb*.h header. Angle-bracket
#     includes are allowed because the standard library is the only thing the
#     core may reach for. Unreal's reflection macros (UCLASS/UPROPERTY/...) are
#     forbidden too, since they drag in the engine's reflection pipeline.
#
# The single exception in C++ is ShadowboundCoreModule.cpp, which exists purely
# to register the module with Unreal and by design includes one engine header.
# It is excluded here and from the standalone test build for the same reason.
#
# Comments are stripped before matching, so the core is free to *document* the
# engine boundary (and it does) without tripping this gate.
#
# Exit 0 = both cores are pure. Exit 1 = a core leaked an engine dependency.
# -----------------------------------------------------------------------------
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$SCRIPT_DIR/.."

CORE_DIR="$ROOT/Core"
CPP_CORE_DIR="$ROOT/Source/ShadowboundCore"

if [ ! -d "$CORE_DIR" ]; then
    echo "check-core-purity: FAIL - C# core directory not found at $CORE_DIR" >&2
    exit 1
fi

if [ ! -d "$CPP_CORE_DIR" ]; then
    echo "check-core-purity: FAIL - C++ core directory not found at $CPP_CORE_DIR" >&2
    exit 1
fi

failures=0

report_failure() {
    local title="$1"
    local matches="$2"
    echo "check-core-purity: FAIL - $title" >&2
    echo "$matches" >&2
    failures=1
}

# Prints every code line under a directory as "path:line:content", with // and
# /* */ comments removed. Reports original line numbers.
strip_comments_and_print_code() {
    local dir="$1"
    shift
    find "$dir" "$@" -type f -print0 \
        | xargs -0 -r awk '
            FNR == 1 { inblock = 0 }
            {
                line = $0

                # Remove block comments, which may span lines.
                while (1) {
                    if (inblock) {
                        p = index(line, "*/")
                        if (p == 0) { line = ""; break }
                        line = substr(line, p + 2)
                        inblock = 0
                    } else {
                        p = index(line, "/*")
                        if (p == 0) break
                        q = index(substr(line, p), "*/")
                        if (q == 0) {
                            line = substr(line, 1, p - 1)
                            inblock = 1
                            break
                        }
                        line = substr(line, 1, p - 1) substr(line, p + q + 1)
                    }
                }

                # Remove trailing line comments. A "//" inside a string literal
                # would also be cut; the core avoids such literals.
                p = index(line, "//")
                if (p > 0) line = substr(line, 1, p - 1)

                if (line ~ /[^ \t]/) print FILENAME ":" FNR ":" line
            }
        '
}

# =============================== C# core =====================================
# Mirrors the asmdef's "noEngineReferences": true, which only helps someone who
# opens Unity.

CS_CODE="$(strip_comments_and_print_code "$CORE_DIR" -name '*.cs')"

# 1. No Unity engine namespaces, types or package references.
if matches=$(printf '%s\n' "$CS_CODE" | grep -E '(^|[^A-Za-z0-9_])(UnityEngine|UnityEditor|Unity)[._]' || true); [ -n "$matches" ]; then
    report_failure "C# core must not reference any Unity API" "$matches"
fi

if matches=$(printf '%s\n' "$CS_CODE" | grep -E '^[^:]*:[0-9]+:[[:space:]]*using[[:space:]]+Unity' || true); [ -n "$matches" ]; then
    report_failure "C# core must not import Unity namespaces" "$matches"
fi

# 2. No conditional compilation that could hide engine code behind a symbol.
if matches=$(printf '%s\n' "$CS_CODE" | grep -E '^[^:]*:[0-9]+:[[:space:]]*#[[:space:]]*(if|elif).*\bUNITY_' || true); [ -n "$matches" ]; then
    report_failure "C# core must not branch on UNITY_ defines" "$matches"
fi

# 3. No Unity inspector attributes, which only exist inside the engine.
if matches=$(printf '%s\n' "$CS_CODE" | grep -E '^[^:]*:[0-9]+:.*\[(SerializeField|RequireComponent|AddComponentMenu|ExecuteInEditMode|CreateAssetMenu|ExecuteAlways)\]' || true); [ -n "$matches" ]; then
    report_failure "C# core must not use Unity inspector attributes" "$matches"
fi

# =============================== C++ core ====================================

CPP_CODE="$(strip_comments_and_print_code "$CPP_CORE_DIR" \
    \( -name '*.h' -o -name '*.cpp' \) ! -name 'ShadowboundCoreModule.cpp')"

# 1. Every quoted include must be a local Sb*.h header. Reaching beyond that
#    means reaching for the engine.
if matches=$(printf '%s\n' "$CPP_CODE" | grep -E '#[[:space:]]*include[[:space:]]*"' \
    | grep -Ev '#[[:space:]]*include[[:space:]]*"Sb[A-Za-z0-9_]*\.h"' || true); [ -n "$matches" ]; then
    report_failure "C++ core may only include its own Sb*.h headers" "$matches"
fi

# 2. No Unreal reflection or module machinery. Module registration lives in the
#    excluded ShadowboundCoreModule.cpp.
if matches=$(printf '%s\n' "$CPP_CODE" | grep -E '\b(GENERATED_BODY|GENERATED_USTRUCT_BODY)\b|(UCLASS|USTRUCT|UENUM|UPROPERTY|UFUNCTION|IMPLEMENT_MODULE|IMPLEMENT_PRIMARY_GAME_MODULE)[[:space:]]*\(' || true); [ -n "$matches" ]; then
    report_failure "C++ core must not use Unreal reflection or module macros" "$matches"
fi

if [ "$failures" -ne 0 ]; then
    echo "" >&2
    echo "The core layer is portable, deterministic game logic that must be" >&2
    echo "testable without an engine. Move engine-dependent code into" >&2
    echo "Source/Shadowbound (Unreal) and keep the cores pure." >&2
    exit 1
fi

echo "check-core-purity: OK (C# core in Core/ and C++ core in Source/ShadowboundCore are engine-free)"
