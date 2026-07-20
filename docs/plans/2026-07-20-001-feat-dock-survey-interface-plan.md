---
title: Dock Survey Interface (Map Area Picker + Survey Tab + Standardized Record) - Plan
type: feat
date: 2026-07-20
topic: dock-survey-interface
artifact_contract: ce-unified-plan/v1
artifact_readiness: requirements-only
product_contract_source: ce-brainstorm
execution: code
---

# Dock Survey Interface (Map Area Picker + Survey Tab + Standardized Record) - Plan

## Goal Capsule

- **Objective:** Turn the survey drone from a proof of concept into something a player can
  actually use: draw a survey area on the mod's own map layer from the dock (same drawing
  mechanics as districts, separate independent system), and read the results in a dock tab,
  backed by a standardized record a future mining drone can consume.
- **Product authority:** This brainstorm. It supersedes two earlier decisions — that
  district assignment was an acceptable interaction (it was a workaround, never requested),
  and that a tooltip or chat command was an acceptable readout (they are debugging surfaces).
- **Open blockers:** one, and it gates R1, R2 and R7 together. The server-side contracts for a
  mod-owned map layer and a mod-defined component tab are all public and verified present, but
  whether the **stock client renders mod-defined** layers and tab content is untested — client
  views are normally emitted by build-time codegen a server-only mod does not run. A single
  spike covering both rides the next deploy batch (see Dependencies). Do not plan
  implementation detail for R1/R2/R7 ahead of its result.

## Product Contract

### Summary

The mod gains its **own map layer** — a survey-plot overlay that reuses the engine's map
drawing machinery but is an independent system with its own purpose, exactly as districts and
property deeds are independent systems sharing the same map mechanics. Survey areas are named,
persistent entities the player creates and curates: the dock gains a **Survey Areas** tab to
create, list, assign and delete them — drawn on the map exactly as a district is — and a
separate **Survey Results** tab. Access is delegated to the dock's `PropertyAuthComponent`, so
anyone the owner authorizes as a consumer can direct the drone without being able to take the
machine. The drone surveys
the selected area and reports findings to the dock, which owns the record. Findings are
stored at finer-than-plot precision — ore type, location, depth — in a standardized form
designed from the start for two readers: the tab renders it for the player, and a future
mining drone consumes the same record to choose and travel to a dig site. Chat commands
become diagnostics only.

### Problem Frame

The drone works but delivers nothing usable. Results reach the player only through a chat
command, which is a debugging tool rather than an interface, and the advertised tooltip does
not render — so in practice the data is nowhere. Separately, assigning work requires the
player to draw a *district* first.

**Districts were a deliberate and correct development scaffold, not a mistake.** Borrowing an
existing drawn-region system let pathfinding, roaming, sampling and depth all be tested
without first building a map layer — real progress on the hard parts, unblocked. The error
would be shipping the scaffold as the interface. The district system exists for civics and
governance; making a player create one per survey couples the mod to an unrelated system and
is unreasonable as a repeat interaction. That is what this work replaces, and the scaffold
stays in place until the replacement is proven.

The fix is not to avoid the map machinery — it is to stop borrowing *someone else's layer*.
The engine's map system is generic (`IMapEntryOverlay`, `IPlotOverlayWithMapLegend`,
`IMinimapCategorizedOption`); districts are one tenant of it, property deeds another,
influence and world layers others still. The mod should be its own tenant: same drawing
mechanics, own layer, own purpose, no civics entanglement.

### Key Decisions

- **The mod owns a survey-plot map layer of its own, built on the engine's map system.** Not a
  reuse of districts and not an avoidance of the map machinery — a parallel implementor of the
  same generic overlay contracts, in the same relationship to districts that property deeds
  are. Survey plots are drawn with the identical mechanics, live on their own toggleable
  layer under the mod's own category, and are invisible to and unaffected by civics. This
  replaces district assignment rather than supplementing it.
- **A survey area is a first-class named entity, not "whatever the dock currently points at".**
  The player creates survey areas, they persist and accumulate in a list, and a dock is
  *assigned* one of them. This mirrors deeds and districts: named, coloured, drawn entries in a
  registrar, all rendered on one layer. Creating an area and assigning an area are decoupled
  actions — the same area can be reassigned to a different dock later, and an area outlives the
  dock that first used it.
- **The dock exposes two tabs, not one.** A *Survey Areas* tab — a list of existing areas plus
  a "create new area" action, modelled on `RealEstateDesk`'s deed list — and a *Survey Results*
  tab showing findings. Separating them means the results tab is never cluttered by
  administration, and an unassigned dock still has an obvious next action.
- **Access follows the engine's property system rather than reinventing one.** The dock carries
  `PropertyAuthComponent`. Anyone with `ConsumerAccess` on the dock may open its tabs, create
  survey areas, and assign them; only owners may pick the dock up; unauthorized players cannot
  interact at all. This deliberately diverges from the engine's own `Deed.EditInMap`, which is
  `OwnerAccess` — here a trusted crew member should be able to redirect a drone without being
  able to steal the machine.
- **Results live in a dock tab.** The dock window is where an object's information belongs,
  alongside its existing tabs. The tooltip is abandoned as a delivery surface — not merely
  because it did not work, but because it is not the right place for data the player
  studies and acts on.
- **The dock owns the survey record; the drone is a sensor that reports to it.** Accumulating
  on the drone loses everything when the drone is removed, and makes dock-to-dock transfer
  incoherent.
- **The record is session-scoped by design, not by omission.** The world already stores block
  data; persisting a survey would duplicate it. Survey data is inherently transient: a player
  holds it in memory long enough to go mine, a mining drone consumes it immediately and
  routes, and once a location is mined its survey entry is stale. Remembering what has
  already been mined across sessions is a different, richer capability belonging to a more
  expensive automator later.
- **Standardized for both readers now.** The stored shape is designed for machine consumption
  from the start so a mining drone reads it without a migration, even though no mining drone
  exists yet.
- **Precision finer than a plot.** Selection happens in plots, but findings are addressed
  more precisely so a dig site is pinpointed rather than approximated. The tab rolls findings
  up into something readable while preserving the precise location for the machine reader.

  **Cost note for planning:** Eco's plot is `PlotUtil.PropertyPlotLength = Chunk.Size / 2` = 8
  world units, and the existing `SurveyCellSize` is also 8 — so today's grid cell *is* exactly
  one plot. This requirement therefore replaces the current aggregate density model with
  per-finding records rather than tuning a constant, and `SurveyGrid`, `DensestCell`,
  `DockReadout` and their tests are rewritten. Budget it as such.

### Actors

- A1. **Drone owner** — places the dock, authorizes others on it, creates survey areas, returns
  later, reads results, decides where to mine. The only actor who can pick the dock up.
- A1a. **Authorized crew member** (`ConsumerAccess` on the dock) — can create, assign and delete
  survey areas and read both tabs, but cannot take the dock. The reason access is delegated to
  `PropertyAuthComponent` rather than restricted to the owner.
- A2. **Survey drone** — surveys the assigned area and reports findings to its dock.
- A3. **Drone dock** — owns the area assignment, owns the survey record, presents the tab.
- A4. **Mining drone (future)** — not built here; constrains the record's shape as its
  eventual reader.

### Requirements

- R1. From the dock's Survey Areas tab the player can **create a new survey area**, drawing its
  plots on the map with the same mechanics as editing a district or a deed. The picker carries
  mod-authored title and hint text naming it as a survey area, so that R2's "no district was
  created" is legible to the player rather than merely true underneath.
- R1a. A survey area is **named and persists**. The Survey Areas tab lists existing areas, and
  the player can assign one to the dock's drone, rename it, or delete it. Creating and
  assigning are separate actions: an area may be assigned to a different dock later, and
  deleting an area that a dock is using unassigns that dock rather than silently breaking it.
- R1b. A survey area is capped at a maximum plot count — on the order of a homestead, a few
  dozen plots, not a valley — enforced in the picker and re-validated server-side on the
  returned overlay. The cap is a property of the drone tier, so a better drone surveys a larger
  area: the same progression axis as scan depth.
- R1c. Creating, assigning, renaming and deleting survey areas, and reading either tab, require
  `ConsumerAccess` on the dock. Picking the dock up requires owner access, per the engine's
  normal object behavior. Players with no authorization cannot interact with the dock at all.
  All of this is delegated to `PropertyAuthComponent` rather than implemented in the mod.
- R2. Survey areas live on the mod's **own map layer**, not on the district layer. Drawing one
  creates no district, requires no civics object, and is invisible to the civics system; the
  layer appears under the mod's own category in the map's layer list and toggles like any other
  overlay.
- R2a. The survey layer shows a player only **their own** survey areas — those they created or
  are authorized to use — not every survey area on the server. Keeps the map uncluttered on a
  populated server.
- R3. The dock retains its assigned survey area as the drone's standing assignment, and the
  player can change it by assigning a different area from the list. Both the survey areas
  themselves and the dock's assignment persist across a server restart (unlike the findings,
  per R8): after a restart the drone resumes surveying the same area and the results tab shows
  a fresh empty record for it, so the player never returns to a drone that sits idle looking
  broken.
- R3a. Findings are recorded against the survey area that produced them. Reassigning a dock to
  a different area never destroys findings from the previous one; the results tab groups them
  by area and a machine reader can filter to the currently assigned one. This keeps R6's
  consumer from routing to a location outside the area the player currently cares about.
- R4. The drone surveys within the dock's assigned area and reports its findings to the dock.
- R5. The dock stores survey findings at finer-than-plot precision, recording at minimum the
  ore type, the location, the depth below the surface, and how concentrated the ore is — the
  last so that both readers can rank a rich seam above a stray block rather than merely
  locating it.
- R6. The stored record is standardized and machine-readable, such that a future mining drone
  can select a target and route to it without a change to the format. **This is an intent
  statement, deliberately not verifiable in this work** — the consumer that would prove it is
  out of scope, so AE5 checks the record's shape and not its sufficiency. If the shape turns
  out to be wrong, the cost lands on the mining drone.
- R7. The dock presents a **Survey Results** tab showing findings in a form a player can act on
  — what was found, where, how concentrated, and how deep — and a separate **Survey Areas** tab
  for creating, listing, assigning and deleting areas. An unassigned dock's Areas tab presents
  the create action prominently, so the feature is discoverable without the player knowing to
  look for it.
- R7a. The tab shows how much of the assigned area has been surveyed and whether the drone is
  currently surveying, so an empty result is legible as "nothing found here yet" rather than
  being indistinguishable from "not walked yet" or "drone broken".
- R8. Survey findings survive the drone being removed from the dock, and are not persisted
  across a server restart.
- R8a. A dock holds at most one active survey drone. Separate docks may be assigned
  overlapping areas and each records independently. A drone whose dock no longer exists stops
  rather than accumulating findings it can never report.
- R9. Cancelling the area picker, or disconnecting while it is open, leaves the dock's prior
  assignment unchanged and leaves no pending state behind. Only an explicit confirm changes
  the assignment.

### Key Flows

- F1. **Create an area.** A1 opens the dock's Survey Areas tab, chooses to create a new area,
  draws its plots on the survey layer, names it, and confirms. **Outcome:** the area exists in
  the list and on the map. **Covers R1, R1b, R2.**
- F1a. **Assign an area.** A1 picks an existing area from the list and assigns it to the dock.
  **Outcome:** the dock holds that area and the drone begins surveying it. **Covers R1a, R3,
  R4.**
- F2. **Survey accumulates.** The drone roams the assigned area reporting findings; the dock's
  record grows. **Outcome:** the dock holds findings independent of the drone's presence.
  **Covers R4, R5, R8.**
- F3. **Read and decide.** A1 returns later, opens the dock, and reads the survey tab.
  **Outcome:** A1 knows where and how deep to mine without using chat. **Covers R7.**

### Acceptance Examples

- AE1. **Covers R1, R2.** Given a world with no district anywhere, when A1 creates a survey area
  from the dock, then the map opens on the mod's own survey layer for plot drawing, the area is
  created, and no district exists afterward. The survey layer is togglable in the map's layer
  list under the mod's own category, independently of the district layer.
- AE1a. **Covers R1a.** Given several existing survey areas, when A1 assigns a different one to
  the dock, then the drone surveys that area; and when A1 deletes the area a dock is using, then
  that dock becomes unassigned rather than breaking.
- AE1b. **Covers R2a.** Given another player has created survey areas, when A1 opens the survey
  layer, then A1 sees only their own areas.
- AE1c. **Covers R1c.** Given a dock inside owned property, when a player with `ConsumerAccess`
  opens it, then they can create and assign survey areas and read both tabs but cannot pick the
  dock up; and an unauthorized player cannot interact with it at all.
- AE2. **Covers R3, R3a.** Given an assigned area with findings, when A1 assigns a different
  area, then the drone surveys the new area, the earlier findings remain readable, and each
  finding is attributable to the area it came from.
- AE2a. **Covers R1b.** Given the picker is open, when A1 draws more plots than the drone's
  tier allows, then the selection is refused rather than silently accepted.
- AE2b. **Covers R9.** Given an assigned area, when A1 opens the picker and cancels or
  disconnects instead of confirming, then the dock's existing assignment is unchanged.
- AE3. **Covers R7.** Given the drone has found ore, when A1 opens the dock's survey tab, then
  the results are readable there — including depth — with no chat command used.
- AE4. **Covers R8.** Given accumulated findings, when A1 removes the drone from the dock,
  then the results remain readable in the tab.
- AE5. **Covers R5, R6.** Given findings for several ore types, then each carries a location
  more precise than its plot, a depth, and a concentration, in a single consistent shape.
- AE6. **Covers R3, R8.** Given accumulated findings, when the server restarts, then the dock
  still holds its assigned area and the drone resumes surveying it, while the findings record
  starts empty — the deliberate, documented behavior.
- AE7. **Covers R7a.** Given an area assigned but barely walked, when A1 opens the tab, then it
  distinguishes "surveyed, nothing found" from "not yet surveyed" and shows whether the drone
  is currently working.
- AE8. **Covers R8a.** Given a dock with a deployed drone, when the dock is destroyed, then the
  drone stops rather than continuing to accumulate findings it cannot report.

### Scope Boundaries

Out of scope for this work:

- The mining drone itself. Only the record it will read is guaranteed here.
- Dock-to-dock transfer as a shipped feature; the format must not preclude it, but no
  transfer is built.
- Persisting surveys across server restarts, and remembering which locations have already
  been mined — a later, more expensive mining automator's concern.
- Visual/art work on the objects, and the drone tier progression (deeper sensors, higher
  climb limits) already noted as a future axis.

### Dependencies / Assumptions

Verified against the game source during this brainstorm:

- **The map plot picker is reachable by a mod.** A player can be shown the map selection UI
  and return the chosen plots; the supporting overlay types are public. This is the same
  mechanism district editing uses, which is why the interaction can match it. Exact wiring is
  plan-time work.
**The map system is generic and districts are only one tenant.** The overlay contracts live in
`Server/Eco.Shared/Gameplay/Maps/IOverlay.cs` and `Server/Eco.Shared/UI/IMinimapOption.cs`:
`IMapEntryOverlay` (a `Dictionary<int, MapEntry>` of coloured entries plus an `Array2D<int>`
map referencing them), `IPlotOverlayWithMapLegend` (adds a legend key), and
`IMinimapCategorizedOption` (adds `FolderStructure`, `Priority`, `IsOnByDefault` — the layer's
place in the map's layer list). Independent implementors already in the tree:

Two properties matter and they are **separable** — no single existing layer is the model for
both:

| Layer | Player-drawn | Civics-free | Opened from a WorldObject |
|---|---|---|---|
| `DistrictMap` | yes | **no** | via civic slot |
| **Deeds / `PropertyManager`** | **yes** | **yes** | **yes — `RealEstateDesk`** |
| `InfluenceMap` | no — derived from settlements | yes | no |
| `WorldLayer` | no — simulated | yes | no |

- **Deeds are the structural precedent.** They are the one drawn, civics-free layer a player
  edits from a placed object, which is exactly this feature's shape.
- **Districts are the UX precedent.** An explicit region a player deliberately marks out for a
  purpose, which is what a survey area is. `InfluenceMap` and `WorldLayer` are *derived* layers
  — nobody draws them (`InfluenceMap.Map` is computed by `UpdateAndGetChangedPlots`) — so they
  prove only that a non-civics layer can exist and render, not that one can be drawn.

**Drawing does not require any special interface — just call `player.EditMap(request)`.**
`Deed.EditInMap` is a plain `[RPC(AccessType.OwnerAccess)]` calling `DeedEditingUtil.EditInMap`,
which builds the `MapEditRequest` and awaits `EditMap`. `ICustomClientEdit`
(`Server/Eco.Gameplay/Systems/ViewEditor.cs:23`) is **not** the general mechanism: it is
implemented by `DistrictMap` alone, and both call sites are civics plumbing
(`CivicObjectComponent`, `CivicsUtils`) for invoking an editor on a draft proposal. The mod
does not need it. `RelatedRegistrar` names a registrar owned by any plugin implementing
`IContainsRegistrars`, so a mod-owned registrar is structurally ordinary.

For the layer's own rendering, `InfluenceMap`
(`Server/Eco.Gameplay/InfluenceObjects/InfluenceMap.cs`) still shows the shape: a plain
`IController` with `Map = new Array2D<int>(PlotUtil.WorldPlotDims)`, its own `MapEntries`
colours, `LegendEntriesViewKey`, and `FolderStructure` placing it under its own category.

**For the dock end, the working precedent is `RealEstateDeskObject`** — a plain `WorldObject` that opens the map
picker and presents a data tab, which is precisely this feature's shape. It is 15 lines with no
logic of its own (`Server/Mods/__core__/Objects/RealEstateDeskObject.cs`); everything comes from
two components. Civics objects like `ZoningOffice` are *not* the model to copy: they are
`CivicObject`s holding a `DistrictMap` in a civic slot, which is the coupling R2 removes.

- **A component can declare a tab and sync collections into it.**
  `DeedManagementComponent` (`Server/Eco.Gameplay/Components/DeedManagementComponent.cs`) is the
  template: `[Serialized]`, `[CreateComponentTabLoc, HasIcon]`, and critically
  `Availability => WorldObjectComponentClientAvailability.UI`. Its tab body is
  `[SyncToView] IEnumerable<Deed> AllDeeds` — **a synced collection, not a single text blob** —
  refreshed by calling `this.Changed(nameof(...))`. So a list-shaped survey readout is the
  precedented shape, not a stretch goal.
- **Per-player filtered views exist.** The same component has
  `[SyncToView] IEnumerable<Deed> MyDeeds(Player player)` — a synced *method* taking the viewing
  player. If the no-permission-gate decision above is ever revisited, this is the mechanism that
  makes an owner-only readout possible without hiding the tab itself.
- **The map picker is reached from an object via an `[RPC]`.** `Deed.EditInMap` is
  `[RPC(AccessType.OwnerAccess)]` calling `DeedEditingUtil.EditInMap`, which builds the
  `MapEditRequest` and awaits `player.EditMap`. Note the engine gates its own map-edit entry
  point at the RPC; our decision not to is deliberate and recorded above.

Carried forward unverified — **the residual risk after the above**:

- Whether the client renders a synced collection of a **mod-defined** type. `Deed` is a type the
  stock client already knows; our finding type is not, and client views are emitted by build-time
  codegen a server-only mod does not participate in. This is the same failure class as the
  tooltip that does not render, and R7, F3 and AE3 depend on it.

- **Whether the client renders a mod-defined overlay as a selectable map layer.** The overlay
  contracts are `[ApplyOnView]` interfaces, and every in-tree implementor ships with the client.
  The same codegen question applies here as to the tab: the contracts are public and
  server-side, but a layer the stock client has never heard of may not appear in the layer
  list. This is now the single biggest unknown, because R1 and R2 both rest on it.

  **Resolution: one spike, riding the next deploy batch, covering both unknowns** — they share a
  root cause and must not cost two restarts:
  1. A component with `[CreateComponentTabLoc, HasIcon]`, `Availability = UI`, and two synced
     members: a `LocString` (conservative shape) and an `IEnumerable` of our own finding type
     (the shape R7 wants) — plus an `[RPC(AccessType.ConsumerAccess)]` invoked from that tab, to
     confirm a mod tab can carry an *action* and not only text. R1's "create area" button and
     R1a's per-row assign/delete all depend on that.
  2. A minimal `IController` overlay implementing `IPlotOverlayWithMapLegend` +
     `IMinimapCategorizedOption` with its own `FolderStructure`, drawn by calling
     `player.EditMap` directly (the deed pattern), reachable from a diagnostic command.

  One restart answers: does a mod tab render content, which property shapes survive, does a
  mod layer appear in the layer list, and can it be drawn on. R1, R2 and R7 are then all
  constrained to what the spike proves rather than to inference.
- Whether `MapEditRequest.RelatedRegistrar` may be left unset by a non-civics caller. District
  editing sets it to a client-known view name; deed editing leaves it unset, which is the basis
  for expecting null to work — but that is inference, not a positive test.
- The picker returns a world-sized `Array2D<int>` plus a client-editable `MapEntries` dictionary
  whose IDs are renumbered client-side, so the server must diff and re-validate rather than
  trusting what comes back. Following the deed pattern (`AllowNewEntries = false`, one fixed
  entry) keeps that reconciliation minimal.

Carried forward unverified:

- Whether the map picker imposes constraints when invoked outside a civics context (limits,
  permissions, labelling) is unknown and is a plan-time question.
- Whether `MapEditRequest.RelatedRegistrar` may be left unset by a non-civics caller. District
  editing sets it to a client-known view name; deed editing leaves it unset, which is the
  basis for expecting null to work — but that is inference, not a positive test.

### Sources / Research

- `docs/plans/2026-07-11-001-feat-survey-drone-plan.md` — the original product contract,
  whose R12 asked for map-based area selection and R14 for a dock readout. This work returns
  to both after the district and chat-command substitutions failed the intent.
- `docs/solutions/best-practices/ship-the-readout-not-just-the-data.md` — the learning that a
  feature whose output the player cannot read is not shipped; this plan is its correction.
