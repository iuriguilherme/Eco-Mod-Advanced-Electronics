# Tech-Tree Icons — Manual In-Game Protocol

Owner-run verification of the icon work on `feat/tech-tree-icons`.

| | |
|---|---|
| **Tester** | Iuri (repository owner) — the only party with the live server and client |
| **Round 1 build** | `d0232b0` — **PASSED**, tester-reported 08-30 16:10, screenshots 48 |
| **Round 2 build** | `01f94af` — server assembly **and** bundle |
| **Requires restart** | Yes — a mod assembly does not hot-reload |
| **Requires Editor + bundle rebuild** | **Yes** — round 2 removes scene objects |

## Why this document exists

Verification up to now has been ad-hoc: change something, restart, look, guess again. That
produced four restarts and no record of what was asserted, what passed, or what was still
unknown. This protocol replaces that with a checklist, and it names what is *not* yet known so a
failure is informative rather than another round of guessing.

**The plan's requirements are stale and must be updated.** `docs/plans/2026-08-10-001-feat-tech-tree-icons-plan.md`
is a placeholder-era plan: R3, R4 and R5 specify generated flat-colour placeholders at 128×128
with distinct fills, and AE1 accepts "absent from the enumerated missing-icon report" as success.
That premise was abandoned — placeholders were judged a regression, and the deliverable became
real artwork. The requirements below are what the work is actually being held to. They are
recorded here so testing is traceable, and **they need folding back into the plan or into a
successor plan; until that happens the plan does not describe this work.**

## Requirements under test

- **RI1.** Every entry a player can see draws artwork that is correct for what it is — vanilla's
  own where the game already has the right picture, a render of our own model where we have one.
- **RI2.** No entry draws a flat-colour placeholder. Eco's own missing-icon sprite is preferred
  over one.
- **RI3.** No two distinct entries draw the same picture, except where vanilla itself uses one
  picture for the whole category.
- **RI4.** An entry's icon is consistent across **every** surface it appears on. A correct
  inventory icon and a placeholder in a tooltip is a failure, not a partial pass.
- **RI5.** Borrowed art is recorded as a placeholder to be replaced, and is distinguishable in
  the source from art that is final.
- **RI6.** Nothing borrowed reaches a commit or a release archive as an *asset*; naming vanilla's
  icon ships no pixels and is the intended mechanism.

## List 1 — Target state per entry

Ten entries are live. `MiningArmItem` is never shown to a player by design;
`AdvancedElectronicsAssemblyItem` is excluded from the build (`<Compile Remove>`, shelved) and
does not ship, so neither is under test.

| Entry | Should draw | Route | Kind |
|---|---|---|---|
| `AdvancedElectronicsSkill` | `ElectronicsSkill` | named | borrowed — replace |
| `AdvancedElectronicsSkillBook` | `ElectronicsSkillBook` | named | **final** — every skill book in Eco is one picture |
| `AdvancedElectronicsSkillScroll` | `ElectronicsSkillScroll` | named | **final** — every skill scroll is one picture |
| `AdvancedElectronicsUpgradeItem` | `ElectronicsUpgradeItem` | named | borrowed — replace |
| `SurveyDroneItem` | render of `SurveyDroneObject`, teal-blue, plated | bundle | **final** |
| `MiningDroneItem` | render of `MiningDroneObject`, lime, plated | bundle | **final** |
| `HarvestDroneItem` | render of `HarvestDroneObject`, chocolate, plated | bundle | **final** |
| `DroneDockItem` | Eco's missing-icon default | none | needs a real model, then a render |
| `BatteryItem` | Eco's missing-icon default | none | needs art |
| `EngineeringResearchPaperPostModernItem` | Eco's missing-icon default | none | needs art — spec'd in `docs/guides/2026-08-research-paper-icon-spec.md` |

## List 2 — Known defects, before this build

| # | Defect | Evidence | Fixed in |
|---|---|---|---|
| D1 | Inline icons in tooltip and chat text draw the mod's flat placeholders | Screenshots 47 — magenta square in the Advanced Electronics tooltip, purple beside the skill book | `d0232b0` — **verified fixed**, round 1 |
| D2 | Four entries still **ship** flat-colour PNGs in the bundle under their class names | icon table had 11 rows | `01f94af` — rows removed; scene objects and PNGs pending the Editor trip |
| D5 | **Recipe icons draw the flat placeholder.** A recipe's icon is its first product's class `Name` (`RecipeFamily.cs:241`), and that method is **not virtual** — there is no override point, so this cannot be fixed in the server classes | Tester-reported 08-30 16:10 | `01f94af` — fixed by removing the shipped art, so the class name resolves to nothing rather than to a square |
| D3 | `DroneDockItem`, `BatteryItem`, `EngineeringResearchPaperPostModernItem` draw flat colours, violating RI2 | Screenshot 47 storage chest — green, white and blue-grey squares | Not fixed — blocked on art |
| D4 | Comment headers in `AdvancedElectronics.cs` still say "WHY HasStaticIcon RATHER THAN `[HasIcon]`", which is no longer true — both are set | source | Not fixed — cosmetic, tracked |

D2 matters beyond tidiness: `ModBundleManager` calls `SetSpriteAlias(ServerName, DisplayName)`,
which maps the item's display name onto whatever the bundle registered under its class name. Any
surface that resolves by display name will keep finding the flat placeholder until those four
stop shipping one.

## List 3 — What `d0232b0` is expected to change

`ItemLinkable.ItemIconUILink` builds the inline text icon from `this.Name`, the class name
(`Server/Eco.Gameplay/Items/ItemLinkable.cs:56`). It is overridden in the four named entries to
follow `IconName` instead.

**Should change — this is the whole test:**

- Inline icons in tooltip and chat text for the skill, book, scroll and upgrade.

**Must NOT change — treat any movement here as a regression:**

- The three drone icons, their plates and their tints.
- The skill, book and scroll in inventory slots and recipe rows.
- The Advanced Electronics node in the Tech Tree.
- The dock, battery and research paper, which should still show Eco's default or their current
  placeholder — this build does not touch them.

## The four fields, and which surface each drives

Established by reading the Eco source. Testing one surface says nothing about the others, which
is why the checklist below covers all of them.

| Field set in our source | Eco source | Surface |
|---|---|---|
| `[HasStaticIcon("StaticIconName")]` | `ControllerMarshalerService.cs:414` | Ecopedia pages |
| `[HasIcon("…")]` | `TypeTooltips.cs:46` | type tooltips |
| `override string IconName` | `Item.cs:34` | inventory, hotbar, storage, recipe rows |
| `override ItemIconUILink` | `ItemLinkable.cs:56` | inline icons in tooltip and chat text |

## Round 2 — what changed and what to look for

D5 is the reason round 2 exists, and it forced a different shape of fix. Every remaining
name-keyed surface had one cause: **the bundle still registered flat-colour art under our class
names**, and several consumers resolve by class name with no override available. Removing that
art is the fix.

**Chosen deliberately (option B):** the mod now ships *no* icon for the skill, book, scroll and
upgrade. Surfaces that resolve by class name fall back to Eco's own missing-icon sprite rather
than to a coloured square — RI2 satisfied, RI1 not yet on those surfaces. Shipping vanilla's art
under our class names would fix them outright and is under consideration pending a licensing
check; that is tracked, not decided.

**Expected in round 2 — should change:**

- Recipe icons for the four entries stop being coloured squares. They become Eco's default
  missing-icon sprite, **not** vanilla's book — that is the accepted interim.

**Must NOT change:**

- Everything that passed in round 1: inventory slots, hotbar, recipe rows, Tech Tree node,
  inline tooltip and chat icons, Ecopedia pages.
- The three drone icons, plates and tints.

**Gate state going into the Editor trip.** `validate-icon-binding.sh` is RED with exactly four
`scene item … has no row` messages. That is the correct reading of a tree whose table rows are
gone but whose scene objects are not yet removed; it must be GREEN after the Editor step below.
`validate-name-match.sh` is green and now reports the four as named-icon types shipping no asset
by design — an exemption discovered from the source, so deleting a binding makes its type
required again. Both sides of that were tested.

## Editor steps for round 2

1. Let the domain reload finish.
2. `Eco Tools > Advanced Electronics > Retire Unlisted Item Icons` — deletes the four scene
   objects. It names each one it removes.
3. Save the scene.
4. Stop and tell me: I delete the four PNGs and their `.meta` files, then run both gates. Both
   must be green before the bundle is built.
5. `Eco Tools > Mod Kit > Build Current Bundle`.
6. Tell me again: I copy the bundle, rebuild and deploy the assembly.
7. Restart, then run the checklist below.

## Procedure

Restart the server first, then log in. Setup:

```
/give advancedelectronicsskillbook
/give advancedelectronicsskillscroll
/give advancedelectronicsupgrade
/give engineering research paper post modern
/give battery
/give surveydrone
/give miningdrone
/give harvestdrone
/give dronedock
```

Work the table top to bottom. Record **PASS**, **FAIL** or **N/A**, and for any FAIL note what
was drawn instead. A screenshot per failing row is worth more than a description.

| # | Surface | What to open | Expected |
|---|---|---|---|
| T1 | Storage / inventory slot | Open the storage chest holding the given items | Book = vanilla blue book; scroll = vanilla scroll; upgrade = green circuit board; drones = plated chassis, three visibly different colours; dock, battery, paper = Eco's default or current placeholder |
| T2 | Hotbar | Move the book and a drone to the hotbar | Same pictures as T1 |
| T3 | Recipe row + product panel | Laboratory → Crafting → find "Advanced Electronics Skill Book" | Row icon and the large Product image both vanilla's blue book, on the same olive plate as the other skill books beside it |
| T4 | Recipe row, upgrade | Electronics Assembly → Crafting → "Advanced Electronics Upgrade" | Green circuit board on the navy plate |
| T5 | Tech Tree node | Tech Tree → Advanced Electronics | Node icon = Electronics' green board |
| T6 | **Inline tooltip icon** | Hover Advanced Electronics in the Tech Tree | The squares beside "Advanced Electronics Skill Book" and the header become the real book / board. **This is what `d0232b0` changes.** |
| T7 | **Inline chat icon** | Hover any item and read its "Crafted At" / "Requires" lines | Every inline icon for our four entries is real art, none is a flat square |
| T8 | Ecopedia page | Ecopedia → Items → Skill Books → Advanced Electronics Skill Book; and Professions → Engineer → Advanced Electronics | Page icons are real art |
| T9 | Type tooltip | Hover the *skill name* where it appears as a type link | Real art |
| T10 | Client log | After opening every surface above | No `Cannot find icon with name "…"` naming any of our entries; no new exception; no duplicate-key error |
| T11 | Drone regression | Compare the three drones side by side | Still plated, still three distinct colours — unchanged from before this build |
| T12 | **Recipe icon** | Laboratory → Crafting → the "Advanced Electronics Skill Book" recipe row and its Ecopedia recipe link | Eco's default missing-icon sprite, **not** a coloured square. Vanilla's book here would need option A |
| T13 | Regression sweep after asset removal | Re-check T1, T3, T6 | Unchanged from round 1 — removing the shipped art must not disturb the surfaces the source overrides drive |

## Verdict

| # | Result | Drew instead | Note |
|---|---|---|---|
| T1 | | | |
| T2 | | | |
| T3 | | | |
| T4 | | | |
| T5 | | | |
| T6 | | | |
| T7 | | | |
| T8 | | | |
| T9 | | | |
| T10 | | | |
| T11 | | | |
| T12 | | | |
| T13 | | | |

## If a row fails

Do not restart again on a guess. A failure at T6 or T7 means `ItemIconUILink` is not the path
for that surface, or the override is not reached — the next step is tracing the client's icon-tag
resolution in the Eco source, not trying a fifth field. A failure at T1–T5 after they previously
passed is a regression and takes priority over everything else.

## Out of scope for this pass

- Art for the dock, the battery and the PostModern research paper (D3). Three entries, blocked on
  drawing, not on mechanism.
- Removing the four dead flat-colour PNGs and their scene objects (D2). Needs an Editor trip.
- Folding these requirements back into the plan, or superseding it.

## Related

- `docs/solutions/architecture-patterns/mod-icons-reference-vanilla-art-by-name.md` — the mechanism
- `docs/guides/2026-08-research-paper-icon-spec.md` — the PostModern art brief
- `docs/plans/2026-08-10-001-feat-tech-tree-icons-plan.md` — **stale**, placeholder-era premise
