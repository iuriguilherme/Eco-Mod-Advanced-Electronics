# Extracting vanilla icons for side-by-side comparison

Placeholder icons are judged by holding them next to the real game's art at the same size.
Eco's icons are not shipped as individual files — they are baked into one atlas — so getting
a single vanilla icon out means cropping a named rect from that atlas.

**The output is disposable. The recipe is not.** Vanilla artwork is Strange Loop Games',
never ours: it must not reach a commit, a tracked file, or a release archive. What is worth
keeping is the coordinate convention and the two traps below, because both cost time to
rediscover.

## Where it goes, and why it matters

Write crops to `.references/VanillaIcons/`, and to nowhere under `Assets/`.

`.references/` is ignored by the leading-dot rule at `.gitignore:119`, so nothing there can
be committed by accident. The `Assets/` exclusion is the load-bearing half: Unity imports
everything under `Assets/`, which mints a `.meta` sidecar that *is* a tracked-file candidate,
trips the packaging staleness guard, and makes the asset eligible to enter the bundle as a
scene dependency. That last one is the single route by which borrowed art could ship.

Confirm both after extracting:

```bash
git status --short --untracked-files=all      # must not list the crops
git check-ignore -v .references/VanillaIcons/<Name>.png
```

## The two traps

**The atlas meta has two name tables, and only one of them is true.** The sprite-sheet
entries carry `name:` plus a `rect:` — those are real. Further down the same file,
`nameFileIdTable` maps names to internal ids and **retains stale entries for sprites that no
longer exist**, including `_FG` names with no rect behind them. Grepping that table for a name
returns a hit for artwork that is not in the atlas. Read the rects.

**Unity's rect origin is bottom-left; every cropping tool's is top-left.** A rect is stored as
`x`, `y`, `width`, `height` with `y` measured up from the bottom of the atlas, so the top edge
a cropper wants is:

```
top = atlas_height - (y + height)
```

Getting this wrong does not fail — it silently lands on the sprite above or below, which looks
like a plausible icon. Open each crop and check it is the one you asked for.

## The recipe

The atlas and its meta live in the Eco **source checkout** (not in this repository, and not in
the shipped dedicated server, whose assemblies are embedded):

```
<eco-checkout>/Content/Art/UI/Icons/UI_Icons_Baked_0.png
<eco-checkout>/Content/Art/UI/Icons/UI_Icons_Baked_0.png.meta
```

As of Eco 0.14 that atlas is 8192×8192 and holds a little over 4,000 rects, all of the icons
this mod cares about at 128×128 — which is where the mod's own placeholder size comes from
(`PlaceholderIconSize` in `Assets/Art/AdvancedElectronics/Editor/AdvancedElectronicsBuildTools.cs`).

Needs Pillow. Pass the checkout path as an argument rather than hard-coding it — machine-local
paths do not belong in this repository.

```python
# extract_icons.py <eco-checkout> <out-dir> <SpriteName> [SpriteName ...]
import re, sys
from pathlib import Path
from PIL import Image

eco, out, wanted = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3:]
png = eco / "Content/Art/UI/Icons/UI_Icons_Baked_0.png"
meta = Path(str(png) + ".meta").read_text(encoding="utf-8", errors="replace")

# Sprite-sheet rects only -- never nameFileIdTable, which keeps stale names.
rects = {m["name"]: (int(m["x"]), int(m["y"]), int(m["w"]), int(m["h"]))
         for m in re.finditer(
             r"^      name: (?P<name>[A-Za-z0-9_]+)\s*\n"
             r"      rect:\s*\n        serializedVersion: \d+\s*\n"
             r"        x: (?P<x>\d+)\s*\n        y: (?P<y>\d+)\s*\n"
             r"        width: (?P<w>\d+)\s*\n        height: (?P<h>\d+)\s*$",
             meta, re.MULTILINE)}

img = Image.open(png)
out.mkdir(parents=True, exist_ok=True)
for name in wanted:
    if name not in rects:
        print(f"MISSING {name}"); continue
    x, y, w, h = rects[name]
    top = img.height - (y + h)          # bottom-left origin -> top-left box
    img.crop((x, top, x + w, top + h)).save(out / f"{name}.png")
    print(f"{w}x{h}  {name}")
```

## Finding a name

Sprite names are the server class names, so the counterpart of a mod class is usually the same
name minus the mod's prefix. List what exists before guessing:

```bash
grep -oE '^      name: [A-Za-z0-9_]*Skill[A-Za-z0-9_]*' <eco-checkout>/Content/Art/UI/Icons/UI_Icons_Baked_0.png.meta \
    | sed 's/.*name: //' | sort -u
```

Where a mod class has no vanilla counterpart, take the nearest sibling — it still answers the
size and readability question the comparison exists for. The tech-tree icon work used:

| Mod class | Vanilla sprite | Why |
|---|---|---|
| `AdvancedElectronicsSkill` | `ElectronicsSkill` | Direct sibling; `EngineerSkill` is the parent profession and worth pulling alongside it |
| `AdvancedElectronicsSkillBook` | `ElectronicsSkillBook` | Direct counterpart |
| `AdvancedElectronicsSkillScroll` | `ElectronicsSkillScroll` | Direct counterpart |
| `EngineeringResearchPaperPostModernItem` | `EngineeringResearchPaperModernItem` | Vanilla has no PostModern tier; Modern is the tier below |

One thing that surprised the plan that commissioned this: vanilla *does* ship a per-specialty
skill emblem (`ElectronicsSkill` is one), so a specialty skill has a direct sibling rather than
only a generic stand-in.

## Related

- `docs/guides/2026-08-unity-working-guide.md` — the Editor-side workflow these placeholders feed
- `scripts/validate-icon-binding.sh` — proves each scene item draws its own icon file
