---
artifact_contract: ce-unified-plan/v1
artifact_readiness: requirements-only
product_contract_source: ce-brainstorm
date: 2026-07-26
type: feat
topic: broaden-survey-materials
---

# Broaden Survey Detection to Material Types - Plan

## Goal Capsule

**Objective.** Broaden the survey drone from ore-only prospecting to a curated set of block
materials, and shift the readout from an ore-centric concentration model to a quantity-led one so
the reported numbers are meaningful for common bulk materials as well as rare ore.

**Product authority.** Solo maintainer, validated by live play.

**Active scope.** The detection + reporting side only — *what* the drone finds and *how* it is
reported. The material tag/type **selector** (letting the player choose and limit which materials
to survey for) is a separate, later brainstorm and is **not** active scope here. This work is its
prerequisite: it establishes broadened detection; the selector filters it.

**Open blockers.** None. Park-and-sweep already sweeps every column of each plot, so real per-block
counts exist; this repoints classification and readout, not the sampling engine.

---

## Product Contract

### Summary

The drone detects and reports blocks in six material categories — **Rock, Ore, Sulfur, Peat, Clay,
Sand** — at **specific-type** granularity (Limestone, Granite, IronOre, CopperOre, ...). For each
material type found in the surveyed area, the readout leads with **quantity** (block count) plus the
**shallowest location** and the **depth range**. No cap on how many types are reported (capping is
the selector's job, deferred). Blocks outside the six categories (dirt, grass, gravel) are ignored.

### Requirements

**R1 — Detect the six material categories, by specific type.**
The sensor classifies each scanned block as belonging to one of {Rock, Ore, Sulfur, Peat, Clay,
Sand} or not, and attributes it under its **specific type name** (e.g. `Limestone`, `Granite`,
`IronOre`, `CopperOre`, and `Sulfur`/`Peat`/`Clay`/`Sand` for the already-specific ones). Blocks in
no category are not recorded. (Today the sensor gates on `Minable` and reports ore type names only.)

**R2 — Quantity-led readout per material type.**
For each detected material type in the area, the readout shows: **quantity** (total block count
found) as the headline, the **shallowest occurrence** as a location (x, y, z), and the **depth
range** (shallowest–deepest blocks below surface). Example: `Limestone: ~210 blocks, shallowest at
(412, 63, -88), depth 2-14`.

**R3 — Concentration is no longer the headline.**
The ore-centric concentration ratio (ore-blocks / sampled-blocks) is dropped as the lead metric. It
may be retained as an ore-only secondary detail if it adds value, but quantity is the primary
signal for every material.

**R4 — No cap on reported types (in the unlimited surfaces).**
Every detected material type is reported. The dock **tab**, **tooltip**, and **`/drone survey`
chat** show the full list. Limiting *which* materials are surveyed/shown is the selector's job and
is deferred. See R6 for the one surface that is inherently capped.

**R5 — Survey targets raw natural block forms only; crushed variants excluded.**
Rock, Ore, and Sulfur each have a **crushed** variant, but those are processed *items*, not natural
terrain blocks — the drone scans terrain, so it only encounters and reports the raw block forms.
The classifier must not map a crushed form to a survey material. (Forward note for the selector: it
must not offer crushed forms as survey targets.)

**R6 — The world-space readout stays within its fixed slot budget.**
The world-space text above the dock renders through a **fixed** set of animated-state slots
(`MaxOreLines = 6`), so it cannot grow with an unbounded type list. It shows the top materials
(sorted, e.g. by quantity) up to that budget; the full list lives in the tab/tooltip/chat (R4).
This is a rendering constraint, not a product cap.

### Primary flow

1. The drone park-and-sweeps the assigned area, scanning every column top-down.
2. Each scanned block is classified: if it belongs to one of the six categories, it is recorded
   under its specific type name (raw forms only), accumulating count, shallowest location, and depth
   range for that type in that area.
3. The readout lists every detected material type with quantity + shallowest location + depth range
   (tab/tooltip/chat full; world text top-N).

### Acceptance examples

- **Mixed column:** a plot of limestone over an iron seam reports `Limestone` (high count, shallow)
  and `IronOre` (lower count, deeper) as distinct types — not a single "Rock"/"Ore" bucket.
- **Non-target ignored:** dirt and grass the drill passes through are not reported as materials.
- **Bulk material is meaningful:** `Sandstone: ~340 blocks, depth 1-9` reads as a real deposit,
  where the old concentration model would have shown a near-meaningless high %.
- **Crushed excluded:** the survey never reports "Crushed Sulfur" or "Crushed Limestone" — only the
  raw `Sulfur` / `Limestone` terrain blocks.
- **Many types + world text:** an area with 9 material types shows all 9 in the tab; the world-space
  text shows the top 6 by quantity.

### Key decisions

- **Specific-type granularity** (not broad Rock/Ore buckets) — players care which ore and which rock.
- **Quantity is the headline metric** for all materials; concentration retired as the lead.
- **Curated six-category allowlist** {Rock, Ore, Sulfur, Peat, Clay, Sand} — not "all solid blocks";
  dirt/grass/gravel are noise.
- **Raw forms only; crushed variants excluded** (they are items, not terrain).
- **No survey-count cap now** — capping/limiting which materials is deferred to the selector.

### Out of scope

- The material **selector** (choose/limit which materials to survey for, tag + specific-type picker,
  native-picker feasibility) — the next brainstorm.
- Dirt, grass, gravel, and other non-listed blocks.
- Specific-type breakdown *cap* / pagination in the unlimited surfaces.

### Assumptions

- Eco classifies these materials via block tags and/or the block-type hierarchy (e.g. an `Ore`/`Rock`
  tag or a common base type); the exact classification mechanism is an implementation detail for
  planning, verified against the reference assemblies / game source.
- The per-area `SurveyRecord` and its serialized `OreFindingSnapshot` (KTD11) generalize from an
  ore-type key to a material-type key; the aggregation shifts from best-plot-by-concentration to
  area-total count + shallowest occurrence + depth range. Naming that still says "ore" (`EcoOreReader`,
  `SurveyRecord` keys, `FormatOreLine`) is expected to generalize to "material" — a rename, not a
  behavior change.
- "Shallowest occurrence" is an adequate single dig-start location for a material spread across
  plots; a per-plot hotspot view is not required for this scope.

### Outstanding questions

- Sort order for the readout (and thus which types survive the world-text top-6): by quantity, by
  rarity, or a fixed category order? Default assumed quantity-descending; revisit in planning or live
  testing.
- Should depth be reported as a range (min-max) or just the shallowest? Range assumed; confirm it
  reads well in-game.
