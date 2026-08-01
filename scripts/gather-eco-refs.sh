#!/usr/bin/env bash
#
# Build and collect Eco 0.14 reference assemblies from a local Eco source checkout.
#
# Eco.ReferenceAssemblies has no 0.14 package, and the shipped dedicated server is a
# single-file bundle with its managed assemblies embedded, so neither is usable as a
# reference source. This script produces the same set the NuGet package would have
# carried, out of a checkout pinned to a known commit.
#
# The output stays inside the Eco checkout. It is Strange Loop Games' SDK and must never
# land in this repository, git-ignored or not.
#
# Usage:
#   scripts/gather-eco-refs.sh <path-to-eco-checkout> [--allow-sha-mismatch]
#
# Then point EcoRefAssembliesDir in EcoServerMod/AdvancedElectronics/Local.props at the
# directory this prints on the last line.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CSPROJ="$REPO_ROOT/EcoServerMod/AdvancedElectronics/AdvancedElectronics.csproj"

if [ $# -lt 1 ]; then
    echo "usage: scripts/gather-eco-refs.sh <path-to-eco-checkout> [--allow-sha-mismatch]" >&2
    exit 2
fi

ECO_ROOT="$(cd "$1" && pwd)"
ALLOW_SHA_MISMATCH=0
[ "${2:-}" = "--allow-sha-mismatch" ] && ALLOW_SHA_MISMATCH=1

# The pinned commit lives in the csproj, which is the tracked, single source of truth.
# Reading it here rather than repeating it keeps the two from drifting apart.
EXPECTED_SHA="$(grep -oP '(?<=<EcoRefSha>)[0-9a-f]+' "$CSPROJ")"
if [ -z "$EXPECTED_SHA" ]; then
    echo "could not read <EcoRefSha> from $CSPROJ" >&2
    exit 1
fi

ACTUAL_SHA="$(git -C "$ECO_ROOT" rev-parse HEAD)"
if [ "$ACTUAL_SHA" != "$EXPECTED_SHA" ]; then
    echo "checkout is at $ACTUAL_SHA, csproj pins $EXPECTED_SHA" >&2
    if [ "$ALLOW_SHA_MISMATCH" -eq 0 ]; then
        echo "check out the pinned commit, or pass --allow-sha-mismatch and update <EcoRefSha>." >&2
        exit 1
    fi
    echo "continuing anyway (--allow-sha-mismatch)" >&2
fi

# LFS-backed binaries in the checkout are pointer files until fetched, and the build fails
# with "Resolved file has a bad image" rather than anything naming LFS.
if head -c 40 "$ECO_ROOT/Server/Eco.Shared/Packages/StrangeCloud.Service.Client.CSharp.dll" \
   | grep -q "git-lfs"; then
    echo "LFS objects are not fetched in $ECO_ROOT -- run: git -C \"$ECO_ROOT\" lfs pull" >&2
    exit 1
fi

# SolutionDir is what the TechTree and version-stamp prebuilds resolve their paths from.
# Building a project file directly leaves it unset, the generators silently write nowhere,
# and the compile fails on thousands of missing generated types.
SOLUTION_DIR="$ECO_ROOT/Server/"

# Two passes, deliberately. Both prebuilds emit sources during AfterBuild, after the compile
# item globs have already been evaluated, so a cold checkout cannot succeed in one pass.
for pass in 1 2; do
    echo "=== build pass $pass ==="
    for proj in Eco.Mods Eco.Server; do
        dotnet build "$ECO_ROOT/Server/$proj/$proj.csproj" \
            -c Release -v minimal -p:SolutionDir="$SOLUTION_DIR" \
            || { [ "$pass" -eq 1 ] && echo "(pass 1 failure is expected on a cold checkout)"; }
    done
done

OUT="$ECO_ROOT/Build/EcoModkit/ReferenceAssemblies"
rm -rf "$OUT"
mkdir -p "$OUT"

# Reference assemblies land under a per-project TFM that varies (net10.0 vs net10.0-windows),
# so find them rather than assuming a path. A few projects multi-target, and their older
# frameworks produce a second assembly of the same name -- Eco.Shared also builds net6.0,
# Eco.Networking.ENet also builds netstandard2.1. Match only net10.0* or whichever copy the
# filesystem happens to return last silently wins, which is how a mod ends up compiling
# against a downlevel surface.
find "$ECO_ROOT/Server" -path "*/bin/Release/net10.0*/ref/*.dll" -exec cp {} "$OUT/" \;
cp "$ECO_ROOT/Server/Eco.Shared/Packages/StrangeCloud.Service.Client.CSharp.dll" "$OUT/"

COUNT=$(find "$OUT" -name "*.dll" | wc -l)
if [ "$COUNT" -lt 10 ]; then
    echo "only $COUNT assemblies collected -- the build did not produce the full set" >&2
    exit 1
fi

echo
echo "collected $COUNT reference assemblies from $ACTUAL_SHA"
find "$OUT" -name "*.dll" -exec basename {} \; | sort | sed 's/^/  /'
echo
echo "$OUT"
