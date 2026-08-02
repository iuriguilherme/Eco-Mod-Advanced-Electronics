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
# Usage:
#   scripts/deploy-usercode-overrides.sh <path-to-eco-server>            # install
#   scripts/deploy-usercode-overrides.sh <path-to-eco-server> --refresh  # re-derive from __core__
#
# --refresh regenerates the tracked override from the server's current __core__ file, for after an
# Eco update changes the vanilla table. Review the diff before committing it.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REL="AutoGen/WorldObject/RoboticAssemblyLine.override.cs"
TRACKED="$REPO_ROOT/EcoServerMod/UserCode/$REL"

MODULE_TAG="AdvancedElectronicsUpgrade"

if [ $# -lt 1 ]; then
    echo "usage: scripts/deploy-usercode-overrides.sh <path-to-eco-server> [--refresh]" >&2
    exit 2
fi

SERVER="$1"
REFRESH=0
[ "${2:-}" = "--refresh" ] && REFRESH=1

CORE="$SERVER/Mods/__core__/AutoGen/WorldObject/RoboticAssemblyLine.cs"
DEST="$SERVER/Mods/UserCode/$REL"

[ -d "$SERVER/Mods/__core__" ] || { echo "not an Eco server directory (no Mods/__core__): $SERVER" >&2; exit 1; }

if [ "$REFRESH" -eq 1 ]; then
    [ -f "$CORE" ] || { echo "core file not found: $CORE" >&2; exit 1; }

    grep -q "AllowPluginModules(ItemTypes = new\[\] {" "$CORE" || {
        echo "the vanilla [AllowPluginModules] no longer has the expected shape." >&2
        echo "read $CORE and update this script rather than writing a silently useless override." >&2
        exit 1
    }

    sed "s/AllowPluginModules(ItemTypes = new\[\] { /AllowPluginModules(Tags = new[] { \"$MODULE_TAG\" }, ItemTypes = new[] { /" \
        "$CORE" > "$TRACKED"

    # The override replaces the whole core file, so a truncated copy would delete the table.
    CORE_LINES=$(wc -l < "$CORE")
    NEW_LINES=$(wc -l < "$TRACKED")
    [ "$CORE_LINES" -eq "$NEW_LINES" ] || {
        echo "refreshed override is $NEW_LINES lines against the core file's $CORE_LINES -- rejecting." >&2
        exit 1
    }
    echo "refreshed $TRACKED from $CORE ($NEW_LINES lines); review the diff before committing."
fi

grep -q "$MODULE_TAG" "$TRACKED" || { echo "tracked override does not carry the $MODULE_TAG tag: $TRACKED" >&2; exit 1; }

mkdir -p "$(dirname "$DEST")"
cp "$TRACKED" "$DEST"
echo "installed $DEST"
