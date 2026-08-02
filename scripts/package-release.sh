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
#   AdvancedElectronics/README.txt  LICENSE.txt
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
# refusing to package a STALE one: the bundle carries the prefabs and the
# DockReadoutDisplay MonoBehaviour, so a bundle older than the client sources ships
# behaviour that silently does not match the DLLs. That already happened once.
#
# Usage:
#   scripts/package-release.sh [--version X.Y.Z] [--force]
#
#   --force   package even if the bundle looks stale (you have just rebuilt it and
#             know better -- e.g. a git checkout rewrote source mtimes)

set -euo pipefail

VERSION="0.0.3"
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

cat > "$MODDIR/README.txt" <<TXT
Advanced Electronics — an Eco mod adding a ground survey drone.
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
  the whole world's load with it. Updating on a world that already contains
  Drone Docks or Survey Drones is the single most likely way to lose that world.

  *** THIS VERSION SPECIFICALLY: placed Drone Docks WILL NOT LOAD. ***

  This version merges the dock's Areas and Results tabs into a single Survey
  tab, which removes a stored component from every Drone Dock. That is not a
  "can fail" -- a dock placed by an earlier version does not survive the
  update, and no migration ships to rescue it.

  The safe update path is a fresh world, or removing every Drone Dock and Survey
  Drone with admin tools BEFORE installing the new version.

  *** Drone Docks and Survey Drones from earlier versions must be re-crafted. ***

  The drone's fuel now lives on the dock, installed there by the drone itself
  while it is slotted. An object's component set is fixed when it is created, so
  a dock placed before this version never gains the new components and a drone
  crafted before it declares none -- neither one starts working again on its own.

  A Survey Drone now also carries its own condition, which wears while it works
  and travels with the drone when you move it to another dock. Drones in
  different condition no longer stack together.

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
  3. OPTIONAL, for the Advanced Electronics Upgrade module: the Robotic Assembly
     Line only accepts modules it names, and a mod cannot add itself to that list.
     Install the UserCode override from a clone of this mod's source:

        scripts/deploy-usercode-overrides.sh /path/to/Eco_Data/Server

     Without it everything works except slotting the upgrade module into the
     Robotic Assembly Line.
  4. If you are UPDATING, read the UPDATING section below first.
  4. Start the server. The mods listing should show "Advanced Electronics".

UPDATING -- READ THIS
  Read the save-migration warning above first. The file side of an update is
  easy; the world side is not.

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
  - Drone Docks placed by an earlier version do not load. This version removes a
    stored component from the dock and ships no save migration -- see the ALPHA
    warning above.
  - No placement preview: the Drone Dock and the Advanced Electronics Assembly show
    no ghost outline while you are holding them. They still place normally.
  - The Survey Drone's own window opens with no tabs, so it cannot be refuelled.
    Fuel consumption is not implemented yet; the drone runs regardless.
  - A Survey Drone can be left orphaned in the world across a server restart --
    alive with no dock owning it. Admins can list and remove them with
    '/drone orphans' and '/drone orphans destroy'.
  - An area the drone cannot reach only shows as the drone's status reading
    "Unreachable"; the area itself is not marked.
  - Item and skill icons are flat-colour placeholders.

LICENSE
  LGPL-3.0-or-later. See LICENSE.txt. Source is at the GitHub link above; you may
  modify and redistribute this mod under the same terms.

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
