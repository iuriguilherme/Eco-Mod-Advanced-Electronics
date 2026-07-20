---
title: Survey Drone v1 Closure (States-Only Visuals + Acceptance Run) - Plan
type: feat
date: 2026-07-19
topic: survey-drone-v1-closure
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
product_contract_source: ce-brainstorm
execution: code
---

# Survey Drone v1 Closure (States-Only Visuals + Acceptance Run) - Plan

## Goal Capsule

> **STATUS (2026-07-20): NOT COMPLETE. This plan's premise proved wrong.**
> It was written believing R16's visual layer was the only remaining slice. Live testing
> then found the feature did not work at all: the drone never moved (walkability predicate
> treated its own occupancy block as solid), and once it did move, survey results reached
> no player-readable surface. Both are fixed, but AE1–AE9 remain **unaccepted**, further
> sessions are required, and no part of this plan should be read as evidence the goal is
> reached. Treat the "last open slice" framing below as historical.

- **Objective:** Close out `docs/plans/2026-07-11-001-feat-survey-drone-plan.md` by fulfilling its
  last open slice — R16's visual layer — at a deliberately re-scoped "states only" bar, then
  recording the owner acceptance run's AE1–AE9 verdicts.
- **Product authority:** The parent plan, amended by this brainstorm's dialogue: the owner chose
  "states only" for v1 visuals and server-side proof for their verification.
- **Product Contract preservation:** unchanged.
- **Open blockers:** the objective above understated the remaining work; see the status note.
  The acceptance run is owner-run by design (the parent plan's own
  out-of-CI gate), not a blocker to implementing this plan's code scope.

## Product Contract

### Summary

v1 ships the animation **state contract** without the art: the server pushes named animation
states (dock activity, drone movement speed) through the same server-synced state surface the
readout already uses, and the client prefabs declare those state names so future art binds to
them without server changes. No meshes, animators, or visible animation ship in v1. The pushed
values are verified server-side via the existing `/drone status` diagnostic during the single
batched acceptance session that also records AE1–AE9.

### Key Decisions

- **R16 is re-scoped, explicitly.** The parent plan's R16 letter ("launch/return/working
  animation states … locomotion-appropriate animation") is amended for v1 to: the state
  *contract* exists end-to-end (server pushes + prefab declarations) and is verified
  server-side. Visible animation and meshes move to a follow-up art milestone. v1 "done" is
  judged against this amended scope — recorded here so it is a decision, not a fudge.
- **Server-side proof over a debug visual.** `/drone status` prints the pushed state values;
  no throwaway visible reaction is built. Consequence accepted: the client-side half of the
  state pipeline (prefab binding actually receiving values) stays unverified until the art
  milestone, which inherits that verification obligation along with the art itself.
- **One batched deploy.** All changes ship in the already-pending deploy batch; the single
  acceptance session adjudicates everything (per
  `docs/solutions/workflow-issues/eco-mod-batched-live-testing.md`).

### Requirements

- R1. The server pushes a named activity state for the dock and a named movement-speed state
  for the drone, updated from the drone's lifecycle status and mover state, through the same
  server-synced state mechanism the dock readout already uses.
- R2. Both client prefabs declare those state names in their state arrays, forming the binding
  contract future art attaches to without further server changes.
- R3. `/drone status` additionally reports the current pushed animation-state values, so the
  acceptance session can verify them in chat.
- R4. The owner acceptance run executes the parent plan's manual protocol
  (`docs/protocols/2026-07-survey-drone-manual-protocol.md`), records AE1–AE9 verdicts, and
  includes the R3 state readback; afterwards the pending-verification claims in
  `docs/solutions/` are updated to recorded outcomes.

### Scope Boundaries

Deferred to the follow-up art milestone:

- Meshes/models beyond the current placeholders (composed-primitive, AI-generated, or imported).
- Animator controllers, animation clips, and any visible reaction to the pushed states.
- Client-side verification that prefab bindings receive the pushed values (inherited obligation).

Out of scope entirely: changes to drone behavior, survey logic, readout content, or the
acceptance protocol itself.

### Acceptance Examples

- AE-C1. **Covers R1, R3.** While the drone is en route or roaming, `/drone status` shows a
  positive movement-speed value and the dock activity state reflecting the current
  `DroneStatus`; while docked/idle, speed reads zero and the activity state reflects idle.
- AE-C2. **Covers R2.** The built bundle's dock and drone prefabs declare the exact state
  names the server pushes (name-for-name match, same convention as the readout states).
- AE-C3. **Covers R4.** After the acceptance session, each of the parent plan's AE1–AE9 has a
  recorded verdict, and no `docs/solutions/` entry for this feature still says "pending live
  confirmation" for a claim the session actually exercised.

### Sources / Research

- `docs/plans/2026-07-11-001-feat-survey-drone-plan.md` — the parent plan this closure amends
  (R16) and fulfils; its Verification Contract defines the acceptance gates reused here.
- `docs/solutions/workflow-issues/eco-mod-batched-live-testing.md` — the batched-deploy rule
  this plan's single-session verification shape follows.
- This session's engine-source verification that animator parameters auto-bind to declared
  state names on the client (`WorldObject.BindEvents`), which is what makes the states-only
  contract sufficient for future art to attach without server changes.

## Planning Contract

### Key Technical Decisions

- **KTD1 — State names are the contract: `Working` (dock, bool) and `MoveSpeed` (drone, float).**
  Confirmed with the owner at plan time. `Working` is true while the dock's paired drone is
  EnRoute or Surveying (false for Idle/Unreachable/no drone); `MoveSpeed` is the drone's
  current speed in world units/second (the mover's constant while moving, 0 when stationary).
  Renaming later touches server, prefabs, and bundle at once — treat as frozen.
- **KTD2 — Push sites reuse existing tick surfaces; push on change, not every tick.** The dock
  pushes `Working` from its existing `Tick()` (same cadence as the readout refresh, only when
  the value changes). The drone's mover pushes `MoveSpeed` on movement transitions
  (start/stop), not per-tick — `SetAnimatedState` is a synced dictionary write; per-tick
  same-value writes are churn with no consumer.
- **KTD3 — Prefab state arrays are hand-edited YAML + reimport, per the established
  keyboard-only workflow.** The dock's bool `States` array is safe to hand-edit:
  `DockReadoutDisplay.EnsureStateArrays` force-sets only `StringStates`/`FloatStates`, never
  `States`. The drone prefab has no tool interference. Event-array lengths must match their
  name arrays (engine indexes them pairwise — same convention already proven for the readout).

### Sequencing

U1 → U2 (readback needs the pushes) and U3 independently buildable; U4 blocked on the
owner-run acceptance session, which follows the single batched deploy of U1–U3.

## Implementation Units

### U1. Server-side state pushes (dock `Working`, drone `MoveSpeed`)

- **Goal:** The server pushes the two contract states from existing tick surfaces.
- **Requirements:** R1.
- **Dependencies:** none.
- **Files:** `EcoServerMod/AdvancedElectronics/DroneDock.cs`,
  `EcoServerMod/AdvancedElectronics/DroneMoverComponent.cs`.
- **Approach:** Per KTD1/KTD2. Dock: in `Tick()` (alongside the readout refresh pacing),
  compute `working = SpawnedDrone alive && lifecycle.Status is EnRoute or Surveying`; call
  `SetAnimatedState("Working", working)` only when the value differs from the last push
  (cache the last pushed value in a private field). Mover: track a `wasMoving` field; when
  `IsMoving` transitions, call `Parent.SetAnimatedState("MoveSpeed", moving ?
  MoveSpeedMetersPerSecond : 0f)`. The bool overload of `SetAnimatedState` exists on the
  same surface as the string/float overloads already in use.
- **Patterns to follow:** `RefreshReadout()`'s existing `SetAnimatedState` usage and its
  change-pacing field (`secondsSinceLastReadoutRefresh`).
- **Test scenarios:** `Test expectation: none — Eco-bound state sync; the value logic is two
  one-line derivations verified via U2's readback in the acceptance session (AE-C1).`
- **Verification:** `dotnet build` green; Navigation suite still passes untouched (34/34).

### U2. `/drone status` reports the pushed animation-state values

- **Goal:** The diagnostic prints both contract states so AE-C1 is checkable in chat.
- **Requirements:** R3.
- **Dependencies:** U1.
- **Files:** `EcoServerMod/AdvancedElectronics/DroneCommands.cs`.
- **Approach:** In the existing `Status` subcommand, after the lifecycle/mover lines, print
  the dock's `Working` and the drone's `MoveSpeed` via `GetAnimatedState<T>` guarded for
  never-pushed keys (report "not yet pushed" rather than throwing — the AnimatedStates
  dictionary indexer throws on a missing key, and states are pushed on change, so a
  just-placed dock may not have pushed yet).
- **Patterns to follow:** the existing `Status` subcommand's per-layer line format.
- **Test scenarios:** `Test expectation: none — Eco-bound diagnostic output; exercised live
  by AE-C1.`
- **Verification:** `dotnet build` green; the command compiles with the missing-key guard.

### U3. Prefab state-array declarations + bundle rebuild

- **Goal:** Both prefabs declare the contract state names, and the rebuilt bundle ships them.
- **Requirements:** R2.
- **Dependencies:** none (deployable batch requires U1–U3 together).
- **Files:** `Assets/Art/AdvancedElectronics/DroneDockObject.prefab`,
  `Assets/Art/AdvancedElectronics/SurveyDroneObject.prefab`.
- **Approach:** Per KTD3. Dock: add `Working` to the `States` array and one matching entry to
  each of `OnStateEnabledEvents`/`OnStateDisabledEvents`/`OnStateChangedEvents` (pairwise
  indexing). Drone: add `MoveSpeed` to `FloatStates` plus one `OnFloatStateChanged` entry.
  Reimport via Unity MCP, verify the arrays via an editor script (same in-editor proof used
  for the readout listener), rebuild the bundle, copy to the server `Mods/UserCode/`.
- **Patterns to follow:** the dock's existing `StringStates`/`OnStringStateChanged` YAML
  block (exact serialization shape for name + empty persistent-call event entries).
- **Test scenarios:** `Test expectation: none — serialized asset data; name-for-name match
  against U1's pushed names checked in-editor (AE-C2) before bundling.`
- **Verification:** In-editor script confirms both prefabs' arrays contain exactly the KTD1
  names with matching event-array lengths; bundle rebuilds and deploys.

### U4. Acceptance-run docs closure

- **Goal:** After the owner's single acceptance session, the knowledge base reflects recorded
  outcomes instead of pending claims.
- **Requirements:** R4.
- **Dependencies:** U1–U3 deployed; owner-run session completed (out-of-CI, per parent plan).
- **Files:** `docs/protocols/2026-07-survey-drone-manual-protocol.md` (verdicts),
  `docs/solutions/conventions/eco-custom-worldobject-placement-requirements.md`,
  `docs/solutions/runtime-errors/worldobject-zero-size-blocks-placement.md`,
  `docs/solutions/runtime-errors/worldobjectcomponent-missing-attributes-empty-window.md`,
  `docs/solutions/ui-bugs/modkit-prefab-materials-need-curved-shaders.md`,
  `docs/solutions/workflow-issues/eco-mod-batched-live-testing.md` (only where a pending
  claim was actually exercised).
- **Approach:** Record AE1–AE9 verdicts in the protocol doc from the owner's session evidence
  (screenshots, `/drone status` output, server log). Update each `docs/solutions/` entry whose
  "pending live confirmation" claim the session exercised — to confirmed or to the recorded
  failure, honestly either way. Unexercised claims stay pending, stated as such.
- **Test scenarios:** `Test expectation: none — documentation pass.`
- **Verification:** AE-C3 — no exercised claim still reads "pending"; every AE1–AE9 has a
  recorded verdict.

## Verification Contract

| Gate | Command / method | Applies to | Blocking |
|---|---|---|---|
| Server mod compiles | `dotnet build EcoServerMod/AdvancedElectronics` | U1, U2 | Yes |
| Navigation suite unchanged | `dotnet test EcoServerMod/AdvancedElectronics.Navigation.Tests` (34/34) | U1 (no regressions) | Yes |
| Prefab contract check | In-editor script: both prefabs declare exactly `Working`/`MoveSpeed` with matching event-array lengths | U3 | Yes |
| Single batched live session | Owner runs parent protocol + AE-C1 state readback via `/drone status` | U1–U3 (AE-C1), U4 input | Out of CI — owner-run |

## Definition of Done

- U1–U3 built, in-editor verified, and deployed in one batch (DLLs + bundle) with zero
  intermediate restarts requested of the owner.
- `/drone status` reports both contract states with the missing-key guard.
- After the owner session: AE1–AE9 verdicts recorded; AE-C1/AE-C2 outcomes recorded;
  exercised `docs/solutions/` pending claims updated (U4).
- Parent plan's Definition of Done thereby fully met under the recorded R16 re-scope.
