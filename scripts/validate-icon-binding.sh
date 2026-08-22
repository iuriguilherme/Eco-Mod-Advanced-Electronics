#!/usr/bin/env bash
# U4 — icon-binding validation gate (docs/plans/2026-08-10-001-feat-tech-tree-icons-plan.md).
#
# Proves that every item under the mod scene's "Items" root draws ITS OWN icon file,
# and not some other entry's. Headless, no Unity Editor required, so it can run inside
# the one Editor session between saving the scene and building the bundle — which is the
# last moment a mis-binding costs a re-run rather than a server restart.
#
# WHY THIS EXISTS, and why validate-name-match.sh is not enough. That gate answers "does
# every server class have a client asset named for it". It cannot answer "is that asset
# the one this class actually draws", because the two are joined by a GUID buried in the
# scene rather than by a name. MiningDroneItem passed the name gate for three weeks while
# rendering SurveyDroneItem's placeholder: both scene objects referenced the same sprite
# GUID, the mining drone had no icon file of its own, and the result rendered perfectly.
# A wrong icon is invisible by construction -- it looks exactly like success.
#
# HOW AN ICON REACHES THE SCREEN, and therefore what this walks:
#
#     scene GameObject named for the server class   (under the "Items" root)
#       └── child GameObject named "Icon"
#             └── child GameObject named "Foreground"
#                   └── Image component, m_Sprite -> {guid: ...}
#                         └── the .meta sidecar that declares that guid
#                               └── the PNG beside it
#
# The client's mod-icon registration finds those images BY GAMEOBJECT NAME (the Eco
# source's Client/Assets/Scripts/Mods/ModBundleManager.cs), not through the ItemTemplate
# component's serialized fields, so the names "Icon" and "Foreground" are load-bearing and
# a rename breaks every icon at once with the Inspector still looking correct. That is the
# third failure class below.
#
# The scene cannot be read line by line. Unity emits components BEFORE the GameObjects
# that own them, and every item carries a second Image on its "Background" child holding a
# shared ModKit sprite, so neither line order nor proximity attributes a sprite to an item.
# The object graph is rebuilt from m_GameObject/m_Father/m_Children instead, and the shared
# background is excluded by its position in that graph rather than by its GUID.
#
# Exit code 0 = clean. Exit code 1 = at least one failure, reported above the exit line.

set -euo pipefail
cd "$(dirname "$0")/.."

SCENE_DIR="Assets/Art/AdvancedElectronics/Scenes"
ICON_DIR="Assets/Art/AdvancedElectronics/Sprites/Icons"
ICON_TABLE="Assets/Art/AdvancedElectronics/Editor/AdvancedElectronicsBuildTools.cs"

for required in "$SCENE_DIR" "$ICON_DIR" "$ICON_TABLE"; do
    if [ ! -e "$required" ]; then
        echo "ERROR: $required not found — run from the repo root or check the path." >&2
        exit 2
    fi
done

# --- The expected set, derived from the icon table itself ----------------------
#
# Read from the tool's ItemIcons rows rather than written down as a number here. A
# literal count goes stale the moment a row is added, and a stale assertion gets deleted
# as noise by the next person who adds one -- taking the discovery check with it.
mapfile -t TABLE_ROWS < <(
    sed -n '/ItemIcons =/,/^    };$/p' "$ICON_TABLE" \
        | grep -oE '^\s*\("[A-Za-z0-9_]+"' \
        | grep -oE '"[A-Za-z0-9_]+"' \
        | tr -d '"' \
        | sort -u
)

if [ "${#TABLE_ROWS[@]}" -eq 0 ]; then
    echo "ERROR: parsed zero rows out of $ICON_TABLE's ItemIcons table." >&2
    echo "The table's shape must have changed; fix this parser rather than letting an" >&2
    echo "empty expected set pass everything." >&2
    exit 2
fi

echo "Icon table rows (need a scene object drawing their own icon): ${TABLE_ROWS[*]}"
echo

# --- Walk the scene's object graph --------------------------------------------
#
# Emits one tab-separated record per GameObject under the "Items" root:
#
#     <item name>\t<status>\t<sprite guid or "-">
#
# status is one of OK (a guid was reached), NOSPRITE (the Image's sprite reference is
# unset), NOFOREGROUND (no child named "Foreground" under "Icon"), NOICON (no child named
# "Icon"), or NOIMAGE (a "Foreground" child carrying no Image with a sprite field at all).
walk_scene() {
    local scene="$1"
    awk '
        /^--- !u![0-9]+ &[0-9]+/ {
            cls = substr($2, 4)
            id  = substr($3, 2)
            next
        }
        # GameObject
        cls == 1 && /^  m_Name: / {
            name = substr($0, 11)
            gsub(/\r$/, "", name)
            goName[id] = name
            next
        }
        # Transform (4) and RectTransform (224) carry the parent/child graph.
        (cls == 4 || cls == 224) && /^  m_GameObject: \{fileID: [0-9]+\}/ {
            match($0, /[0-9]+\}/); g = substr($0, RSTART, RLENGTH - 1)
            tGo[id] = g; goT[g] = id
            next
        }
        (cls == 4 || cls == 224) && /^  m_Father: \{fileID: [0-9]+\}/ {
            match($0, /[0-9]+\}/); f = substr($0, RSTART, RLENGTH - 1)
            tFather[id] = f
            next
        }
        # Image (a MonoBehaviour) is the only component here with a sprite field.
        cls == 114 && /^  m_Sprite: / {
            if (/guid: [0-9a-f]+/) {
                match($0, /guid: [0-9a-f]+/)
                sprite[componentGo[id]] = substr($0, RSTART + 6, RLENGTH - 6)
            } else {
                sprite[componentGo[id]] = "-"
            }
            next
        }
        cls == 114 && /^  m_GameObject: \{fileID: [0-9]+\}/ {
            match($0, /[0-9]+\}/); componentGo[id] = substr($0, RSTART, RLENGTH - 1)
            next
        }
        END {
            # Child lists, keyed by owning GameObject.
            for (t in tFather) {
                f = tFather[t]
                if (!(f in tGo)) continue
                kids[tGo[f]] = kids[tGo[f]] " " tGo[t]
            }

            # The "Items" root. Named by the ModKit scene convention and by the icon
            # finisher, which refuses to run without it.
            itemsRoot = ""
            for (g in goName) if (goName[g] == "Items") itemsRoot = g
            if (itemsRoot == "") { print "NOITEMSROOT"; exit }

            n = split(kids[itemsRoot], items, " ")
            for (i = 1; i <= n; i++) {
                item = items[i]
                if (item == "") continue

                icon = childNamed(item, "Icon")
                if (icon == "") { print goName[item] "\tNOICON\t-"; continue }

                fg = childNamed(icon, "Foreground")
                if (fg == "") { print goName[item] "\tNOFOREGROUND\t-"; continue }

                if (!(fg in sprite)) { print goName[item] "\tNOIMAGE\t-"; continue }
                if (sprite[fg] == "-") { print goName[item] "\tNOSPRITE\t-"; continue }

                print goName[item] "\tOK\t" sprite[fg]
            }
        }

        function childNamed(parent, want,   m, list, j, kid) {
            m = split(kids[parent], list, " ")
            for (j = 1; j <= m; j++) {
                kid = list[j]
                if (kid != "" && goName[kid] == want) return kid
            }
            return ""
        }
    ' "$scene"
}

SCENES=()
while IFS= read -r s; do SCENES+=("$s"); done < <(find "$SCENE_DIR" -name '*.unity' | sort)

if [ "${#SCENES[@]}" -eq 0 ]; then
    echo "ERROR: no .unity scene found under $SCENE_DIR." >&2
    exit 2
fi

RECORDS=""
for scene in "${SCENES[@]}"; do
    out="$(walk_scene "$scene")"
    if [ "$out" = "NOITEMSROOT" ]; then
        echo "NOTE: $scene has no GameObject named 'Items' — skipped (not a mod content scene)."
        continue
    fi
    RECORDS="$RECORDS$out
"
done

RECORDS="$(printf '%s' "$RECORDS" | sed '/^$/d')"

if [ -z "$RECORDS" ]; then
    echo "ERROR: no item GameObjects found under any scene's 'Items' root." >&2
    echo "Either the scene lost its items or this walker stopped working; an empty" >&2
    echo "discovery set must never be reported as a pass." >&2
    exit 2
fi

mapfile -t SCENE_ITEMS < <(printf '%s\n' "$RECORDS" | cut -f1 | sort -u)
echo "Scene item objects found under the 'Items' root: ${SCENE_ITEMS[*]}"
echo

# --- guid -> asset path, from the .meta sidecars ------------------------------
#
# The .meta beside an asset is what declares its GUID, and the GUID is the only thing the
# scene stores. One pass over every .meta under Assets/ builds the reverse map.
declare -A GUID_PATH
while IFS= read -r line; do
    meta="${line%%:guid: *}"
    guid="${line##*:guid: }"
    GUID_PATH["$guid"]="${meta%.meta}"
done < <(grep -r --include='*.meta' -m1 '^guid: ' Assets 2>/dev/null || true)

failures=0

# --- 1. Every scene item draws its own icon file ------------------------------
while IFS=$'\t' read -r item status guid; do
    [ -z "$item" ] && continue
    expected="$ICON_DIR/${item}_icon.png"

    case "$status" in
        NOICON)
            echo "MISMATCH: '$item' has no child GameObject named 'Icon'. The client finds icon images"
            echo "          by that exact child name, so this item registers no icon at all — and the"
            echo "          Inspector still looks correct. Rebuild it with Eco Tools > Advanced"
            echo "          Electronics > Finish All Item Icons, or restore the child's name."
            failures=1
            ;;
        NOFOREGROUND)
            echo "MISMATCH: '$item' has an 'Icon' child but no 'Foreground' child under it. The client"
            echo "          reads the image on the GameObject named 'Foreground'; a renamed child breaks"
            echo "          registration silently while the sprite is still visible in the Inspector."
            failures=1
            ;;
        NOIMAGE)
            echo "MISMATCH: '$item' has an Icon/Foreground child carrying no Image component. Nothing can"
            echo "          be registered from it."
            failures=1
            ;;
        NOSPRITE)
            echo "MISMATCH: '$item' has an Icon/Foreground Image whose sprite reference is UNSET."
            echo "          Expected it to reference $expected."
            failures=1
            ;;
        OK)
            actual="${GUID_PATH[$guid]:-}"
            if [ -z "$actual" ]; then
                echo "MISMATCH: '$item' draws sprite guid $guid, which no .meta under Assets/ claims."
                echo "          The reference is dangling — most often the result of an icon PNG being"
                echo "          deleted and recreated, which re-mints its GUID."
                failures=1
            elif [ "$actual" != "$expected" ]; then
                echo "MISMATCH: '$item' draws $actual"
                echo "          but should draw $expected."
                echo "          Two items resolving to one file is the mis-binding this gate exists for:"
                echo "          it renders perfectly and looks exactly like success."
                failures=1
            fi
            ;;
        *)
            echo "MISMATCH: '$item' returned an unrecognised status '$status' from the scene walk."
            failures=1
            ;;
    esac
done <<< "$RECORDS"

# --- 2. The table and the scene agree on the set ------------------------------
for row in "${TABLE_ROWS[@]}"; do
    if ! printf '%s\n' "${SCENE_ITEMS[@]}" | grep -qx "$row"; then
        echo "MISMATCH: icon table row '$row' has no GameObject under the scene's 'Items' root."
        echo "          Run Eco Tools > Advanced Electronics > Finish All Item Icons to create it."
        failures=1
    fi
done

for item in "${SCENE_ITEMS[@]}"; do
    if ! printf '%s\n' "${TABLE_ROWS[@]}" | grep -qx "$item"; then
        echo "MISMATCH: scene item '$item' has no row in $ICON_TABLE's ItemIcons table."
        echo "          The finisher therefore never gives it a sprite of its own, so it keeps whatever"
        echo "          it was last pointed at — which is how MiningDroneItem came to draw the survey"
        echo "          drone's placeholder. Add a row rather than excusing the entry here."
        failures=1
    fi
done

echo
if [ "$failures" -ne 0 ]; then
    echo "FAIL: one or more items do not bind to their own icon — see MISMATCH lines above."
    exit 1
fi

echo "PASS: every scene item under 'Items' draws its own icon file, and the table and the scene agree."
