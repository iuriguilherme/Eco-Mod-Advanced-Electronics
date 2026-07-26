---
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
product_contract_source: ce-brainstorm
date: 2026-07-26
type: feat
topic: broaden-survey-materials
---

# Broaden Survey Detection to Material Types - Plan

## Goal Capsule

**Objective.** Broaden the survey drone from ore-only prospecting to a curated set of block
materials, and shift the readout from an ore-centric concentration model to a quantity-led one so
the numbers are meaningful for common bulk materials as well as rare ore.

**Product authority.** Solo maintainer, validated by live play.

**Active scope.** The detection + reporting side only — *what* the drone finds and *how* it is
reported, on the two surfaces the player and admin actually use: the **dock-window Survey panel**
and the **admin chat commands**. The material tag/type **selector** (choosing/limiting which
materials to survey for) is a separate, later brainstorm and is **not** active scope.

**Product Contract preservation.** Changed from the requirements-only version, user-directed this
session: (a) Coal is classified as Ore (include all `Minable`); (b) readout surfaces reduced to the
dock panel + chat — the world-space floating text and the object tooltip are **retired**, not
migrated (they were unwanted and the world-text likely never rendered). R6 (world-text slot cap) is
dropped as moot.

**Open blockers.** None. Park-and-sweep already sweeps every column, so real per-block counts exist.

---

## Product Contract

### Summary

The drone detects and reports blocks in six material categories — **Rock, Ore (incl. Coal), Sulfur,
Peat, Clay, Sand** — at **specific-type** granularity (Limestone, Granite, IronOre, CopperOre,
Coal, Sulfur, Clay, Sand, ...). For each material type found in the area, the readout leads with
**quantity** (block count) plus the **shallowest location** and the **depth range**. The readout
lives in the **dock-window Survey panel** (players) and the **`/drone survey` / `/drone status`
chat** (admin debugging). No cap on how many types are reported.

### Requirements

**R1 — Detect the six material categories, by specific type.**
Classify each scanned block: **any `Minable` block** (all rock types, all ores, sulfur — Coal is an
ore) is recorded under its specific type name; **`Diggable` blocks are recorded only when their type
is Sand, Clay, or Peat**. All other blocks (Dirt, Grass, Gravel, …) are ignored. (Today the reader
gates on `Minable` and then a `*Ore`/`Coal` name filter, dropping rock and the diggables.)

**R2 — Quantity-led readout per material type.**
For each detected material type in the area, the readout shows: **quantity** (total block count in
the area) as the headline, the **shallowest occurrence** as a location (x, y, z), and the **depth
range** (shallowest–deepest below surface). Example: `Limestone: ~210 blocks, shallowest at (412,
63, -88), depth 2-14`.

**R3 — Concentration is no longer the headline.**
The concentration ratio is dropped as the lead metric; quantity is the primary signal for every
material. Concentration may be kept as a secondary ore-only detail if it adds value.

**R4 — Readout surfaces: dock panel + chat only.**
The full per-material list renders in the **dock-window Survey panel** (`SurveyAreasComponent`,
unlimited text) and the **admin chat commands** (`/drone survey`, `/drone status`). Both show every
detected material type, sorted by quantity descending.

**R5 — Raw natural block forms only; crushed variants excluded.**
Rock, Ore, and Sulfur have crushed variants, but those are `[Diggable]`/`[Crushed]` items, not
`[Minable]` terrain — so the `Minable` gate already excludes them, and the diggable allowlist
(Sand/Clay/Peat) does not match crushed names. The survey reports only raw terrain blocks.

**R6 — Retire the unwanted readout surfaces.**
The world-space floating text above the dock and the object tooltip are removed as survey-readout
surfaces (unwanted; the world-text never reliably rendered). No survey data is pushed to animated
string states or the tooltip. (A non-text animation state such as `Working` may remain for future
art; it is not a readout.)

### Primary flow

1. The drone park-and-sweeps the area, scanning every column top-down.
2. Each block is classified (R1); matching blocks are recorded under their specific type name,
   accumulating area-total count, shallowest occurrence, and depth range per type.
3. The dock panel and chat list every detected type with quantity + shallowest location + depth
   range, sorted by quantity.

### Acceptance examples

- **Mixed column:** limestone over an iron seam reports `Limestone` (high count, shallow) and
  `IronOre` (lower count, deeper) as distinct types.
- **Non-target ignored:** dirt, grass, and gravel are not reported.
- **Bulk material is meaningful:** `Sandstone: ~340 blocks, depth 1-9` reads as a real deposit.
- **Crushed excluded:** never reports "Crushed Sulfur"/"Crushed Limestone" — only raw terrain.
- **Coal included:** a coal seam reports `Coal: ~60 blocks, ...`.
- **No floating text / tooltip:** the dock shows survey results only in its window panel and via
  chat; nothing floats over the object and the hover tooltip carries no survey list.

### Key decisions

- **Classify by mining marker, not a curated list:** all `Minable` → reported by specific type
  (covers Rock, Ore, Sulfur, Coal); `Diggable` allowlisted to {Sand, Clay, Peat}. Simplest rule that
  matches the six categories, and specific-type granularity falls out of the block type name.
- **Coal is Ore** — included via `Minable`.
- **Quantity is the headline metric**; concentration retired as the lead.
- **Raw forms only** — free from the `Minable` gate.
- **Surfaces: dock panel + chat only** — world-text and tooltip retired (user-directed).
- **No survey-count cap** — limiting which materials is the selector's job (deferred).

### Out of scope

- The material **selector** (choose/limit which materials, tag + specific-type picker) — next
  brainstorm.
- Dirt, grass, gravel, and other non-listed blocks.
- Humanizing type names (`IronOre` → "Iron Ore") — cosmetic, deferred.

---

## Planning Contract

### Approach

Three coherent changes: broaden the block classifier (Eco-side), generalize the finding model from
concentration to quantity in the Eco-free Navigation library (test-first, it has a unit suite), then
repoint the two wanted readout surfaces to the new model and delete the two unwanted ones. The
sampling engine (park-and-sweep) is untouched — it already produces full per-block coverage.

The classifier rests on Eco block markers the existing `EcoOreReader` already proved by reflection:
`Minable` is present on raw ore/rock/sulfur and absent on crushed variants. `Diggable` is the
parallel marker for dug materials; its exact usability as `block.Is<Diggable>()` and the specific
type names (`Sand`, `Clay`, `Peat` after stripping the `Block` suffix) are **assumptions to verify
live**, consistent with this codebase's established ASSUMPTION convention for stripped
reference-assembly semantics.

### Key technical decisions

- **KTD1 — Classification via `Minable`/`Diggable` markers + a small diggable allowlist**, not a
  hardcoded material list. Reuses the proven `block.Is<Minable>()` probe; adds
  `block.Is<Diggable>()` gated by a `{Sand, Clay, Peat}` name allowlist. Extensible (add a diggable =
  one entry). (session-settled: user-directed — Coal counts as Ore, so all `Minable` is in.)
- **KTD2 — Finding model becomes area-total quantity + shallowest + depth range.** `SurveyFinding`
  gains `Count`, `DepthMin`, `DepthMax`; the per-area aggregation sums counts across plots and tracks
  the shallowest occurrence and depth extent, replacing best-plot-by-concentration. Concentration is
  retained on the struct but demoted from the readout headline. Done test-first against
  `SurveyRecordTests`.
- **KTD3 — Retire world-text + tooltip readout.** Delete `SurveyReadoutTooltip` and the survey text
  pushed via `SetAnimatedState` in `RefreshReadout`; keep `RefreshReadout`'s snapshot-persist and
  tab-refresh responsibilities. (session-settled: user-directed — only the dock panel and chat are
  wanted.)
- **KTD4 — "ore" → "material" naming.** Generalize `IOreReader`/`TryGetOreType`,
  `EcoOreReader`, `SurveyRecord` keys, and `FormatOreLine` to material vocabulary. A rename, not a
  behavior change; keep it inside the units that already touch each file.

### Implementation units

#### U1. Broaden the block classifier to materials

**Goal.** Detect the six categories by specific type instead of ore-only.

**Requirements.** R1, R5. KTD1, KTD4.

**Dependencies.** none.

**Files.**
- `EcoServerMod/AdvancedElectronics/EcoOreReader.cs` — broaden + rename (`EcoMaterialReader`,
  `TryGetMaterial`).
- `EcoServerMod/AdvancedElectronics.Navigation/IOreReader.cs` — rename interface to
  `IMaterialReader` / `TryGetMaterial` (find its actual path; it lives in the Navigation project).
- `EcoServerMod/AdvancedElectronics/OreSensorComponent.cs` — update the call site + `oreReader`
  field naming; `SampleColumn` passes the returned material string through unchanged.

**Approach.** Replace the `Minable`-then-`*Ore`/`Coal` filter with: return the stripped type name
when `block.Is<Minable>()`; else when `block.Is<Diggable>()` and the stripped name is in
`{Sand, Clay, Peat}`; else no material. Keep the `Block`-suffix strip that already yields the
specific type name. Preserve the ASSUMPTION comments and add one for `Diggable`/diggable names.

**Patterns to follow.** The existing `EcoOreReader` structure and its ASSUMPTION-comment convention.

**Test scenarios.** `Test expectation: none — Eco-world-coupled classifier; verified live` (the
`IMaterialReader` contract is exercised through the Navigation fakes in U2, and the live behavior is
confirmed in the Verification Contract). Note the specific live checks: a rock block (e.g. Limestone)
now reports; a dirt/grass/gravel block does not; Sand/Clay/Peat report; a crushed variant does not.

#### U2. Quantity-led finding model (Navigation, test-first)

**Goal.** Aggregate per material as area-total count + shallowest occurrence + depth range, replacing
best-plot-by-concentration.

**Requirements.** R2, R3. KTD2.

**Dependencies.** none (pure library; can land alongside U1).

**Files.**
- `EcoServerMod/AdvancedElectronics.Navigation/SurveyFinding.cs` — add `Count`, `DepthMin`,
  `DepthMax`; keep `Concentration`.
- `EcoServerMod/AdvancedElectronics.Navigation/SurveyRecord.cs` — per-material area aggregation:
  total count across plots, min-depth occurrence (position + depth), depth min/max; `Findings(areaId)`
  emits one finding per material. Keep `Coverage`.
- `EcoServerMod/AdvancedElectronics.Navigation.Tests/SurveyRecordTests.cs` — update/extend.

**Approach.** Aggregation shifts from per-plot to per-area-per-material. Retain the idempotent
sampled-block dedupe. `Concentration`, if kept, is computed area-level (material blocks / sampled
blocks) for the ore case only.

**Execution note.** Test-first: update `SurveyRecordTests` to the quantity model, watch them fail,
then change `SurveyRecord`/`SurveyFinding`.

**Test scenarios.**
- Records N blocks of one material across two plots → `Findings` reports `Count == N`.
- Shallowest occurrence is the min-depth block; `DepthMin`/`DepthMax` bracket all occurrences.
- Two materials in one area → two findings, each with its own count/shallowest/depth range.
- Idempotent: re-recording the same (x,y,z) does not inflate `Count`.
- `ClearArea` drops that area's findings; other areas unaffected.
- Empty area / no matching material → no findings.

#### U3. Repoint the dock panel + chat to the material model

**Goal.** Render quantity + shallowest + depth-range per material, sorted by quantity, in the two
wanted surfaces.

**Requirements.** R2, R4. KTD2, KTD4.

**Dependencies.** U2.

**Files.**
- `EcoServerMod/AdvancedElectronics/SurveyAreaEntry.cs` — `OreFindingSnapshot` gains `Count`,
  `DepthMin`, `DepthMax`; `SetFindings`/`ReadFindings` map them.
- `EcoServerMod/AdvancedElectronics/DockReadout.cs` — `FormatMaterialLine(SurveyFinding)`:
  `"<Name>: ~<count> blocks, shallowest at <pos>, depth <min>-<max>"`; drop the concentration-led
  format.
- `EcoServerMod/AdvancedElectronics/SurveyAreasComponent.cs` — list + results order by `Count` desc;
  the compact list line shows top material by count.
- `EcoServerMod/AdvancedElectronics/DroneCommands.cs` — `/drone survey` and `/drone status` use the
  new line format and sort.

**Approach.** Mechanical repoint from the concentration finding to the quantity finding; ordering
changes from `Concentration` to `Count` descending.

**Patterns to follow.** The existing snapshot round-trip (`OreFindingSnapshot.From` / `ToSurveyFinding`)
and the current tab/chat readout builders.

**Test scenarios.** `Test expectation: none — server-component/formatting glue verified live`;
`DockReadout` formatting is pure and could get a small unit test if convenient, but the finding math
is already covered in U2.

#### U4. Retire the world-text and tooltip readout

**Goal.** Remove the two unwanted survey-readout surfaces.

**Requirements.** R6. KTD3.

**Dependencies.** none (independent of U3; sequence after to avoid churn on shared files).

**Files.**
- `EcoServerMod/AdvancedElectronics/DroneDock.cs` — delete `SurveyReadoutTooltip`; in
  `RefreshReadout` drop the `SetAnimatedState` survey text pushes (status/ore-line/coverage slots),
  keeping the snapshot persist + `surveyTab.RefreshResults()`; keep `PushWorkingState` (animation, not
  text).
- `EcoServerMod/AdvancedElectronics/DockReadout.cs` — remove `BuildStateLines`/`FormatStatusLine` and
  the state-slot constants if they become unused after the tooltip/world-text removal.

**Approach.** Straight deletion of the unwanted surface code; confirm nothing else consumes the
removed helpers before deleting them.

**Test scenarios.** `Test expectation: none — removal; verified by build + live (no floating text,
tooltip carries no survey list)`.

### Verification Contract

- `dotnet build EcoServerMod/AdvancedElectronics -c Release` — 0 errors.
- `dotnet test EcoServerMod/AdvancedElectronics.Navigation.Tests` — green, including the updated
  `SurveyRecordTests` quantity assertions.
- Deploy `AdvancedElectronics.dll` to `<eco-server>\Eco_Data\Server\Mods\UserCode`.
- Live (user authority): survey an area with rock + ore + sand/clay/peat → the dock panel lists each
  specific material with quantity + shallowest + depth range, sorted by quantity; dirt/grass/gravel
  and crushed variants absent; `/drone survey` matches; no floating text over the dock; tooltip has no
  survey list.

### Definition of Done

R1–R6 satisfied; build + Navigation tests green; deployed; the dock panel and chat show the
quantity-led material readout for the six categories; world-text and tooltip retired.

### Assumptions

- `block.Is<Diggable>()` is usable the same way `block.Is<Minable>()` is, and Sand/Clay/Peat blocks
  strip to those exact type names. Verify live (ASSUMPTION convention).
- The `SurveyRecord`/`OreFindingSnapshot` (KTD11) generalize cleanly from an ore key to a material
  key; naming that still says "ore" generalizes to "material" as a rename.
- Removing `BuildStateLines`/tooltip leaves no other consumer — confirm during U4 before deleting.

### Open questions

- Readout sort is quantity-descending; revisit if rarity-first reads better in play.
- Depth shown as a range (min-max); confirm it reads well, else fall back to shallowest only.
