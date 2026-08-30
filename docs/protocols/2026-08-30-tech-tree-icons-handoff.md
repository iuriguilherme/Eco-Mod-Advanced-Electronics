# Tech-Tree Icons — Session Handoff

Written 2026-08-30 for whoever picks this up next, cold. **The plan is not delivered.** This
records exactly where the work stands, what is proven, what is assumed, and what is left.

| | |
|---|---|
| **Branch** | `feat/tech-tree-icons`, 34 commits ahead of `origin/main`, **nothing pushed** |
| **Head** | `422e4d6` |
| **Both gates** | green — `scripts/validate-icon-binding.sh`, `scripts/validate-name-match.sh` |
| **Build / tests** | 0 errors, 260/260 |
| **Deployed** | server DLL 08-30 15:45 (= `d0232b0`); bundle 08-29 21:27, byte-identical to repo |
| **Pending deploy** | Bundle **must be rebuilt** — the scene changed at `422e4d6` and the deployed bundle predates it |

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

### 1. Rebuild and redeploy the bundle — blocking, mechanical

`422e4d6` removed four scene objects; the deployed bundle predates it. Until it is rebuilt,
the retirement has not reached the client.

Editor: `Eco Tools > Mod Kit > Build Current Bundle`. Then copy to `EcoModsDir` and restart.
Then run protocol rows **T12** (recipe icons) and **T13** (regression sweep).

Expected at T12: recipe icons become Eco's **default** missing-icon sprite, not vanilla's book.
That is option B working as chosen, not a failure.

### 2. Art — three items and one model

Needs a human artist. **A presentable brief was requested and not yet written** — that is the
next deliverable, covering at minimum: `BatteryItem`, `EngineeringResearchPaperPostModernItem`,
the `DroneDockObject` model, and the tag icons above. The research paper is already specified to
pixel level in its guide; the others are not.

### 3. The licensing question — unresolved, and it gates a real simplification

Shipping vanilla's art under our own class names ("option A") would fix every remaining
name-keyed surface at once and let **all four** source-side overrides be deleted. It was not
taken because it puts Strange Loop Games' pixels in a public LGPL repo and release archive.

What is known: the four reference mods that add skills — AnimalHusbandry, Mixology,
ArcaneKnowledge, IntelligenceSkillMod — contain **zero icon code of any kind**, and ship art in
their `.unity3d` bundles. **What was not done: decompressing those bundles to see whether the art
inside is vanilla-derived.** That is the question that actually bears on precedent, it is
answerable from `.references/Mods/`, and it needs an LZ4 reader. Do not assume it was ruled out.

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
