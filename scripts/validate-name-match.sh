#!/usr/bin/env bash
# U11 — name-match validation gate (docs/plans/2026-07-11-001-feat-survey-drone-plan.md).
#
# Cross-checks that every server-side WorldObject/Item class in
# EcoServerMod/AdvancedElectronics/ has a matching-named client-side
# counterpart, and flags any asset under Assets/Art/AdvancedElectronics/ that
# doesn't match a known server class name. This is the automatable half of
# U11's Verification ("the name cross-check reports zero mismatches") — a
# scripted grep diff per the plan's Approach note ("editor script or a
# documented dotnet/grep cross-check"), run headless, no Unity Editor
# required.
#
# WorldObjects are matched as separate .prefab files under
# Assets/Art/AdvancedElectronics/ (the ModKit's "Adding World Objects" flow
# always produces a standalone prefab). Items are matched differently: per
# the ModKit's "Adding Items" flow (Assets/EcoModKit/Docs/README.md), an item
# is an unpacked GameObject living directly inside a scene under the "Items"
# root — never saved as its own prefab file — so Item types are matched by
# grepping every Assets/**/*.unity scene file for a GameObject with that exact
# m_Name instead of by filename.
#
# Exit code 0 = clean (matches this repo's CI-friendly convention: no
# mismatches). Exit code 1 = mismatches found — read the report above the
# exit line for what's missing/unexpected.

set -euo pipefail
cd "$(dirname "$0")/.."

SERVER_DIR="EcoServerMod/AdvancedElectronics"
ASSET_DIR="Assets/Art/AdvancedElectronics"

if [ ! -d "$SERVER_DIR" ]; then
  echo "ERROR: $SERVER_DIR not found — run from the repo root or check the path." >&2
  exit 2
fi

# --- Server type discovery ---------------------------------------------------
#
# Declarations are matched against a NORMALIZED view of the sources rather than
# line by line, because a base list legitimately spans several lines:
#
#     public partial class BatteryBlock :
#         PickupableBlock
#         , IRepresentsItem
#
# normalize_sources strips // comments first (so commented-out template code is
# never discovered as a real type), then joins everything and collapses runs of
# whitespace, leaving each declaration as "class <Name> : <Base> , <Iface> {".
#
# An earlier version of this script grepped 'class X : WorldObject *$' and
# 'class X : Item *$'. Those end-of-line anchors were meant to exclude
# WorldObjectItem<T> and WorldObjectComponent, but they also excluded every
# declaration carrying a trailing interface, a trailing brace, or a generic
# base — which is nearly all of them. The gate consequently discovered only
# SurveyDroneObject and SurveyDroneItem and reported PASS while DroneDockObject
# and six newer types went unchecked: a green gate that verified almost nothing.
# The same exclusion is now done by requiring a word boundary after the base
# name rather than end-of-line.
normalize_sources() {
  find "$SERVER_DIR" -name '*.cs' -exec cat {} + \
    | sed -E 's://.*$::' \
    | tr '\n' ' ' \
    | sed -E 's/[[:space:]]+/ /g'
}

DECLS="$(normalize_sources)"

# WorldObject subclasses need a name-matching .prefab. Requiring one of " ,{"
# after the base name is what keeps WorldObjectItem<T> and WorldObjectComponent
# out of this list.
mapfile -t WORLD_OBJECT_TYPES < <(
  printf '%s' "$DECLS" \
    | grep -oE 'class [A-Za-z0-9_]+ : WorldObject[ ,{]' \
    | sed -E 's/class ([A-Za-z0-9_]+) : WorldObject./\1/' \
    | sort -u
)

# Item types need a name-matching GameObject under the scene's "Items" root.
# Every base listed here is something a player can hold: a plain Item, the
# world-object item that places a WorldObject, the block item that places a
# block, and the skill book/scroll pair.
#
# PickupableBlock is deliberately NOT listed. A placed block binds through a
# BlockSet rather than an Items-root GameObject, and this mod ships no BlockSet
# yet (the battery's placed block is explicitly deferred), so demanding an asset
# for one would fail the gate on work that was consciously left undone.
mapfile -t ITEM_TYPES < <(
  printf '%s' "$DECLS" \
    | grep -oE 'class [A-Za-z0-9_]+ : (Item[ ,{]|WorldObjectItem<|BlockItem<|SkillBook<|SkillScroll<)' \
    | sed -E 's/class ([A-Za-z0-9_]+) : .*/\1/' \
    | sort -u
)

echo "Server WorldObject types (need a name-matching prefab): ${WORLD_OBJECT_TYPES[*]:-none}"
echo "Server Item types (need a name-matching icon asset):    ${ITEM_TYPES[*]:-none}"
echo

if [ ! -d "$ASSET_DIR" ]; then
  echo "ERROR: $ASSET_DIR does not exist yet — no client assets have been created (U9/U10 pending)." >&2
  echo "Every server type above is therefore unmatched." >&2
  exit 1
fi

# Every non-.meta file's basename (without extension) directly under
# Assets/Art/AdvancedElectronics/ — the set of client asset names to match
# against. Unity mirrors every real asset with a same-named .meta sidecar
# (see this repo's CLAUDE.md "Unity repo hygiene" note); .meta files are
# excluded here since they're not independent assets.
mapfile -t ASSET_NAMES < <(
  find "$ASSET_DIR" -type f ! -name '*.meta' -exec basename {} \; \
    | sed -E 's/\.[^.]+$//' \
    | sort -u
)

echo "Client asset basenames found: ${ASSET_NAMES[*]:-none}"
echo

missing=0
for t in "${WORLD_OBJECT_TYPES[@]:-}"; do
  [ -z "$t" ] && continue
  if ! printf '%s\n' "${ASSET_NAMES[@]:-}" | grep -qx "$t"; then
    echo "MISMATCH: WorldObject '$t' has no matching-named prefab under $ASSET_DIR"
    missing=1
  fi
done

for t in "${ITEM_TYPES[@]:-}"; do
  [ -z "$t" ] && continue
  # Item GameObjects live inside a scene, not as a standalone asset file (see
  # header) — check file basenames first (in case a future workflow saves one
  # as a prefab too), then fall back to scanning every scene for a matching
  # m_Name: <Type> GameObject.
  if printf '%s\n' "${ASSET_NAMES[@]:-}" | grep -qx "$t"; then
    continue
  fi
  if find Assets -name '*.unity' -exec grep -l "^  m_Name: $t\$" {} + | grep -q .; then
    continue
  fi
  echo "MISMATCH: Item '$t' has no matching-named icon asset under $ASSET_DIR and no matching GameObject in any Assets/**/*.unity scene"
  missing=1
done

# Reverse direction: an asset name that doesn't match any known server type
# is either a typo (silent-failure seam this check exists to catch) or a
# deliberately shared/support asset (e.g. a material or texture named after
# neither class) — report it but don't fail the gate on this direction alone,
# since not every asset file is expected to be a 1:1 class match (materials,
# textures, sub-meshes legitimately live in the same folder).
for a in "${ASSET_NAMES[@]:-}"; do
  [ -z "$a" ] && continue
  is_known=0
  # "<Type>_icon.png" is the generated placeholder sprite for <Type>. The suffix
  # is a human-readable convention only -- nothing binds to the PNG name (the
  # scene GameObject is the bound artifact) -- so strip it before comparing,
  # rather than emitting a NOTE for every icon the finisher produces.
  a_base="${a%_icon}"
  for t in "${WORLD_OBJECT_TYPES[@]:-}" "${ITEM_TYPES[@]:-}"; do
    { [ "$a" = "$t" ] || [ "$a_base" = "$t" ]; } && is_known=1 && break
  done
  if [ "$is_known" -eq 0 ]; then
    echo "NOTE: asset '$a' under $ASSET_DIR doesn't match any known server WorldObject/Item name — confirm this is intentional (material/texture/support asset), not a typo of a server class name."
  fi
done

echo
if [ "$missing" -eq 0 ]; then
  echo "PASS: every server WorldObject/Item type has a matching-named client asset."
  exit 0
else
  echo "FAIL: one or more server types have no matching client asset — see MISMATCH lines above."
  exit 1
fi
