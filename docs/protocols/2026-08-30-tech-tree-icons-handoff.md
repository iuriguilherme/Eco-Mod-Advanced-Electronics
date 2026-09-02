# Tech-Tree Icons — Session Handoff

Written 2026-08-30 for whoever picks this up next, cold. **The plan is not delivered.** This
records exactly where the work stands, what is proven, what is assumed, and what is left.

| | |
|---|---|
| **Branch** | `feat/tech-tree-icons`, ~37 commits ahead of `origin/main`, **nothing pushed** |
| **Head** | the last commit on the branch; `422e4d6` is where the code work ended, later commits are docs |
| **Both gates** | green — `scripts/validate-icon-binding.sh`, `scripts/validate-name-match.sh` |
| **Build / tests** | 0 errors, 260/260 |
| **Deployed** | server DLL 08-30 15:45 (= `d0232b0`); bundle 08-30 16:25, md5-identical to the repo's |
| **Pending deploy** | None. Verified 2026-08-31 by reading the bundle's own contents, not its timestamp |

## Read these first

1. `docs/solutions/architecture-patterns/mod-icons-reference-vanilla-art-by-name.md` — the
   mechanism. Four fields, four consumers, the surfaces with no override, and how to find a
   consumer instead of guessing at it. **Read this before touching an icon.**
2. `docs/protocols/2026-08-30-tech-tree-icons-protocol.md` — the test checklist, round 1 result,
   round 2 steps. The owner is the tester; no one else has the server.
3. `docs/guides/2026-08-eco-icon-atlas-guide.md` — finding names in vanilla's atlas, and the
   space-in-names trap that hid the right answer for a whole planning cycle.
4. `docs/guides/2026-08-research-paper-icon-spec.md` — measured art spec for the one icon that
   is fully specified and simply not drawn.

## Where each entry stands

Ten live entries. `MiningArmItem` is never shown by design. `AdvancedElectronicsAssemblyItem` is
excluded from the build (`<Compile Remove>` in the csproj, shelved when its recipes moved) — it
does not ship, so its icon is moot; it still has an icon-table row and a scene object, which is
harmless dead weight.

| Entry | Draws | State |
|---|---|---|
| `AdvancedElectronicsSkillBook` | `ElectronicsSkillBook` | **done** — every skill book in Eco is one picture, so this borrows no identity |
| `AdvancedElectronicsSkillScroll` | `ElectronicsSkillScroll` | **done** — same |
| `AdvancedElectronicsSkill` | `ElectronicsSkill` | borrowed sibling — **replace** |
| `AdvancedElectronicsUpgradeItem` | `ElectronicsUpgradeItem` | borrowed sibling — **replace** |
| `SurveyDroneItem` | render of its prefab, teal-blue, plated | **done** |
| `MiningDroneItem` | render, lime, plated | **done** |
| `HarvestDroneItem` | render, chocolate, plated | **done** |
| `DroneDockItem` | flat steel placeholder | **needs a real model**, then a render |
| `BatteryItem` | flat green placeholder | **needs art** |
| `EngineeringResearchPaperPostModernItem` | flat white placeholder | **needs art** — spec'd |

Also reported missing by the client and never addressed: `Electric Fuel`, `MiningComponent`,
`SurveyComponent`, `AdvancedElectronicsSulfuricBatteryTalentGroup`, `BatteryRecipe`. These are
tags, components and a recipe rather than items; they were out of the original scope and have
not been assessed.

## Remaining work, in dependency order

### 1. Run protocol rows T12 and T13 — the only thing left that needs the owner

**The rebuild is already done.** The earlier entry here said the bundle predated `422e4d6` and
had to be rebuilt; that was wrong, and it was wrong because it compared timestamps. Reading the
bundle itself settles it:

```
scripts/read-mod-bundle.py AssetBundles/AdvancedElectronics.unity3d --strings <dir>
```

The four retired names — `AdvancedElectronicsSkill`, `...SkillBook`, `...SkillScroll`,
`AdvancedElectronicsUpgradeItem` — appear **nowhere** in the built bundle, while the seven live
ones do. The deployed copy is md5-identical to the repo's. The retirement reached the client.

So what remains is the testing, which only the owner can do: protocol rows **T12** (recipe
icons) and **T13** (regression sweep).

Expected at T12: recipe icons become Eco's **default** missing-icon sprite, not vanilla's book.
That is option B working as chosen, not a failure.

### 2. Art brief — **written**

`docs/briefs/2026-08-31-icon-art-brief.html` is the artist-facing request. It is tracked with
`__ICON_*__` placeholders instead of image bytes; `docs/briefs/build-art-brief.py <output.html>`
inlines the three drone icons as data URIs and writes the standalone page that gets shared. The
owner holds the published link.

**Audience: the artist who built the drone model**, who has worked on the base game's art for
years. So the document carries no persuasion, no format spec and no mod overview — he knows all
three better than we do. It is a list of the eleven outstanding pieces grouped by kind of work,
each with its concept and its scope, and nothing else. Items 10 and 11 are marked undecided
because whether components warrant icons is a mod-design question, not an art question.

Full list: dock model, drone role variants, battery, PostModern paper, upgrade module, skill
emblem, three tag icons (`Electric Fuel`, `Post Modern Research`, `AdvancedElectronicsUpgrade` —
all three declared by the mod), and the two undecided components.

One line worth keeping if the document is ever rewritten: art ships **CC BY-SA 4.0**, so a piece
must be drawn fresh in vanilla's visual language rather than painted over vanilla's file.

The per-asset knowledge it was built from, kept here because it is the audit trail:

| Asset | What is known | What is missing |
|---|---|---|
| `EngineeringResearchPaperPostModernItem` | **Fully specified** — `docs/guides/2026-08-research-paper-icon-spec.md` has the measured grammar, geometry, the platinum ramp `#CAE4FF` and band `#7C553F`, and the decision already taken | Only the drawing |
| `BatteryItem` | No vanilla counterpart; atlas holds no `Battery*` rect | Everything — subject, composition |
| `DroneDockObject` **model** | Currently a hand-built Unity primitive; its icon is a render, so a real model fixes the icon for free | The 3D model; icon then needs no art at all |
| `Electric Fuel` tag | Client reports `Cannot find icon with name "Electric Fuel"` | Never assessed — is it ours or vanilla's? |
| `Skill Books` / research-paper **tag** icons | Vanilla's tag icons are one drawing on a grey plate | Whether we need our own at all |
| `MiningComponent`, `SurveyComponent` | Ours, carry a bare `[HasIcon]`, no asset | Whether components warrant icons |
| `AdvancedElectronicsSulfuricBatteryTalentGroup`, `BatteryRecipe` | Reported missing by the client | Out of original scope, unassessed |

Facts the brief carries, all established this session:

- **128 × 128, PNG with alpha.** Vanilla bakes at that size (`UISpriteBaker.cs:58`).
- Icons are **two sprites**: a full one carrying the background plate, and a `_FG` one without.
  The plate is reconstructed and committed at
  `Assets/Art/AdvancedElectronics/Sprites/IconBackground.png` — an artist can composite against
  it rather than inventing one.
- The house style is **isometric three-quarter, flat-shaded, readable at thumbnail size**.
  Extract comparisons with `docs/guides/2026-08-eco-icon-atlas-guide.md`.
- Fills must be **pairwise separable** — measure, do not eyeball. The closest pair in the mod's
  own set was once 20.7 dE, under the ~23 where two colours stop reading as different.
- Licence: **code is LGPL-3.0-or-later, art is CC BY-SA 4.0** (`LICENSE-ART`), and the repo is
  public. Whatever an artist contributes has to be licensable on those terms, agreed up front.

### 3. Licensing — **written**, and the evidence gap is closed

`docs/protocols/2026-08-31-slg-icon-licensing-ask.md` carries both questions in sendable form,
what each answer changes, and the evidence gathered before sending. Two things it settles that
the earlier plan only guessed at:

- **The reference mods set no precedent.** All four were decompressed and inspected. Each ships
  its own art under its own class names; **none** ships an asset named after a vanilla one, and
  none uses the `_FG` convention. So there is nothing to cite — worth knowing before citing
  something that does not exist. The check is by name only; pixel comparison would need a
  Texture2D decoder and was not done.
- **The ModKit ships with no licence file at all**, while distributing four of the game's own
  textures. That absence is the reason to ask rather than infer.

Still open: **where** to ask. Pick a venue where the answer is public and quotable.

### 4. The plan is stale and must be updated or superseded

`docs/plans/2026-08-10-001-feat-tech-tree-icons-plan.md` is placeholder-era: R3, R4 and R5
specify generated flat-colour placeholders, and AE1 accepts "absent from the missing-icon report"
as success. That premise was abandoned mid-session — placeholders were judged a regression. The
requirements the work is actually held to are **RI1–RI6 in the protocol**. The plan currently
does not describe this work. The owner has not yet chosen between revising it in place and
superseding it.

### 5. Small, tracked

- Comment headers in `AdvancedElectronics.cs` still say "WHY HasStaticIcon RATHER THAN
  `[HasIcon]`" — no longer true, both are set.
- `CONCEPTS.md` should gain **borrowed-sibling placeholder**. Not written because that file
  carries the owner's uncommitted changes.
- `validate-name-match.sh` discovers `AdvancedElectronicsAssemblyItem` even though it never
  ships, because it greps source rather than the built assembly.

## Things that will mislead you

- **Four fields, no shared default.** Setting one changes some surfaces and not others. The full
  map is in the learning; do not infer a fix worked from one surface.
- **Recipes and the display-name alias have no override point.** The only lever is what the
  bundle registers under the class name.
- **The space-named atlas generics are tag icons on grey plates.** `Skill Book`, `Skills`,
  `ModernUpgrade` are for category headers and look wrong in an item slot.
- **A sprite-name regex that omits spaces silently drops 27 rects**, including every generic.
  Count what you parse: 4059, not 4032.
- **The ModKit's `ItemTemplate` has no `FullImage` child**, so without one the client registers
  the foreground as the full icon and items have no background plate.
- **22 `IconName` script warnings at bundle build are pre-existing** — the ModKit template
  references Eco client scripts absent from a mod project. Two per item; they scale with item
  count and are not a regression.
- **`Curved/Standard` is a Built-in shader and this project is HDRP**, so prefabs render magenta
  in-Editor. The renderer substitutes an unlit material; that is why, not decoration.

## Process note

Four restarts were spent guessing at fields before each failing surface was traced in the game
source. The owner called this out and was right. The protocol exists to stop it happening again:
one surface, one grep, one named expectation per round.
