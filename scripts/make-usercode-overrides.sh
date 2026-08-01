#!/usr/bin/env bash
#
# Generate the UserCode override that lets the Robotic Assembly Line accept this mod's
# Advanced Electronics Upgrade module.
#
# The Robotic Assembly Line is a vanilla table. Its [AllowPluginModules] enumerates item types
# and carries no tag we could match on, and a mod assembly cannot add to that attribute --
# attributes merge across partial declarations only within a single assembly. Eco's escape hatch
# is a whole-file override: a file at the same path under Mods/UserCode/ with `.override` before
# the extension replaces the __core__ one outright.
#
# "Whole file" is why this is generated rather than committed. The override must reproduce all of
# Strange Loop Games' AutoGen source, and this repository is public; vendoring their SDK here is
# not ours to do. So the override is derived from the server's own installed copy at build time,
# which also means it re-derives correctly after an Eco update instead of pinning a stale copy.
#
# Usage:
#   scripts/make-usercode-overrides.sh <path-to-eco-server>   # e.g. .../Eco_Data/Server
#
# Writes:  <server>/Mods/UserCode/AutoGen/WorldObject/RoboticAssemblyLine.override.cs

set -euo pipefail

if [ $# -lt 1 ]; then
    echo "usage: scripts/make-usercode-overrides.sh <path-to-eco-server>" >&2
    exit 2
fi

SERVER="$1"
CORE="$SERVER/Mods/__core__/AutoGen/WorldObject/RoboticAssemblyLine.cs"
OUT_DIR="$SERVER/Mods/UserCode/AutoGen/WorldObject"
OUT="$OUT_DIR/RoboticAssemblyLine.override.cs"

MODULE="AdvancedElectronicsUpgradeItem"

if [ ! -f "$CORE" ]; then
    echo "core file not found: $CORE" >&2
    echo "pass the server directory that contains Mods/__core__ (e.g. .../Eco_Data/Server)." >&2
    exit 1
fi

if ! grep -q "AllowPluginModules" "$CORE"; then
    echo "no [AllowPluginModules] in $CORE -- Eco changed the table's module declaration." >&2
    echo "re-read the file and update this script rather than shipping a silently useless override." >&2
    exit 1
fi

if grep -q "$MODULE" "$CORE"; then
    echo "$MODULE is already accepted by the shipped table; no override needed." >&2
    exit 0
fi

mkdir -p "$OUT_DIR"

# Insert our module at the head of the ItemTypes array. Anchored on `ItemTypes = new[] {` so a
# change in the vanilla list's contents does not break the patch; a change in its shape fails the
# verification below rather than producing a quietly wrong file.
sed "s/AllowPluginModules(ItemTypes = new\[\] { /AllowPluginModules(ItemTypes = new[] { typeof($MODULE), /" \
    "$CORE" > "$OUT"

if ! grep -q "typeof($MODULE)" "$OUT"; then
    echo "patch did not apply -- the [AllowPluginModules] line does not have the expected shape." >&2
    rm -f "$OUT"
    exit 1
fi

# The override replaces the whole core file, so a truncated copy would silently delete the table.
CORE_LINES=$(wc -l < "$CORE")
OUT_LINES=$(wc -l < "$OUT")
if [ "$CORE_LINES" -ne "$OUT_LINES" ]; then
    echo "override is $OUT_LINES lines against the core file's $CORE_LINES -- refusing to ship it." >&2
    rm -f "$OUT"
    exit 1
fi

echo "wrote $OUT"
echo "  $MODULE added to the Robotic Assembly Line's accepted modules ($OUT_LINES lines)"
