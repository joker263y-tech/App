#!/usr/bin/env bash
# -----------------------------------------------------------------------------
# Enforces the architectural boundary of the Shadowbound core.
#
# Assets/Scripts/Core/Shadowbound.Core.asmdef sets "noEngineReferences": true,
# which makes Unity itself reject any UnityEngine usage from the core. That
# guarantee only helps someone who opens Unity. This script reproduces the same
# rule for the command-line build and test harness, so the boundary cannot be
# broken by code that "compiles fine here" but explodes in the editor.
#
# Comments are stripped before matching, so the core is free to *document* the
# engine boundary (and it does) without tripping this gate.
#
# Exit 0 = core is pure. Exit 1 = core leaked an engine dependency.
# -----------------------------------------------------------------------------
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CORE_DIR="$SCRIPT_DIR/../Assets/Scripts/Core"

if [ ! -d "$CORE_DIR" ]; then
    echo "check-core-purity: FAIL - core directory not found at $CORE_DIR" >&2
    exit 1
fi

# Prints every core source line that is actual code, as "path:line:content",
# with // and /* */ comments removed. Reports original line numbers.
strip_comments_and_print_code() {
    find "$CORE_DIR" -name '*.cs' -type f -print0 \
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

CODE="$(strip_comments_and_print_code)"
failures=0

report_failure() {
    local title="$1"
    local matches="$2"
    echo "check-core-purity: FAIL - $title" >&2
    echo "$matches" >&2
    failures=1
}

# 1. No Unity engine namespaces, types or package references.
#    Matches UnityEngine, UnityEngine.UI, UnityEditor, Unity.Mathematics, etc.
if matches=$(printf '%s\n' "$CODE" | grep -E '(^|[^A-Za-z0-9_])(UnityEngine|UnityEditor|Unity)[._]' || true); [ -n "$matches" ]; then
    report_failure "core must not reference any Unity API" "$matches"
fi

if matches=$(printf '%s\n' "$CODE" | grep -E '^[^:]*:[0-9]+:[[:space:]]*using[[:space:]]+Unity' || true); [ -n "$matches" ]; then
    report_failure "core must not import Unity namespaces" "$matches"
fi

# 2. No conditional compilation that could hide engine code behind a symbol.
if matches=$(printf '%s\n' "$CODE" | grep -E '^[^:]*:[0-9]+:[[:space:]]*#[[:space:]]*(if|elif).*\bUNITY_' || true); [ -n "$matches" ]; then
    report_failure "core must not branch on UNITY_ defines" "$matches"
fi

# 3. No Unity inspector attributes, which only exist inside the engine.
if matches=$(printf '%s\n' "$CODE" | grep -E '^[^:]*:[0-9]+:.*\[(SerializeField|RequireComponent|AddComponentMenu|ExecuteInEditMode|CreateAssetMenu|ExecuteAlways)\]' || true); [ -n "$matches" ]; then
    report_failure "core must not use Unity inspector attributes" "$matches"
fi

if [ "$failures" -ne 0 ]; then
    echo "" >&2
    echo "The core layer is portable, deterministic game logic that must be" >&2
    echo "testable without an engine. Move engine-dependent code into" >&2
    echo "Assets/Scripts/Game/ and keep the core pure." >&2
    exit 1
fi

echo "check-core-purity: OK (core is engine-free)"
