#!/usr/bin/env bash
#
# Assembles the ready-to-upload release zip for the mod.io page.
#
# Produces  dist/AdvancedElectronics-<version>-eco<game>.zip  containing ONE folder,
# which the admin drops into Mods/:
#
#   AdvancedElectronics/AdvancedElectronics.dll
#   AdvancedElectronics/AdvancedElectronics.Navigation.dll
#   AdvancedElectronics/AdvancedElectronics.unity3d
#   AdvancedElectronics/README.txt  LICENSE.txt  LICENSE-ART.txt
#
# Mods/AdvancedElectronics/, NOT Mods/UserCode/AdvancedElectronics/. UserCode is for
# source-code mods Eco compiles at runtime; a compiled-DLL mod lives directly under
# Mods/. That is the layout this project deploys to and live-tests against.
#
# The zip does NOT carry a Mods/ prefix. Server owners know where mods go, and
# shipping the prefix invites extracting it INSIDE Mods/, which produces a
# nested Mods/Mods/... copy. That is not merely untidy: Eco scans
# Mods/ recursively and keys asset bundles by FILENAME, so a second copy of
# AdvancedElectronics.unity3d anywhere under Mods/ aborts server startup with
# "An item with the same key has already been added". One folder, one place.
#
# The bundle sits beside its DLLs the same way NuclearReactor keeps
# binaryReactor.unity3d.
#
# The asset bundle is NOT built here -- it comes out of the Unity Editor
# (Eco Tools > Mod Kit > Build Current Bundle). This script's most important job is
# refusing to package a STALE one: the bundle carries the prefabs, their animator
# controllers and avatar masks, so a bundle older than the client assets ships art that
# silently does not match the DLLs. That already happened once.
#
# The bundle carries no code. A mod cannot ship client MonoBehaviours at all -- the Eco
# client is an IL2CPP build with no managed assembly loading -- so everything the client
# does is driven by the server pushing animated states whose names match the prefab's
# animator parameters. See
# docs/solutions/architecture-patterns/client-animation-is-driven-by-name-not-by-mod-code.md
#
# Usage:
#   scripts/package-release.sh [--version X.Y.Z] [--force]
#
#   --force   package even if the bundle looks stale (you have just rebuilt it and
#             know better -- e.g. a git checkout rewrote source mtimes)

set -euo pipefail

VERSION="0.3.0"
GAME_VERSION="0.14.0.0"
FORCE=0

while [ $# -gt 0 ]; do
    case "$1" in
        --version) VERSION="$2"; shift 2 ;;
        --force)   FORCE=1; shift ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

BUNDLE="AssetBundles/AdvancedElectronics.unity3d"
RELEASE_DIR="EcoServerMod/AdvancedElectronics/bin/Release/net10.0"
STAGE="dist/stage"
OUT="dist/AdvancedElectronics-${VERSION}-eco${GAME_VERSION}.zip"

fail() { echo "ERROR: $*" >&2; exit 1; }

# --- 1. Server DLLs -------------------------------------------------------------
echo "==> Building server DLLs (Release)"
dotnet build EcoServerMod/AdvancedElectronics -c Release --nologo -v q \
    || fail "server build failed"

for dll in AdvancedElectronics.dll AdvancedElectronics.Navigation.dll; do
    [ -f "$RELEASE_DIR/$dll" ] || fail "missing $RELEASE_DIR/$dll"
done

# The spike project is a reference artifact, never shipped.
if [ -f "$RELEASE_DIR/AdvancedElectronics.Spike.dll" ]; then
    fail "AdvancedElectronics.Spike.dll is in the Release output; it must not ship"
fi

# --- 2. Tests must pass ---------------------------------------------------------
echo "==> Running navigation tests"
dotnet test EcoServerMod/AdvancedElectronics.Navigation.Tests --nologo -v q \
    || fail "tests failed -- not packaging"

# --- 3. Asset bundle, and the staleness guard -----------------------------------
[ -f "$BUNDLE" ] || fail "$BUNDLE not found. Build it in Unity: Eco Tools > Mod Kit > Build Current Bundle"

# Anything under Assets/Art newer than the bundle means the bundle predates a client
# change. Fails closed: a git checkout rewrites mtimes and can trigger a false
# positive, which is why --force exists -- but the default must be to refuse.
NEWER="$(find Assets/Art -type f \
            \( -name '*.cs' -o -name '*.prefab' -o -name '*.mat' -o -name '*.png' \) \
            -newer "$BUNDLE" 2>/dev/null | head -5)"

if [ -n "$NEWER" ]; then
    echo "Client sources are newer than the asset bundle:" >&2
    echo "$NEWER" | sed 's/^/    /' >&2
    echo "    (bundle built: $(date -r "$BUNDLE" '+%Y-%m-%d %H:%M'))" >&2
    if [ "$FORCE" -eq 0 ]; then
        fail "bundle is stale. Rebuild it in Unity, or pass --force if you just did."
    fi
    echo "WARNING: packaging a possibly stale bundle because --force was given" >&2
fi

# --- 4. Stage and zip -----------------------------------------------------------
echo "==> Staging"
rm -rf "$STAGE"
MODDIR="$STAGE/AdvancedElectronics"
mkdir -p "$MODDIR"

cp "$RELEASE_DIR/AdvancedElectronics.dll"            "$MODDIR/"
cp "$RELEASE_DIR/AdvancedElectronics.Navigation.dll" "$MODDIR/"
cp "$BUNDLE"                                         "$MODDIR/"
cp LICENSE                                           "$MODDIR/LICENSE.txt"

# The drone model is CC BY-SA 4.0 and not ours. Attribution is a licence condition, and
# the model ships inside the bundle, so the notice has to travel in the zip -- not only
# in the repository a server admin may never look at.
cp LICENSE-ART                                       "$MODDIR/LICENSE-ART.txt"

cat > "$MODDIR/README.txt" <<TXT
Advanced Electronics — an Eco mod adding an autonomous flying survey drone.
Version ${VERSION}, built for Eco ${GAME_VERSION}.

  Mod page: https://mod.io/g/eco/m/advanced-electronics
  Source:   https://github.com/iuriguilherme/Eco-Mod-Advanced-Electronics
  Devlog:   https://www.youtube.com/watch?v=xqkzmVZ5kcM&list=PLHZA8oVAgAd4

*** REQUIRES ECO ${GAME_VERSION}. IT DOES NOT LOAD ON 0.13. ***

  This is a game-version requirement, not a migration note. Earlier releases ran
  on Eco 0.13; this one is built against ${GAME_VERSION} and will not load on a
  0.13 server at all. Update your server first, or stay on the previous release.

*** ALPHA -- DO NOT USE ON A WORLD YOU CARE ABOUT ***

  This mod ships NO SAVE MIGRATIONS. Objects placed by an earlier version can
  fail to load after an update, and a world object that fails to load can take
  the whole world's load with it. That risk is inherent to updating this mod and
  does not go away because a particular release happens to be gentle.

  *** THIS VERSION SPECIFICALLY: the Drone Dock's component set CHANGED.   ***
  *** Remove every Drone Dock with admin tools BEFORE updating, or start   ***
  *** a fresh world.                                                      ***

  Unlike 0.2.0, this release is not a gentle one for placed objects. The dock
  gained two components and had a third replaced: its link component is now the
  game's own shared one, so the storage links you had configured are held by a
  component the dock no longer declares. A component a class no longer declares
  is stripped from placed objects at the next load, together with its contents.

  This was observed during development, not inferred: a dock saved before the
  change failed to initialise afterwards. That failure is now contained to the
  dock rather than aborting server startup, but the dock itself comes up wrong --
  no storage links, and no link radius.

  Survey areas and findings live on the dock too. Removing the dock discards
  them either way; that is the cost of this update.

  BACK UP YOUR SAVE BEFORE UPDATING.

WHAT IS NEW IN 0.3.0

  THE MINING DRONE

  - NEW DRONE: the Mining Drone, crafted at the Robotic Assembly Line. Slot it
    into a Drone Dock and the dock grows a Mining tab instead of the Survey one.
  - It digs a shaft per plot: a 3x3 opening at the surface and the full 5x5
    beneath it, fifteen layers down, and it keeps everything it breaks.
  - IT MINES AS YOU. Every removal is performed as the citizen who assigned the
    area, using a Mining Arm carrying the game's own excavation tag. Settlement
    laws and private property refuse it exactly as they refuse that citizen
    digging by hand, and a refused plot is recorded with the reason rather than
    silently skipped. A law written against excavation tools can name the Mining
    Arm directly.
  - It works from a survey. A mining dock consumes areas published by a SURVEY
    dock you own, and only mines plots that dock has actually surveyed.
  - Survey again to go deeper. One pass takes fifteen layers; re-surveying the
    pit floor opens the next fifteen, and repeating reaches bedrock.
  - It unloads into linked storage and waits at the dock when there is no room,
    rather than stopping mid-area.

  LINKED STORAGE ON THE DOCK

  - The dock's Storage tab now has the game's own per-target Take From / Put
    Into controls, so you choose which containers the drone unloads into. This
    is the standard link UI, not a mod-specific one.

  ADMIN CONTROL

  - NEW COMMAND: /drone haltmining on|off stops every mining drone on the
    server. Admin-only and server-wide by design -- it reaches docks you have no
    access to, which is the point. It survives a restart, and a halted dock says
    so. See the Server administration notes in the source README.

  THE PANELS

  - The Survey tab is select-then-assign: move the selector to read any area's
    findings, then press Assign Selected Area to send the drone. Moving the
    selector no longer redirects a working drone, which it used to.
  - Findings list each material with its item icon, at a larger size.
  - Areas are marked: yellow [assigned], green [mined], red [unreachable].
  - Drone status is written in words -- "flying to the area", "surveying",
    "returning to dock" -- instead of internal state names.
  - Drone Docks now appear on the minimap.
  - A survey drone unassigns itself when its sweep finishes, so a completed area
    does not restart on the next server load.

  NOT IN THIS RELEASE

  - The Harvest Drone exists in the source but is not craftable.
  - Drones do not put anything back: no backfilling spoil, and no building up a
    safety rim around a finished pit. Both need the drone to place blocks, which
    it cannot yet do.
  - The Advanced Electronics Assembly is still excluded from the build.

  This version is also known to leave orphaned objects in the world --
  drones that outlive their dock, or objects an update no longer
  recognises -- which may need removing by hand with admin tools.

  Run it on a test world, or one where losing placed Drone Docks and their
  survey data is acceptable. Back up your save before updating.

INSTALL
  1. Stop the Eco server.
  2. Extract this zip and put the AdvancedElectronics folder into
     Eco_Data/Server/Mods/ , so you end up with
     Mods/AdvancedElectronics/ containing:
        AdvancedElectronics.dll             the mod
        AdvancedElectronics.Navigation.dll  navigation core; the mod will not
                                            load without it
        AdvancedElectronics.unity3d         client assets, sent to players
                                            automatically -- players install nothing
  3. OPTIONAL, for the Advanced Electronics Upgrade module: a vanilla crafting
     table only accepts modules it names, and a mod cannot add itself to that
     list. Install the UserCode overrides from a clone of this mod's source:

        scripts/deploy-usercode-overrides.sh /path/to/Eco_Data/Server

     That one command installs both overrides -- the Robotic Assembly Line and
     the Electronics Assembly. Without them everything works except slotting the
     upgrade module into those two tables.
  4. If you are UPDATING, read the UPDATING section below first.
  5. Start the server. The mods listing should show "Advanced Electronics".

UPDATING -- READ THIS
  Read the save-migration warning above first. The file side of an update is
  easy; the world side is not.

  TAKE YOUR FUEL AND YOUR DRONES OUT FIRST. Removing a dock discards what is
  inside it, batteries included, so empty the fuel slots and pull the drone item
  out before you pick the dock up.

  Overwriting the AdvancedElectronics folder in place is fine -- the file names
  do not change between versions, so a copy-over replaces every shipped file.
  What you must NOT do is leave a SECOND copy of these files anywhere under
  Mods/.

  Eco scans Mods/ recursively and keys asset bundles by filename, so a second
  copy of AdvancedElectronics.unity3d anywhere under Mods/ -- including in a
  folder you created and named something like "Ignore", "old" or "backup" --
  aborts server startup with:

     System.ArgumentException: An item with the same key has already been
     added. Key: AdvancedElectronics.unity3d

  Renaming the folder does not help. Move old copies OUT of Mods/ entirely.

UNINSTALL
  Delete the Mods/AdvancedElectronics/ folder. Removing the mod
  discards any placed Drone Docks along with their survey areas and findings.

USAGE
  Advanced Electronics is an Engineer specialty. Craft the Advanced Electronics
  Skill Book at a Laboratory to discover it.

  Everything is then crafted at vanilla tables:

    Robotic Assembly Line   Drone Dock, Survey Drone, Mining Drone
    Electronics Assembly    Battery, Sulfuric Battery, Advanced Electronics Upgrade
    Laboratory              Advanced Electronics Skill Book, research paper

  The Advanced Electronics Assembly is NOT in this release -- it is excluded from
  the build while its placement is unfinished, so nothing asks you to make one.

  SURVEYING. Place a dock and open its Survey tab. Use "Manage Areas on Map" to
  draw survey areas -- up to ten per dock. Set "View Position" to the one you
  want and press "Assign Selected Area"; "Unassign Area" stops the drone. Insert
  the Survey Drone item to launch.

  Moving the selector only changes which area's findings you are reading. It
  never redirects a drone that is already working.

  MINING. Place a SECOND dock and slot a Mining Drone. Its Mining tab lists the
  areas published by your survey docks -- pick one and press "Assign Selected
  Area". The drone mines only the plots that were surveyed, and stops when it
  runs out of them; survey the pit floor again to send it another fifteen layers
  deeper.

  Open the mining dock's Storage tab and use Take From / Put Into to choose where
  the drone unloads. With nothing linked it fills up and waits.

KNOWN ISSUES IN THIS VERSION
  - Drones fly through trees, stockpiles and other placed objects. They route
    around whatever exists when they plan a trip, and pass straight through
    anything built afterwards. Nothing is damaged or moved -- it is a visual
    fault only.
  - A drone starts travelling before its take-off animation has finished. The
    server times these by counting animation frames rather than by being told
    when a clip ends, so the two drift apart. Cosmetic.
  - The Drone Dock is a placeholder cube, and it draws about a metre above the
    volume it occupies. It places and works normally.
  - No placement preview: the Drone Dock shows no ghost outline while you hold
    it. It still places normally.
  - A drone can be left orphaned in the world across a server restart -- alive
    with no dock owning it. Admins can list and remove them with '/drone orphans'
    and '/drone orphans destroy'.
  - Mining a plot is not reversible from inside the mod, and a worked plot is
    left as an open pit with a 3x3 mouth. There is no fence around it.
  - Item and skill icons are flat-colour placeholders.

LICENSE

    Advanced Electronics -- an Eco mod adding an autonomous flying survey drone.
    Copyright (C) 2026  Iuri Guilherme

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Lesser General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Lesser General Public License for more details.

    You should have received a copy of the GNU Lesser General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.

  The full text is in LICENSE.txt. Source is at the GitHub link above.

  Eco and the Eco ModKit are the property of Strange Loop Games and are neither
  included in nor covered by this license.

  ATTRIBUTION

    The HRVSTR-01 drone -- its mesh, textures, rig and animation clips, and work
    adapted from them -- was created by Phlo123 (https://github.com/Phlo123) and
    is licensed under the Creative Commons Attribution-ShareAlike 4.0
    International license, not under the license above. Its full text is in
    LICENSE-ART.txt.
TXT

echo "==> Zipping"
mkdir -p dist
rm -f "$OUT"

# `zip` is absent from a stock Git-for-Windows shell, so fall back to Python's
# zipfile (python3 is already required by nothing else here, but is present on
# every machine that can run the Unity/dotnet toolchain we depend on).
if command -v zip >/dev/null 2>&1; then
    ( cd "$STAGE" && zip -qr "../$(basename "$OUT")" . )
elif command -v python3 >/dev/null 2>&1; then
    python3 - "$STAGE" "$OUT" <<'PY'
import os, sys, zipfile
stage, out = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for root, _, files in os.walk(stage):
        for f in sorted(files):
            full = os.path.join(root, f)
            z.write(full, os.path.relpath(full, stage).replace(os.sep, "/"))
PY
else
    fail "neither 'zip' nor 'python3' available to build the archive"
fi

[ -f "$OUT" ] || fail "archive was not created"
rm -rf "$STAGE"

echo
echo "Built: $OUT"
if command -v unzip >/dev/null 2>&1; then
    unzip -l "$OUT"
else
    python3 - "$OUT" <<'PY'
import sys, zipfile
with zipfile.ZipFile(sys.argv[1]) as z:
    for i in z.infolist():
        print(f"{i.file_size:>10}  {i.filename}")
PY
fi
