#!/usr/bin/env bash
# -----------------------------------------------------------------------------
# Enforces the architectural boundary of the Shadowbound core.
#
# The core under Core/ is deterministic, engine-free game logic. It is the
# source of truth for every game rule and it must stay free of engine
# dependencies - Godot, Unity and Unreal alike - or the whole reason it exists
# (that its rules can be compiled and tested without an engine) is lost. The
# Godot presentation layer under scripts/ references the core; the core never
# references anything back.
#
#   * no Godot API (Godot.*, Node/SceneTree types, [Export] ...)
#   * no UnityEngine/UnityEditor, no UNITY_ conditional compilation
#   * no Unreal types or reflection macros
#
# Comments are stripped before matching, so the core is free to *document* the
# engine boundary without tripping this gate.
#
# Exit 0 = the core is pure. Exit 1 = it leaked an engine dependency.
# -----------------------------------------------------------------------------
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$SCRIPT_DIR/.."

CORE_DIR="$ROOT/Core"

if [ ! -d "$CORE_DIR" ]; then
    echo "check-core-purity: FAIL - core directory not found at $CORE_DIR" >&2
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

                p = index(line, "//")
                if (p > 0) line = substr(line, 1, p - 1)

                if (line ~ /[^ \t]/) print FILENAME ":" FNR ":" line
            }
        '
}

CS_CODE="$(strip_comments_and_print_code "$CORE_DIR" -name '*.cs')"

# 1. No engine namespaces, types or package references.
if matches=$(printf '%s\n' "$CS_CODE" | grep -E '(^|[^A-Za-z0-9_])(Godot|GodotSharp|UnityEngine|UnityEditor|Unity|Unreal|UnrealBuildTool)[._]' || true); [ -n "$matches" ]; then
    report_failure "core must not reference any engine API" "$matches"
fi

if matches=$(printf '%s\n' "$CS_CODE" | grep -E '^[^:]*:[0-9]+:[[:space:]]*using[[:space:]]+(Godot|Unity)' || true); [ -n "$matches" ]; then
    report_failure "core must not import engine namespaces" "$matches"
fi

# 2. No conditional compilation that could hide engine code behind a symbol.
if matches=$(printf '%s\n' "$CS_CODE" | grep -E '^[^:]*:[0-9]+:[[:space:]]*#[[:space:]]*(if|elif).*\b(UNITY_|GODOT)' || true); [ -n "$matches" ]; then
    report_failure "core must not branch on engine defines" "$matches"
fi

# 3. No engine inspector attributes, which only exist inside an engine.
if matches=$(printf '%s\n' "$CS_CODE" | grep -E '^[^:]*:[0-9]+:.*\[(SerializeField|RequireComponent|AddComponentMenu|ExecuteInEditMode|CreateAssetMenu|ExecuteAlways|Export|Tool|GlobalClass)\]' || true); [ -n "$matches" ]; then
    report_failure "core must not use engine inspector attributes" "$matches"
fi

if [ "$failures" -ne 0 ]; then
    echo "" >&2
    echo "The core layer is portable, deterministic game logic that must be" >&2
    echo "testable without an engine. Move engine-dependent code into the" >&2
    echo "Godot presentation layer under scripts/." >&2
    exit 1
fi

echo "check-core-purity: OK (C# core in Core/ is engine-free)"
