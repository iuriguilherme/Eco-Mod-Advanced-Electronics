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

VERSION="0.2.0"
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
cp LICENSE-ART.md                                    "$MODDIR/LICENSE-ART.txt"

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

  THIS VERSION, SPECIFICALLY: unlike 0.1.0, this release does not change the
  component set of the Drone Dock or the Survey Drone, so docks and drones placed
  by 0.1.0 are expected to load. That is a statement about what changed in the
  code, not a guarantee about your world -- it has not been tested against every
  0.1.0 save, and no migration exists to rescue one that does fail.

  BACK UP YOUR SAVE BEFORE UPDATING. If you are coming from a release older than
  0.1.0, the 0.1.0 warning still applies in full: remove every Drone Dock and
  Survey Drone with admin tools first, or start a fresh world.

WHAT IS NEW IN 0.2.0

  THE DRONE FLIES, AND YOU CAN SEE IT

  - The Survey Drone has a model and a full animation set. It sits docked, picks
    its mode, lifts off, flies, works, and settles back onto its pad, driven by
    what the drone is actually doing on the server rather than by a loop that
    plays regardless.
  - It flies at altitude instead of skimming the terrain, holds itself level like
    a helicopter rather than tilting into climbs, and changes height slowly enough
    to see.
  - The Drone Dock is a 4x4 pad, and the drone parks on top of it rather than
    inside it.

  BATTERIES

  - NEW ITEM: the Battery. Crafted at the Electronics Assembly from nitric acid,
    copper concentrate and plastic at Advanced Electronics 1. One battery runs a
    drone for about an hour, weighs 1 kg, and stacks to five -- so a dock's two
    fuel slots hold about ten hours of surveying and a full load fits in your
    pack alongside everything else.
  - THE DRONE NOW BURNS BATTERIES, NOT LIQUID FUEL. A dock with a Survey Drone
    slotted accepts only Batteries; biodiesel and gasoline are refused. Nothing
    else about fuelling changes -- burn rate, slot count, and the recall-when-
    empty behaviour are the same.
  - NEW TALENT: Sulfuric Battery, one star at Advanced Electronics 3. It unlocks
    a second Battery recipe that halves the copper by spending sulfuric acid
    instead. The recipe is listed at the Electronics Assembly before you learn
    the talent, marked with the talent it needs, and the original recipe stays
    available whether you learn it or not.
  - The Electronics Assembly now accepts the Advanced Electronics Upgrade, which
    reduces what both Battery recipes cost. See INSTALL step 3.

  NOT IN THIS RELEASE

  - A Mining Drone and a Harvest Drone exist in the code and are visible in the
    source, but neither is craftable: their recipes are withheld because the arm
    they carry does not yet behave correctly in flight. They are not on any bench
    and do not appear in the Advanced Electronics tech tree. Nothing is lost by
    waiting -- the Survey Drone is the one this release is about.

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

  NO ACTION IS NEEDED FOR FUEL. You do not have to drain your docks before
  updating. A dock still holding biodiesel keeps burning it after the update
  until it is spent, then reports itself out of fuel and waits for Batteries.
  The Battery-only restriction filters what may be ADDED to a fuel tank, never
  what may be burned from it, so leftover fuel is used rather than stranded --
  and you can still pull it out by hand at any time.

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
  Skill Book at a Laboratory to discover it, then build the Advanced Electronics
  Assembly at the Electric Machinist Table.

  Everything else is crafted at the Advanced Electronics Assembly: the Drone Dock,
  the Survey Drone, and the Advanced Electronics Upgrade module.

  Place the dock and open its Survey tab. Use "Manage Areas on Map" to draw survey
  areas -- up to ten per dock. The list numbers them; set "Assigned Position" to a
  number to send the drone there, or 0 to stop it. Insert the Survey Drone item to
  launch.

  Findings are shown one area at a time. "View Position" chooses which area you are
  reading and is separate from the assignment, so you can check any area without
  redirecting the drone.

KNOWN ISSUES IN THIS VERSION
  - The drone's movement and its animation are out of step at the transitions --
    it starts travelling before the take-off has finished playing, and lands
    without waiting for the docking animation. The server times these by counting
    animation frames rather than by being told when a clip starts or ends, so the
    two drift apart wherever a clip and a movement leg are supposed to line up.
    Cosmetic only: the drone goes where it should, and does what it should when it
    gets there.
  - The Drone Dock is a placeholder cube, and it currently draws about a metre
    above the volume it actually occupies. It places and works normally.
  - No placement preview: the Drone Dock and the Advanced Electronics Assembly show
    no ghost outline while you are holding them. They still place normally.
  - A Survey Drone can be left orphaned in the world across a server restart --
    alive with no dock owning it. Admins can list and remove them with
    '/drone orphans' and '/drone orphans destroy'.
  - An area the drone cannot reach only shows as the drone's status reading
    "Unreachable"; the area itself is not marked.
  - Item and skill icons are flat-colour placeholders.

CREDITS
  The HRVSTR-01 drone -- its model, textures, rig and animations -- was created by
  Phlo123 (https://github.com/Phlo123) and is licensed under Creative Commons
  Attribution-ShareAlike 4.0 International (CC BY-SA 4.0):

      https://creativecommons.org/licenses/by-sa/4.0/

  If you redistribute a modified version of the model you must credit Phlo123,
  say that you changed it, and license your version under CC BY-SA 4.0 as well.
  That obligation attaches to the model, not to the rest of this mod. Full terms
  and the exact file list are in LICENSE-ART.txt.

LICENSE
  The mod's code is LGPL-3.0-or-later. See LICENSE.txt. Source is at the GitHub
  link above; you may modify and redistribute this mod under the same terms.

  The drone model is NOT under that license -- see CREDITS above and
  LICENSE-ART.txt. The two coexist because the model and the code are separate
  works shipped together, not one derived from the other.

  Eco and the Eco ModKit are the property of Strange Loop Games and are not
  included in or covered by this license.
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
