#!/usr/bin/env bash
#
# Fail if any tracked file under Assets/ has no declared licence, if a file declared as the
# project's own work is in fact derived from contributed art, or if non-redistributable
# material has reached version control.
#
# Nothing else in the loop catches a licensing error. The compiler does not, the tests do
# not, and the release script's staleness check does not -- the only detector is a person who
# happens to think about it, which is why a wrong claim once shipped to mod.io. This is that
# detector, written down.
#
# The rule it enforces is in
# docs/solutions/conventions/a-licence-notice-travels-with-the-asset-not-the-repo.md: what
# decides the licence is what a file was made FROM. Derivation is detected mechanically, by
# looking for a licensed asset's GUID inside another file. Names are not evidence -- a file
# called HRVSTR_something may be ours, and a file called Foo.mat may be built on his textures.
#
# Usage: scripts/validate-provenance.sh [--verbose]
# Exit:  0 all tracked files classified and consistent; 1 otherwise.

set -uo pipefail

VERBOSE=0
[ "${1:-}" = "--verbose" ] && VERBOSE=1

ROOT="$(git rev-parse --show-toplevel 2>/dev/null)" || {
    echo "validate-provenance: not inside a git repository" >&2
    exit 1
}
cd "$ROOT" || exit 1

MANIFEST="scripts/provenance.manifest"
[ -f "$MANIFEST" ] || { echo "validate-provenance: missing $MANIFEST" >&2; exit 1; }

problems=0
fail() { printf '  FAIL  %s\n' "$*"; problems=$((problems + 1)); }
note() { [ "$VERBOSE" -eq 1 ] && printf '        %s\n' "$*"; return 0; }

# ---------------------------------------------------------------------------------------
# Read the manifest. Declaration order is precedence: the first matching line wins, so the
# specific cc-by-sa globs must sit above the broad lgpl ones.
# ---------------------------------------------------------------------------------------
licensed_dirs=""
decl_verbs=()
decl_globs=()
not_tracked=()

while IFS= read -r line; do
    line="${line%%$'\r'}"
    case "$line" in ''|'#'*) continue;; esac
    verb="${line%% *}"
    rest="${line#"$verb"}"
    rest="${rest#"${rest%%[! ]*}"}"          # strip leading spaces
    case "$verb" in
        licensed-source) licensed_dirs="$licensed_dirs $rest" ;;
        not-tracked)     not_tracked+=("$rest") ;;
        cc-by-sa|lgpl)   decl_verbs+=("$verb"); decl_globs+=("$rest") ;;
        lgpl-assembly)
            path="${rest%%  --*}"
            reason="${rest#*--}"
            if [ "$path" = "$rest" ] || [ -z "${reason// /}" ]; then
                fail "manifest: lgpl-assembly needs '<path>  -- <reason>': $rest"
                continue
            fi
            case "$path" in *'*'*|*'?'*)
                fail "manifest: lgpl-assembly takes exact paths, not globs: $path"
                continue ;;
            esac
            decl_verbs+=("lgpl-assembly"); decl_globs+=("$path") ;;
        *) fail "manifest: unknown verb '$verb'" ;;
    esac
done < "$MANIFEST"

# ---------------------------------------------------------------------------------------
# Collect the GUIDs the licensed source owns. These are the derivation markers.
# ---------------------------------------------------------------------------------------
guid_re=""
guid_count=0
for dir in $licensed_dirs; do
    [ -d "$dir" ] || { fail "licensed-source directory does not exist: $dir"; continue; }
    while IFS= read -r meta; do
        guid="$(grep -m1 '^guid: ' "$meta" | cut -d' ' -f2 | tr -d '\r')"
        [ -n "$guid" ] || continue
        guid_re="${guid_re:+$guid_re|}$guid"
        guid_count=$((guid_count + 1))
    done < <(find "$dir" -name '*.meta' -type f)
done
[ "$guid_count" -gt 0 ] || fail "no GUIDs found under licensed-source -- the derivation check would pass vacuously"

# ---------------------------------------------------------------------------------------
# Walk the tracked files.
# ---------------------------------------------------------------------------------------
n_ccbysa=0; n_lgpl=0; n_assembly=0

while IFS= read -r f; do
    # Non-redistributable material, anywhere in the tree.
    for glob in "${not_tracked[@]}"; do
        # shellcheck disable=SC2254
        case "$f" in $glob) fail "must not be tracked (non-redistributable): $f";; esac
    done

    case "$f" in Assets/*) ;; *) continue;; esac
    case "$f" in *.meta) continue;; esac          # a .meta inherits its asset's class

    verb=""
    for i in "${!decl_globs[@]}"; do
        # shellcheck disable=SC2254
        case "$f" in ${decl_globs[$i]}) verb="${decl_verbs[$i]}"; break;; esac
    done

    if [ -z "$verb" ]; then
        fail "no licence declared: $f"
        continue
    fi

    derived=0
    if [ -n "$guid_re" ] && grep -aqE "$guid_re" "$f" 2>/dev/null; then
        derived=1
    fi

    case "$verb" in
        cc-by-sa)      n_ccbysa=$((n_ccbysa + 1)); note "cc-by-sa      $f" ;;
        lgpl-assembly) n_assembly=$((n_assembly + 1)); note "lgpl-assembly $f"
            [ "$derived" -eq 1 ] || fail "declared lgpl-assembly but references no licensed asset: $f" ;;
        lgpl)          n_lgpl=$((n_lgpl + 1)); note "lgpl          $f"
            [ "$derived" -eq 0 ] || fail "declared lgpl but references a licensed asset's GUID -- it is an adaptation, or it needs an lgpl-assembly line with a reason: $f" ;;
    esac
done < <(git ls-files)

# ---------------------------------------------------------------------------------------
if [ "$problems" -gt 0 ]; then
    echo "FAIL: $problems provenance problem(s)."
    echo "      Decide the licence from what the file was made FROM, then declare it in $MANIFEST."
    exit 1
fi

echo "PASS: provenance declared for all tracked files under Assets/ (cc-by-sa $n_ccbysa, lgpl $n_lgpl, assembly $n_assembly; $guid_count licensed GUIDs)."
