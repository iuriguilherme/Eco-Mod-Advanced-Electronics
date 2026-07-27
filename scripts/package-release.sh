#!/usr/bin/env bash
#
# Assembles the ready-to-upload release zip for the mod.io page.
#
# Produces  dist/AdvancedElectronics-<version>-eco<game>.zip  laid out so a server
# admin extracts it straight over Eco_Data/Server/:
#
#   Mods/UserCode/AdvancedElectronics.dll
#   Mods/UserCode/AdvancedElectronics.Navigation.dll
#   Mods/UserCode/AdvancedElectronics.unity3d
#   README.txt  COPYING  COPYING.LESSER
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

VERSION="0.1.0"
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
mkdir -p "$STAGE/Mods/UserCode"

cp "$RELEASE_DIR/AdvancedElectronics.dll"            "$STAGE/Mods/UserCode/"
cp "$RELEASE_DIR/AdvancedElectronics.Navigation.dll" "$STAGE/Mods/UserCode/"
cp "$BUNDLE"                                         "$STAGE/Mods/UserCode/"
cp COPYING COPYING.LESSER                            "$STAGE/"

cat > "$STAGE/README.txt" <<TXT
Advanced Electronics — an Eco mod adding a ground survey drone.
Version ${VERSION}, built for Eco ${GAME_VERSION}.

  Mod page: https://mod.io/g/eco/m/advanced-electronics
  Source:   https://github.com/iuriguilherme/Eco-Mod-Advanced-Electronics

INSTALL
  1. Stop the Eco server.
  2. Extract this zip over your server's Eco_Data/Server/ directory. The three
     files below land in Mods/UserCode/:
        AdvancedElectronics.dll             the mod
        AdvancedElectronics.Navigation.dll  navigation core; the mod will not
                                            load without it
        AdvancedElectronics.unity3d         client assets, sent to players
                                            automatically -- players install nothing
  3. Start the server. The mods listing should show "Advanced Electronics".

UNINSTALL
  Delete those three files. Removing the mod discards any placed Drone Docks
  along with their survey areas and findings.

USAGE
  Craft a Drone Dock and a Survey Drone at the Electric Machinist Table. Place
  the dock, open it, and use the Areas tab to draw survey areas on the map and
  assign one. Insert the Survey Drone item to launch. The Results tab shows what
  was found, one area at a time.

LICENCE
  LGPL-3.0-or-later. See COPYING and COPYING.LESSER. Source is at the GitHub
  link above; you may modify and redistribute this mod under the same terms.

  Eco and the Eco ModKit are the property of Strange Loop Games and are not
  included in or covered by this licence.
TXT

echo "==> Zipping"
mkdir -p dist
rm -f "$OUT"
( cd "$STAGE" && zip -qr "../$(basename "$OUT")" . )
rm -rf "$STAGE"

echo
echo "Built: $OUT"
unzip -l "$OUT"
