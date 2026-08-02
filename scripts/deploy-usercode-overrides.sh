#!/usr/bin/env bash
#
# Install this mod's UserCode overrides into an Eco server, and refresh them against that
# server's __core__ after a game update.
#
# WHY AN OVERRIDE EXISTS AT ALL
#   The Robotic Assembly Line is a vanilla table and only accepts plugin modules its own
#   [AllowPluginModules] names. A mod assembly cannot add to that attribute -- attributes merge
#   across partial declarations only within one assembly. Eco's escape hatch is a whole-file
#   override: a file at the same path under Mods/UserCode/ with `.override` before the extension
#   replaces the __core__ one.
#
# WHY IT MATCHES ON A TAG, NOT OUR TYPE
#   Eco compiles everything under Mods/ into a single assembly whose references are a fixed list
#   of engine DLLs (see Eco.ModKit/RoslynCompiler.cs) -- never the compiled mod DLLs. So an
#   override naming AdvancedElectronicsUpgradeItem fails to compile with CS0246 and takes the
#   server down at boot. AllowPluginModules.Tags is string[], which needs no reference, so the
#   override matches the tag the module carries instead.
#
# WHICH OVERRIDES IT HANDLES
#   Every *.override.cs under EcoServerMod/UserCode/, discovered rather than listed. Each one's
#   __core__ counterpart is its own path with `.override` dropped, so adding a table needs no
#   edit here. All of them carry the same module tag, because the mod ships one plugin module.
#
# Usage:
#   scripts/deploy-usercode-overrides.sh <path-to-eco-server>            # install all
#   scripts/deploy-usercode-overrides.sh <path-to-eco-server> --refresh  # re-derive from __core__
#
# --refresh regenerates every tracked override from the server's current __core__ files, for
# after an Eco update changes a vanilla table. Review the diff before committing it.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
USERCODE_ROOT="$REPO_ROOT/EcoServerMod/UserCode"

MODULE_TAG="AdvancedElectronicsUpgrade"

if [ $# -lt 1 ]; then
    echo "usage: scripts/deploy-usercode-overrides.sh <path-to-eco-server> [--refresh]" >&2
    exit 2
fi

SERVER="$1"
REFRESH=0
[ "${2:-}" = "--refresh" ] && REFRESH=1

[ -d "$SERVER/Mods/__core__" ] || { echo "not an Eco server directory (no Mods/__core__): $SERVER" >&2; exit 1; }
[ -d "$USERCODE_ROOT" ] || { echo "no UserCode tree to deploy: $USERCODE_ROOT" >&2; exit 1; }

# Relative paths, so one name drives the tracked copy, the __core__ source, and the destination.
RELS=()
while IFS= read -r rel; do
    RELS+=("$rel")
done < <(cd "$USERCODE_ROOT" && find . -type f -name '*.override.cs' | sed 's|^\./||' | sort)

[ "${#RELS[@]}" -gt 0 ] || { echo "no *.override.cs files found under $USERCODE_ROOT" >&2; exit 1; }

for REL in "${RELS[@]}"; do
    TRACKED="$USERCODE_ROOT/$REL"
    CORE="$SERVER/Mods/__core__/${REL%.override.cs}.cs"
    DEST="$SERVER/Mods/UserCode/$REL"

    if [ "$REFRESH" -eq 1 ]; then
        [ -f "$CORE" ] || { echo "core file not found: $CORE" >&2; exit 1; }

        grep -q "AllowPluginModules(ItemTypes = new\[\] {" "$CORE" || {
            echo "the vanilla [AllowPluginModules] in $CORE no longer has the expected shape." >&2
            echo "read it and update this script rather than writing a silently useless override." >&2
            exit 1
        }

        sed "s/AllowPluginModules(ItemTypes = new\[\] { /AllowPluginModules(Tags = new[] { \"$MODULE_TAG\" }, ItemTypes = new[] { /" \
            "$CORE" > "$TRACKED"

        # The override replaces the whole core file, so a truncated copy would delete the table.
        CORE_LINES=$(wc -l < "$CORE")
        NEW_LINES=$(wc -l < "$TRACKED")
        [ "$CORE_LINES" -eq "$NEW_LINES" ] || {
            echo "refreshed $REL is $NEW_LINES lines against the core file's $CORE_LINES -- rejecting." >&2
            exit 1
        }
        echo "refreshed $TRACKED from $CORE ($NEW_LINES lines); review the diff before committing."
    fi

    grep -q "$MODULE_TAG" "$TRACKED" || { echo "tracked override does not carry the $MODULE_TAG tag: $TRACKED" >&2; exit 1; }

    mkdir -p "$(dirname "$DEST")"
    cp "$TRACKED" "$DEST"
    echo "installed $DEST"
done

echo "${#RELS[@]} override(s) deployed to $SERVER"
