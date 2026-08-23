# Reading Eco's icon atlas: names to reference, crops to look at

Eco's icons are baked into one atlas rather than shipped as files. Two different jobs need
that atlas, and only one of them produces a file:

1. **Finding the name of an icon**, so a mod class can reference it with
   `[HasIcon("That Name")]` and draw real artwork with nothing shipped. This is the job that
   matters — see
   `docs/solutions/architecture-patterns/mod-icons-reference-vanilla-art-by-name.md`.
2. **Cropping one out to look at it**, so a human can judge which name to pick, or compare
   in-progress artwork against the real thing.

**A crop is disposable and must never be committed.** Vanilla artwork is Strange Loop Games'.
A *name* is not artwork — referencing one ships no pixels and is what vanilla itself does
across its own classes.

The three traps below are what cost the time.

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

## The three traps

**Sprite names can contain spaces, and those are the names worth having.** The generic,
content-neutral icons — `Skill Book`, `Skill Scrolls`, `Crafting Table`, `Modern Research` —
are all named with spaces, and a `[A-Za-z0-9_]+` name pattern drops every one of them without
error. That is 27 rects, silently missing, including the exact ones a mod adding a skill wants.
Count what you parsed: **4059**, not 4032. This trap hid the right answer through an entire
planning cycle and one implementation.

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
# Names CAN CONTAIN SPACES: a [A-Za-z0-9_]+ name pattern silently drops all 27
# generic tag icons -- "Skill Book", "Skill Scrolls", "Crafting Table" -- which
# are the most useful names in the file. Sanity check: 4059 rects, not 4032.
rects = {m["name"]: (int(m["x"]), int(m["y"]), int(m["w"]), int(m["h"]))
         for m in re.finditer(
             r"^      name: (?P<name>\S[^\r\n]*?)[ \t]*\r?\n"
             r"      rect:[ \t]*\r?\n        serializedVersion: \d+[ \t]*\r?\n"
             r"        x: (?P<x>\d+)[ \t]*\r?\n        y: (?P<y>\d+)[ \t]*\r?\n"
             r"        width: (?P<w>\d+)[ \t]*\r?\n        height: (?P<h>\d+)[ \t]*\r?$",
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
grep -oE '^      name: .+' <eco-checkout>/Content/Art/UI/Icons/UI_Icons_Baked_0.png.meta \
    | sed 's/^ *name: //' | sort -u | grep -i skill
```

### Prefer a generic icon over another specialty's

There are two kinds of candidate and they are not equivalent.

A **per-specialty** name like `ElectronicsSkillBook` is that specialty's artwork. Pointing
Advanced Electronics' skill book at it draws a picture that belongs to a different skill — a
better-looking placeholder, but still a placeholder, and one a player can misread.

A **generic** name is content-correct for anything of that kind. These are the space-named
rects the first trap hides:

| Name | Art | Fits |
|---|---|---|
| `Skill Book` | the blue skill book, no specialty emblem | any modded skill book |
| `Skill Scrolls` | a rolled grey scroll | any modded skill scroll |
| `Skill Books` | shelf of books | a skill-book category or group |
| `Crafting Table` | a generic crafting table | a modded craft station item |
| `Basic Research` / `Modern Research` / `Advanced Research` | research apparatus by tier | research-adjacent items |
| `Skills`, `Skills_FG` | the skills emblem | a skill with no better fit |

Full list of the space-named rects: `Advanced Research`, `Animal Skin`, `Asphalt Road`,
`Bank Accounts`, `Basic Research`, `Burnable Fuel`, `Civic Articles`, `Crafting Table`,
`Crop Seed`, `Election Processes`, `Liquid Fuel`, `Modern Research`, `Raw Food`,
`Scientist Specialty`, `Skill Book`, `Skill Books`, `Skill Scrolls`, `Work Orders`,
`Work Parties`, `Work Party`, `white tiger`, and the six `country tier N_FG` /
`town tier N_FG` settlement badges.

Reach for a per-specialty name only when the *content* genuinely matches — an Engineering
research paper one tier up can honestly use `EngineeringResearchPaperModernItem`, because it
is the same profession's paper. Advanced Electronics' book cannot honestly use Electronics'.

## Related

- `docs/solutions/architecture-patterns/mod-icons-reference-vanilla-art-by-name.md` — how a
  name becomes a rendered icon, and when a mod has to ship art instead
- `docs/guides/2026-08-unity-working-guide.md` — the Editor-side workflow for shipped art
- `scripts/validate-icon-binding.sh` — proves each scene item draws its own icon file
