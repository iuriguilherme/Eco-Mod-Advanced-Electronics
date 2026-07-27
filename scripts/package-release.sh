#!/usr/bin/env bash
#
# Assembles the ready-to-upload release zip for the mod.io page.
#
# Produces  dist/AdvancedElectronics-<version>-eco<game>.zip  containing ONE folder,
# which the admin drops into Mods/UserCode/:
#
#   AdvancedElectronics/AdvancedElectronics.dll
#   AdvancedElectronics/AdvancedElectronics.Navigation.dll
#   AdvancedElectronics/AdvancedElectronics.unity3d
#   AdvancedElectronics/README.txt  LICENSE
#
# The zip does NOT carry a Mods/UserCode/ prefix. Server owners know where mods go,
# and shipping the prefix invites extracting it INSIDE UserCode, which produces a
# nested Mods/UserCode/Mods/UserCode/... copy. That is not merely untidy: Eco scans
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

VERSION="0.0.1"
GAME_VERSION="0.13.0.4"
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
cp LICENSE                                           "$MODDIR/"

cat > "$MODDIR/README.txt" <<TXT
Advanced Electronics — an Eco mod adding a ground survey drone.
Version ${VERSION}, built for Eco ${GAME_VERSION}.

  Mod page: https://mod.io/g/eco/m/advanced-electronics
  Source:   https://github.com/iuriguilherme/Eco-Mod-Advanced-Electronics
  Devlog:   https://www.youtube.com/watch?v=xqkzmVZ5kcM&list=PLHZA8oVAgAd4

*** ALPHA -- DO NOT USE ON A WORLD YOU CARE ABOUT ***

  Expect breaking changes between versions, including ones that are not
  migrated: an update can change how dock and drone state is stored, and
  older saved state may not survive it.

  This version is also known to leave orphaned objects in the world --
  drones that outlive their dock, or objects an update no longer
  recognises -- which may need removing by hand with admin tools.

  Run it on a test world, or one where losing placed Drone Docks and their
  survey data is acceptable. Back up your save before updating.

INSTALL
  1. Stop the Eco server.
  2. Extract this zip and put the AdvancedElectronics folder into
     Eco_Data/Server/Mods/UserCode/ , so you end up with
     Mods/UserCode/AdvancedElectronics/ containing:
        AdvancedElectronics.dll             the mod
        AdvancedElectronics.Navigation.dll  navigation core; the mod will not
                                            load without it
        AdvancedElectronics.unity3d         client assets, sent to players
                                            automatically -- players install nothing
  3. If you are UPDATING, delete the old copy first -- see below.
  4. Start the server. The mods listing should show "Advanced Electronics".

UPDATING -- READ THIS
  Delete the previous AdvancedElectronics folder (or any loose
  AdvancedElectronics*.dll / AdvancedElectronics.unity3d left in UserCode)
  BEFORE copying the new one in.

  Eco scans Mods/ recursively and keys asset bundles by filename, so a second
  copy of AdvancedElectronics.unity3d anywhere under Mods/ -- including in a
  folder you created and named something like "Ignore", "old" or "backup" --
  aborts server startup with:

     System.ArgumentException: An item with the same key has already been
     added. Key: AdvancedElectronics.unity3d

  Renaming the folder does not help. Move old copies OUT of Mods/ entirely.

UNINSTALL
  Delete the Mods/UserCode/AdvancedElectronics/ folder. Removing the mod
  discards any placed Drone Docks along with their survey areas and findings.

USAGE
  Craft a Drone Dock and a Survey Drone at the Electric Machinist Table. Place
  the dock, open it, and use the Areas tab to draw survey areas on the map and
  assign one. Insert the Survey Drone item to launch. The Results tab shows what
  was found, one area at a time.

LICENSE
  LGPL-3.0-or-later. See LICENSE. Source is at the GitHub link above; you may
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
