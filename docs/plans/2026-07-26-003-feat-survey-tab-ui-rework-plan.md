---
artifact_contract: ce-unified-plan/v1
artifact_readiness: requirements-only
product_contract_source: ce-brainstorm
date: 2026-07-26
type: feat
topic: survey-tab-ui-rework
---

# Survey Tab UI Rework - Plan

## Goal Capsule

**Objective.** Make the Drone Dock's survey interface usable: eliminate the vertical button stack,
replace cycle-based selection with direct per-area selection, and move area management onto Eco's
native map editor so the interface reads as first-party rather than as a column of buttons.

**Product authority.** Solo maintainer, validated by live play.

**Active scope.** The dock's player-facing survey interface only — tab structure, controls, and how
text is grouped. What the drone detects, how it surveys, and where findings are stored are settled
and unchanged.

**Open blockers.** None. Three feasibility bets are named below; each has a defined fallback, so
none blocks starting.

---

## Product Contract

### Summary

The single Survey tab becomes two: **Areas** (manage and assign) and **Results** (read). Area
creation, renaming, redrawing and deletion move into Eco's native map editor — the same mechanism
districts use — reached by one button. Assignment becomes one button per existing area, so the
player picks an area directly instead of cycling to it. The Results tab carries the material picker
and the findings text and no buttons at all.

### Requirements

**R1 — Split the Survey tab into Areas and Results.**
Two tabs on the dock. **Areas** holds the area list, the assign controls, and the map-manager
button. **Results** holds the material-target picker and the findings readout. Neither tab requires
scrolling at a typical area count.

**R2 — One assign button per existing area.**
The Areas tab shows a button per area the dock owns (`Assign Area 1`, `Assign Area 2`, …). Buttons
for positions with no corresponding area are hidden. Clicking an area's button assigns the drone to
it; clicking the button of the **already-assigned** area unassigns, so no separate Unassign button
exists. Button labels are static (position numbers, not area names) — the numbered list supplies the
names.

**R3 — A single line states the current assignment.**
The Areas tab shows the assigned area as one line, e.g. `Assigned: 2 — North Ridge`, or a clear
unassigned state. This is the authoritative answer to "what is the drone working on".

**R4 — Area management happens on the map.**
One `Manage Areas on Map` button opens Eco's map editor showing **all** of the dock's areas as named
entries at once. Creating, renaming, redrawing and deleting areas all happen there and are applied
on confirm. The tab carries no Create/Edit/View/Delete buttons.

**R5 — The Results tab carries no buttons.**
It renders the material-target picker (a compact collapsible row) and the findings text — per-material
quantity, shallowest location, depth range, coverage, scanned depth and median surface — plus the
drone's status line as a footer.

**R6 — Vertical button stacking is the thing being removed.**
Success is measured by controls that fit without scrolling, not merely fewer buttons. Prefer compact
single-line controls (picker rows, text lines) over buttons wherever a choice exists. A layout that
reintroduces a column of full-width buttons has failed this requirement even if it is otherwise
correct.

### Primary flow

1. Player opens the dock → **Areas** tab: numbered list of areas, one assign button each, and a line
   naming the currently assigned area.
2. To change what the drone works on: click that area's button. To stop it: click the assigned area's
   button again.
3. To create or change areas: `Manage Areas on Map` → the map editor opens with every area drawn and
   named → add, rename, redraw or delete → confirm.
4. To read findings: **Results** tab → optionally narrow with the material picker → read the per-area
   findings.

### Acceptance examples

- **Direct assignment:** dock owns 3 areas → exactly 3 assign buttons show; clicking the second
  assigns the drone to area 2 and the line reads `Assigned: 2 — <name>`.
- **Toggle off:** clicking the assigned area's own button unassigns; the line reads unassigned and
  the drone returns to dock.
- **No stack:** with 3 areas, the Areas tab shows the list, 3 assign buttons and the map button
  without scrolling.
- **Map management:** `Manage Areas on Map` opens with all 3 areas visible and named; adding a fourth
  and deleting the first is reflected in the tab afterwards, with the fourth gaining its own button.
- **Results is button-free:** the Results tab shows the picker and findings only.
- **Beyond the pool:** creating more areas than the button pool supports leaves the extras listed and
  readable but not assignable from the tab (see Assumptions).

### Key decisions

- **One button per area, not a cycle** (session-settled: user-directed — replaces Prev/Next, chosen
  over an editable number field and over map-based assignment). Scroll length from many areas is
  explicitly the player's own tradeoff.
- **Click-the-assigned-button to unassign** — removes a control rather than adding one.
- **Map editor as the area manager**, mirroring districts, which removes four buttons and gives
  direct visual selection.
- **Two tabs**, splitting manage from read.
- **Static button labels** — RPC buttons are declared at compile time, so labels cannot carry area
  names; the numbered list maps position to name.
- **Vertical button stacks are the defect being fixed** (session-settled: user-directed — "this is
  not a smartphone app"), not merely an inefficiency to reduce.

### Out of scope

- Custom client UI panels (not exposed by the ModKit).
- Any change to detection, sampling, findings storage, or the material-filter semantics.
- Humanising material names, and the map-overlay layer (still engine-blocked).

### Assumptions

- **Button pool cap.** The per-area buttons come from a fixed compile-time pool; 10 is assumed
  sufficient. Areas beyond it remain visible and readable but are assignable only via
  `/drone assignarea`. Revisit if live use shows players routinely exceeding it.
- The map editor's multi-entry mode reports created, renamed and deleted entries well enough to
  reconcile against the dock's stored areas; districts rely on this, so it is expected to hold.
- Hiding unused buttons is achievable by binding member visibility to a bool property; if not, the
  pool renders at fixed size and out-of-range buttons no-op with the list stating so.

### Outstanding questions

- Should the Results tab show only the assigned area's findings, all areas stacked, or a picked area?
  Current behaviour shows a selected area; with selection controls gone from Results, "assigned area"
  is the likely default — confirm in play.
- Does the map editor round-trip area **names**, or must renaming stay a tab concern? If names do not
  survive, R4 narrows to create/redraw/delete and renaming needs a home.

### Feasibility bets

Each is testable in one restart and each degrades gracefully:

1. **A second mod component registers its own tab.** An earlier attempt in this project did not
   register a second tab; two "impossible" conclusions were overturned during this session, so it is
   worth re-testing. *Fallback:* keep one tab, ordered management-then-results.
2. **Multi-entry map editing round-trips.** *Fallback:* keep per-area edit via the existing
   single-entry picker, driven from the area buttons.
3. **Member visibility can be bound to a bool.** *Fallback:* fixed visible pool with no-op buttons
   beyond the area count.
