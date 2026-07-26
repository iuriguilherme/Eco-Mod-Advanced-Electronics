# Ideation — Survey system improvements

**Date:** 2026-07-26
**Focus:** Improve the survey system. Four stated pains (verbatim intent):
1. The drone samples only the few columns its roam path crosses, not every column in each assigned plot — regardless of one plot, many plots, or non-contiguous plots.
2. Need to be able to **select what the drone surveys** — clarified: pick target **materials by tag** (`Ore`, `Rock`) or **specific type** (Gold Ore, Limestone, Clay, Sand, Peat, Sulfur), recipe/store-style. Expands survey scope beyond ore to block materials generally.
3. Survey data only appears while a drone is assigned — need to **decouple assignment from viewing data and managing areas**.
4. The UI is weak and doesn't match the rest of Eco — needs polish.

**Mode:** repo-grounded, run inline by the orchestrator (deep, fresh context on the system just built; no cold agent fleet). Web research skipped (internal game design).

---

## Grounding context

What exists today (post-KTD11):

- **Areas** are dock-owned, serialized, drawn on Eco's native map editor (`SurveyAreaEntry`, `SurveyAreaPicker`). Plots are 8×8 world columns.
- **Sampling** is roam-driven: `OreSensorComponent.Tick` samples **one column per tick** from a 5-column footprint (`SampleOffsets`) wherever the drone currently roams (`DroneLifecycle.TickSurveyRoam`). Coverage is incidental to the roam path — most of a plot's 64 columns are never sampled.
- **Findings** now persist **per area** as a serialized `OreFindingSnapshot` list on `SurveyAreaEntry`, folded from the dock-owned in-memory `SurveyRecord` (KTD11). **This is the key unlocked asset:** the data already lives on the area, independent of the drone.
- **Readout** (tab / tooltip / world text / chat) reads the **assigned** area's snapshot only — the decoupling in pain #3 is a *presentation* choice, not a data-model limitation, because the data is already per-area.
- **UI ceiling:** rich client panels (lists, Selector dropdowns) are **not** exposed by the ModKit; a mod tab is limited to text + buttons + editable scalars, or a custom bundle-prefab MonoBehaviour driven by `SetAnimatedState`. The map editor is native and reachable. (See `docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md`.)
- **Known risk in flight:** non-contiguous-area roam/pathfinding is untested.

**Topic axes:** (A) survey coverage & sampling; (B) selection controls; (C) data access & decoupling; (D) UI / native match. A cross-cutting fifth theme — tier the thoroughness/depth/size to drone tiers — folds into A and B and the Progression track.

**Strategy fit:** survivors are scored against `STRATEGY.md` — *autonomy over gadgetry*, *the visible reward*, *progression depth*.

---

## Survivors (ranked)

Ranked by leverage ÷ cost, weighted by how directly each kills a stated pain and fits the strategy.

### 1. Area-centric tab: decouple viewing/managing from assignment  ·  pain #3  ·  leverage HIGH / cost LOW

Make the Survey tab list **every area with its own findings + coverage**, selectable independently of what the drone is doing. Assignment becomes one action in the row (Assign / Unassign / Edit / View / Delete), not a precondition for seeing data. The persisted per-area snapshot already exists — this is almost entirely a *read-surface* change: point `BuildResultsText` (and the tooltip) at the **selected** area rather than the **assigned** one.

- **Why it wins:** highest leverage-to-cost in the set — it cashes in the KTD11 persistence work already shipped. Directly serves "the visible reward."
- **Risk:** the text-tab selection UX (Prev/Next cycle) is clunky; pairs naturally with #4.
- **Brainstorm-ready:** yes. Small, well-bounded, high payoff.

### 2. Deterministic per-plot coverage (park-and-sweep)  ·  pain #1  ·  leverage HIGH / cost MED

Replace incidental roam-sampling with **systematic coverage**: the drone visits each assigned plot and sweeps **every column** (or a fixed dense grid) before moving on, so coverage % becomes a real "columns sampled ÷ columns in area" number. Two shapes to decide in brainstorm:
- **Park-and-sweep (preferred):** drone parks at a plot anchor and the sensor reads all columns in range, then hops to the next plot. Fewer A\* searches; **sidesteps the untested non-contiguous roam risk** because plots are visited discretely, not pathed between continuously.
- **Raster walk:** drone physically walks a lawnmower pattern over each plot.

- **Why it wins:** kills the core "only samples one spot" complaint AND de-risks non-contiguous areas in one move. Serves "autonomy" — the machine actually covers what you drew.
- **Risk:** per-plot full sweep is more sensor work; needs a per-tick budget so it doesn't spike the server (the existing one-column-per-tick throttle is the lever).
- **Brainstorm-ready:** yes — but decide park-and-sweep vs raster first.

### 3. Material target selector (tags + specific types, recipe/store-style)  ·  pain #2  ·  leverage HIGH / cost MED-HIGH

**Clarified intent:** the player selects **what materials to survey** by **tag** (e.g. `Ore`, `Rock`) or **specific type** (Gold Ore, Limestone, Clay, Sand, Peat, Sulfur) — exactly the way ingredients and tags are picked in a recipe or a store filter. One or more, mixing tags and specifics.

Two parts:
- **Broaden the sensor beyond ore.** Survey scope is now *materials*, not just `Minable` ore. `EcoOreReader` generalizes to a material reader keyed by **item type + tags** so rock/sand/clay/peat/sulfur are detectable and reportable.
- **A tag+type multi-select surface.** Mirror Eco's recipe/store item-and-tag picker.

- **Why it wins:** this is the mod's identity move — "survey for what you actually need." Turns a generic prospector into a targeted one, and the target set is a natural higher-tier reward axis (more materials, more tags).
- **Feasibility unlock (important):** unlike area selection (dock-local, no registry — see rejected list), **materials are items with tags in Eco's global registry**, which is exactly the viewable source a native `Selector` / tag-picker consumes. So a **native recipe/store-style picker may be feasible here** where it wasn't for areas. Needs a small feasibility spike; if it lands, it also lifts pain #4 and re-opens idea #6.
- **Risk:** the native-picker feasibility is unproven for a mod tab; fallback is a tag/type entry via buttons or an editable field. Broadening the sensor is straightforward but changes what "ore concentration" means for non-ore materials (rock is common — concentration semantics need a rethink).
- **Brainstorm-ready:** yes — lead with the feasibility spike on the native item/tag selector, then the sensor generalization.

_(Depth band — surface/shallow/deep tied to sensor reach — survives as a **separate** progression idea, not part of pain #2. Cheap, text-tab-friendly, natural tier axis. Carry it into a Progression brainstorm, not this one.)_

### 4. Pragmatic UI polish within native constraints  ·  pain #4  ·  leverage MED / cost LOW-MED

Make the existing surfaces feel intentional and Eco-native **without** the heavy custom-prefab path:
- Lean on the **map editor** as the primary area UX (it's already native-feeling) — view/edit areas there, not in a cramped text list.
- Ship a **chat-emoji/icon set** for ores + drone status so text readouts carry visual weight (mods can ship emoji sets).
- Restructure the **NewTooltip** readout (native tooltip system) with clear sections/spacing.

- **Why it wins:** buys most of the "matches the game" feel at a fraction of custom-UI cost; complements #1's selection UX.
- **Risk:** still text-shaped; won't fully match a hand-built native panel (that's #6).
- **Brainstorm-ready:** yes, as a polish bundle.

### 5. Per-plot enable/disable within an area  ·  pain #2 (alt reading)  ·  leverage MED / cost LOW-MED

Let the player toggle individual plots on/off inside a drawn area without redrawing it — "survey these plots, skip those." This is the *region-selection* reading of "select what to survey."

- **Why it wins:** cheap, and pairs with #2 to cover both interpretations of pain #2.
- **Risk:** overlaps with just editing the area; value depends on whether players want a persistent skip-mask vs a redraw.
- **Brainstorm-ready:** fold into #3's scoping decision.

### 6. Custom client-prefab survey panel  ·  pain #4 (ambitious)  ·  leverage HIGH / cost HIGH

The "real" fix for #4: a bundle-shipped Unity MonoBehaviour on the dock prefab, driven by `SetAnimatedState`, rendering a genuinely native-looking list/panel of areas + findings. This is the only path to a true My-Deeds-style UI (the ModKit blocks the client panel classes).

- **Why it's here:** it's the honest answer to "match the rest of the game," and it would also host #1's area list beautifully.
- **Cost/risk:** substantial Unity client work, one-way data channel, version-fragile — a project, not a polish pass. Sequence it *after* #1–#4 prove the data/UX shape in text first.
- **Brainstorm-ready:** premature until the text-tab version validates the interaction model.

---

## Rejected (with reasons)

- **Whole-plot footprint every tick** (sample all 64 columns per tick) — sensor cost spikes; #2 (park-and-sweep) reaches full coverage under the existing per-tick throttle instead.
- **Coverage-gradient roam** (bias roam toward unsampled columns) — keeps the fragile roam/pathfinding as the coverage mechanism and does nothing for the non-contiguous risk; park-and-sweep is simpler and de-risks both.
- **Multiple concurrent drones / areas** — large lifecycle+spawn scope jump, not asked for; defer.
- **Standalone survey-report export** — subsumed by survivor #1 (the area list already shows every area's data).
- **Native Selector for _area_ selection** — infeasible: Selector options come from a global registry of a viewable type; dock-local area data has no such registry (documented in the render-surfaces convention). Note: this rejection is scoped to **areas** — a native selector for **materials** (items/tags, which DO have a global registry) is a different, promising case, now carried in survivor #3.
- **Target-ore priority roaming** — couples to the roam mechanism we're trying to de-risk; marginal over #3's filter.

---

## Recommended next step

Brainstorm **survivor #1 (area-centric decoupled tab)** first — highest leverage, lowest cost, cashes in shipped work, and it's the surface that #2/#3/#4 all attach to. Then #2 (park-and-sweep coverage) as the autonomy-defining follow-up.

`ce-brainstorm` on #1 to turn it into a requirements-only unified plan.
