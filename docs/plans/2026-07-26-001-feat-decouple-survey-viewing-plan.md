---
artifact_contract: ce-unified-plan/v1
artifact_readiness: requirements-only
product_contract_source: ce-brainstorm
date: 2026-07-26
type: feat
topic: decouple-survey-viewing
---

# Decouple Survey Viewing from Drone Assignment - Plan

## Goal Capsule

**Objective.** Make the Drone Dock's Survey tab show and manage **every** survey area the dock
owns — each with its own coverage and findings — independently of which area (if any) the drone
is currently assigned to. Today the readout only reflects the assigned area, so a player must
assign the drone to an area just to see its data.

**Product authority.** Solo maintainer, validated by live play (per project convention: UX
requirements lead, implementation bends).

**Active scope.** Only the read/manage surface on the dock's Survey tab (plus the dock tooltip
overview). The material selector, sampling/coverage changes, UI-widget polish, and multi-drone
support are separate ideation items and are **not** active scope here — see
`docs/ideation/2026-07-26-survey-system-improvements.md`.

**Open blockers.** None. The data this feature surfaces already persists per area (KTD11), so this
is a read-surface repoint, not a data-model change.

---

## Product Contract

### Summary

The Survey tab presents a **compact list of all areas** (a line each) plus **full findings detail
for the currently selected area**. Selection is driven by the existing Prev/Next cycle and is
independent of drone assignment. All area actions operate on the **selected** area. Viewing and
managing areas requires no drone to be assigned, and works even with no drone docked.

### Requirements

**R1 — Areas list shows all areas, decoupled from assignment.**
The tab's areas section renders one compact line per area: name, plot count, coverage %, and its
single strongest finding (highest-concentration ore/material, or "not surveyed yet"). Each line
carries markers for `[assigned]` (the drone's current target, if any) and `> selected` (the cycle
selection). The list renders whether or not a drone is assigned or docked.

**R2 — Results detail follows selection, not assignment.**
The results section shows the full persisted findings + coverage for the **selected** area. When no
area is selected (e.g. no areas exist), it shows an empty-state message. It never requires the
selected area to be the assigned area.

**R3 — Actions target the selected area.**
Assign, Unassign, Edit, View, and Delete operate on the selected area. Create makes a new area and
selects it. (This matches the current button set; the change is that Results/detail now follow the
same selection the actions already use.)

**R4 — No-drone and no-assignment states are first-class.**
With no drone docked, or a drone docked but unassigned, the player can still browse every area's
coverage and findings and perform all non-dispatch actions (create/edit/view/delete/select). Only
Assign/Unassign have any dependence on a drone existing.

**R5 — Dock tooltip becomes an all-areas overview.**
The dock's info-window tooltip shows a compact summary of every area (name, coverage %, top find),
rather than only the assigned area's findings — a decoupled at-a-glance overview mirroring R1.

**R6 — World-space drone text stays assignment/status-oriented.**
The world-space text above the dock continues to reflect the drone's live status and the area it is
actively working (the assigned area). It is about what the drone is *doing*, not a management
surface, and is intentionally left coupled to assignment.

### Primary flow

1. Player opens the Drone Dock's Survey tab.
2. The areas list shows every area with coverage % and top find; markers show which is assigned
   (if any) and which is selected.
3. Player uses Prev/Next to select any area — no drone assignment required.
4. The results section updates to the selected area's full findings + coverage.
5. Player acts on the selected area (view on map, edit, delete, or assign the drone to it).
6. Data for previously-surveyed areas remains visible regardless of the drone's current target.

### Acceptance examples

- **View without assignment:** Dock has areas A and B, both previously surveyed, drone currently
  unassigned. Opening the tab shows both areas' coverage and top finds; selecting A shows A's full
  findings; selecting B shows B's. No assignment was needed.
- **No drone docked:** Dock has area A with persisted findings, no drone item inserted. The tab
  still lists A with its coverage/findings and allows select/edit/view/delete; Assign reports there
  is no drone to dispatch.
- **Assigned ≠ selected:** Drone assigned to A (surveying). Player selects B. Results detail shows
  B; the list marks A `[assigned]` and B `> selected`; the drone keeps surveying A undisturbed.
- **Empty/edge:** No areas → list shows an empty-state prompt to Create; results section shows the
  empty-state message. Deleting the selected area clears selection to none (or the next area).

### Key decisions

- **Compact list + selected detail** (not full-detail-for-all, not select-one-only): every area's
  coverage/top-find is visible at a glance; full detail is shown for the selected area. Chosen to
  fit the text-only tab (no per-row widgets) while still decoupling viewing from assignment.
- **Results follow selection, not assignment** — the core repoint of this feature.
- **Selection stays cycle-based (Prev/Next).** A dropdown/rich picker is not attempted here; the
  ModKit does not expose native list/Selector widgets for dock-local data
  (`docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md`). A richer picker
  is deferred to the UI-polish ideation items (#4/#6).
- **Tooltip decouples (all-areas overview); world text stays coupled (drone status).** Two
  surfaces, two jobs: the tooltip is a management overview, the world text is live drone status.

### Out of scope

- Material target selector by tag/type (ideation #2/#3).
- Sampling/coverage changes such as full-plot park-and-sweep (ideation #1).
- Native UI widgets / custom client prefab panel (ideation #4/#6).
- Multiple concurrent drones or areas surveyed at once.

### Assumptions

- The per-area `OreFindingSnapshot` + coverage already persisted on `SurveyAreaEntry` (KTD11) is
  sufficient to render both the compact list and the selected detail with no new data captured.
- "Top find" for the compact line is the selected area's highest-concentration finding; ordering of
  the list follows the dock's existing area order (creation/id order).
- The tooltip's all-areas summary stays within a reasonable length for the areas a single dock
  realistically owns; no pagination is required for v1.

### Outstanding questions

- Should the compact list cap the number of areas shown (or the tooltip's length) if a dock
  accumulates many areas? Deferred until live testing shows whether it's a real problem.
- Should "top find" prefer shallowest vs highest-concentration when they disagree? Default is
  highest-concentration; revisit if players find depth the more actionable sort.
