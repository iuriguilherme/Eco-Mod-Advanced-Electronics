# Research paper icons: the visual grammar, and the missing PostModern tier

`EngineeringResearchPaperPostModernItem` is the one tech-tree entry that cannot borrow a vanilla
name. Research paper art is a two-axis system — **family** and **tier** — and PostModern is a
tier vanilla never drew. Reusing `EngineeringResearchPaperModernItem` would read as the Modern
paper, which is a different item the player also holds.

This is the spec for drawing the missing one, derived by measuring vanilla's own sheet rather
than by describing it from memory.

## The matrix vanilla ships

| Family | Basic | Advanced | Modern | PostModern |
|---|:--:|:--:|:--:|:--:|
| Gathering | ✓ | — | — | — |
| Culinary | ✓ | ✓ | ✓ | — |
| Dendrology | ✓ | ✓ | ✓ | — |
| Geology | ✓ | ✓ | ✓ | — |
| Metallurgy | ✓ | ✓ | ✓ | — |
| Agriculture | — | ✓ | ✓ | — |
| Engineering | — | ✓ | ✓ | **the gap** |

Seventeen rects, all 128×128, all in `UI_Icons_Baked_0.png`. No PostModern anywhere, in any
family — so this is a new tier design, not a missing variant.

## What varies, measured

Comparing Geology's three tiers cell by cell (16 px grid, share of pixels changed):

```
Basic -> Advanced            Advanced -> Modern
 26  42  43  43  43  43  45  16      30  44  43  50  50  48  40  11
 29   0   0   1   1   0   0  37      32  24   2   0   1   0  31  11
 37   0   0   0   0   1   0  39      25  25   0   0   0   1  25  25
 37   0   0   0   0   0   0  43      20  21   0   0   0   0  21  25
 37   0   0   0   0   0   0  42      19  17   0   0   0   0  24  16
 37   8   0   0   0   0   1  43      27  49  74  45   1   0  29  13
 31   4   6   7   3   5  13  34      29  69  89  62  20  24  66  20
 17  41  33  31  31  35  41  28      14  40  42  40  48  48  49  17
```

Two things fall straight out:

**The emblem never moves.** The interior block — rows 1–4, columns 2–5, roughly `x 32..96,
y 16..80` — is 0% changed across every tier step. Family is carried entirely by that emblem, and
tier never touches it.

**Tier lives in the border ring and the lower-left badge.** Basic → Advanced changes only the
ring. Advanced → Modern changes the ring *and* a block at rows 5–6, columns 1–3 — roughly
`x 16..64, y 80..112`, the gold star.

Across families at the same tier, 43–54% of pixels differ; across tiers within a family, 17–31%.
The emblem dominates the silhouette; the tier furniture is deliberately quieter.

## The constants

| Element | Value |
|---|---|
| Canvas | 128 × 128, matching every icon in the atlas |
| Sheet | off-white, `rgb(250, 250, 248)`-ish, filling most of the frame |
| Side rails | navy `rgb(66, 99, 144)` on the left and right edges — **identical in every family and every tier** |
| Emblem | `x 32..96, y 16..80`, family-specific, tier-invariant |
| Text lines | lower-right, grey rules standing in for body text |
| Badge slot | `x 16..64, y 80..112`, empty until Modern |

The navy rails were the thing worth measuring: Dendrology looks green-framed and Culinary
red-framed, but that is the emblem bleeding to the edge. Sampled at `(4, 64)` every family
returns the same navy. Do not tint the rails per family.

## The tier ladder

| Tier | Top edge | Badge slot | Reads as |
|---|---|---|---|
| Basic | plain paper, `rgb(252, 252, 248)` — no header band | empty | a loose sheet |
| Advanced | tan band, `rgb(160, 137, 119)` | empty | a bound document |
| Modern | darker brown band, `rgb(142, 111, 91)`, with red accent `rgb(120, 32, 0)` in the ring | gold star, mean `rgb(222, 189, 126)` | a sealed, awarded document |

The progression is **accumulating formality**: bare sheet, then binding, then binding plus seal
plus award. Each step adds furniture; nothing is ever taken away, and the paper never changes
what it is about.

## Drawing PostModern

The brief, then, is narrow and well-constrained:

1. **Keep the Engineering emblem exactly** — the grey gear with the crosshair hub, at
   `x 32..96, y 16..80`, lifted unchanged from `EngineeringResearchPaperAdvancedItem` /
   `...ModernItem`. A player must read "engineering paper" before they read the tier.
2. **Keep the navy side rails** and the off-white sheet.
3. **Take the next step in the two tier slots** — the top band and the badge — so the icon reads
   as one rung above Modern at thumbnail size, without competing with the emblem.

Three directions that fit the established grammar. This is a design choice, not a derivation:

- **Two stars.** The most literal continuation: Modern's single gold star becomes two in the
  badge slot. Cheapest to draw, instantly legible as "one tier up", and it reuses an existing
  shape so the family stays coherent. Risk: at 128 px two stars in a 48 × 32 slot get small.
- **A different badge metal.** Keep one star, change gold to platinum/white-blue, and darken the
  top band another step. Preserves the silhouette exactly and signals rank by material the way
  the top band already signals it by tone. Risk: the gold→platinum read is weaker at thumbnail
  size than a count change.
- **A ribbon or wax seal.** Escalate the Modern red accent into an actual seal in the badge slot.
  The most visually distinct option and the most work, and it departs furthest from the
  star motif the other tiers established.

The first is the safest fit with what vanilla actually does; the third looks best in isolation
and risks reading as a different item family.

Whichever is chosen, the icon then ships through the asset-bundle route — the mod cannot add to
vanilla's baked atlas. See
`docs/solutions/architecture-patterns/mod-icons-reference-vanilla-art-by-name.md` for that path
and its traps.

## Reference crops

`.references/VanillaIcons/papers/` holds the extracted tier ladder and the Modern tier across
families, cropped for side-by-side comparison. Git-ignored and never committed — vanilla art is
Strange Loop Games'. Regenerate with the recipe in
`docs/guides/2026-08-eco-icon-atlas-guide.md`.

## Related

- `docs/guides/2026-08-eco-icon-atlas-guide.md` — reading the atlas, finding names, the
  space-in-names trap
- `docs/solutions/architecture-patterns/mod-icons-reference-vanilla-art-by-name.md` — when a name
  suffices and when art must be drawn
