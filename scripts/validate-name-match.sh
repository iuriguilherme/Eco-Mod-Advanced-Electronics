#!/usr/bin/env bash
# U11 — name-match validation gate (docs/plans/2026-07-11-001-feat-survey-drone-plan.md).
#
# Cross-checks that every server-side WorldObject/Item class in
# EcoServerMod/AdvancedElectronics/ has a matching-named asset under
# Assets/Art/AdvancedElectronics/, and flags any asset there that doesn't match
# a known server class name. This is the automatable half of U11's Verification
# ("the name cross-check reports zero mismatches") — a scripted grep diff per
# the plan's Approach note ("editor script or a documented dotnet/grep
# cross-check"), run headless, no Unity Editor required.
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

# WorldObject subclasses (exact base class "WorldObject", not WorldObjectItem<T>
# or WorldObjectComponent — the trailing-nothing-after-"WorldObject" pattern
# below distinguishes them; see this script's header for why).
mapfile -t WORLD_OBJECT_TYPES < <(
  grep -rhoE 'class [A-Za-z0-9_]+ : WorldObject *$' "$SERVER_DIR" \
    | sed -E 's/class ([A-Za-z0-9_]+) : WorldObject */\1/' \
    | sort -u
)

# Plain Item subclasses (exact base class "Item", not WorldObjectItem<T> —
# same trailing-nothing pattern).
mapfile -t ITEM_TYPES < <(
  grep -rhoE 'class [A-Za-z0-9_]+ : Item *$' "$SERVER_DIR" \
    | sed -E 's/class ([A-Za-z0-9_]+) : Item */\1/' \
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
  if ! printf '%s\n' "${ASSET_NAMES[@]:-}" | grep -qx "$t"; then
    echo "MISMATCH: Item '$t' has no matching-named icon asset under $ASSET_DIR"
    missing=1
  fi
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
  for t in "${WORLD_OBJECT_TYPES[@]:-}" "${ITEM_TYPES[@]:-}"; do
    [ "$a" = "$t" ] && is_known=1 && break
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
