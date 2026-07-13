# Survey Drone — Manual In-Game Protocol

Owner-run verification for `docs/plans/2026-07-11-001-feat-survey-drone-plan.md`'s
Verification Contract row "In-game flows (F1–F3, AE1–AE9)". Extends
`docs/spikes/2026-07-survey-drone-spike.md`'s protocol shape (probe → steps → verdict
table) to the shipped feature rather than the spike's throwaway `/spike` commands.

**Prerequisites:**

- An Eco 0.13.0.4 dedicated server with `AdvancedElectronics.dll` (from
  `EcoServerMod/AdvancedElectronics/`, **not** the spike DLL) deployed to
  `Mods/UserCode/` — see `EcoServerMod/README.md` for build/deploy steps.
- The client asset bundle from `AssetBundles/` installed (U9/U10/U11 — dock prefab,
  drone prefab + item icon, bundle build). **Not yet built as of this writing** (U9–U11
  are pending on Unity Editor / Unity MCP availability); the server-side flows below
  (F1–F2 dispatch/survey logic, AE1–AE4, AE7–AE9) can be exercised with the vanilla
  fallback appearance Eco gives an unbundled `WorldObject`/`Item` (no custom mesh/icon,
  functionally complete). AE5/AE6 (readout) need the dock's `StringStates`/`FloatState`
  wired to a prefab panel to *see* — until U9 lands, read the synced state values via
  a debug/inspector route instead (e.g. Unity MCP's console/inspector once reconnected,
  or a temporary admin command) rather than treating this as a hard blocker.
- A second player account (or a friend) for AE2 (wolf/tool damage) and AE3 (claim
  crossing) — a claim requires a second player's deed, and provoking a wolf needs a
  live one nearby.
- Two districts drawn via the map/law interface, at least one separated from the other
  by open water or another impassable gap (for AE9).

All verdicts below are blank pending the owner's live run — fill in pass/partial/fail +
evidence per row, mirroring the spike report's table format.

---

## F1 — Assign and dispatch

**Setup:** Craft a Drone Dock and a Survey Drone item (via `ElectricMachinistTableObject`
per the recipes in `DroneDock.cs`/`SurveyDrone.cs`); place the dock.

**Steps:**

1. Draw a district on the map/law interface; note its name.
2. Run `/drone district <name>` standing near the dock (must be the nearest dock you
   have full access to — see `DroneCommands.cs`'s design note).
3. Insert the Survey Drone item into the dock's storage slot.
4. Observe: does a drone WorldObject spawn near the dock and begin moving toward the
   assigned district?

| Check | Verdict | Evidence |
|---|---|---|
| `/drone district <name>` resolves and stores the assignment | | |
| Inserting the item spawns a physical drone WorldObject | | |
| The drone begins moving toward the district on its own | | |

---

## F2 — Survey loop

**Setup:** Continue from F1 with the drone en route to its district.

**Steps:**

1. Follow or observe the drone until it arrives inside the district boundary.
2. Confirm it stops roaming toward the district and instead wanders/surveys within it
   (status should read `Surveying` — see AE5/AE6 for reading the status).
3. Let it run for a few minutes over terrain with at least one known ore vein.

| Check | Verdict | Evidence |
|---|---|---|
| Drone stops at the district boundary and begins surveying | | |
| Drone continues moving/sampling within the district (not frozen) | | |

---

## F3 — Read results

**Setup:** Continue from F2 after some survey time has accumulated.

**Steps:**

1. Inspect the dock's readout (panel once U9 lands; synced state values directly
   until then).
2. Confirm a status line and at least one per-ore `"<ore>: densest at <cell>, ~<pct>%"`
   line appear for ore types the drone has crossed.
3. Confirm the coverage gauge shows a nonzero value once sampling has occurred.

| Check | Verdict | Evidence |
|---|---|---|
| Status line reflects current drone status | | |
| At least one per-ore densest-cell line appears and looks plausible | | |
| Coverage gauge is nonzero after sampling | | |

---

## AE1 — Obstacle avoidance (covers R2)

**Setup:** Place a table (or any solid block) directly between the dock and the
assigned district, on the straight line between them.

**Steps:** Dispatch the drone (F1) and watch it approach the obstacle.

| Check | Verdict | Evidence |
|---|---|---|
| Drone routes around the table/block rather than clipping through it | | |

---

## AE2 — Invulnerability (covers R3)

**Setup:** A dispatched, roaming drone; a tool; and (if available) a wolf or other
hostile animal nearby.

**Steps:** Strike the drone with a tool. Separately, let a wolf attack it (or lure one
toward it).

| Check | Verdict | Evidence |
|---|---|---|
| Tool strikes deal no damage; drone continues its task | | |
| Wolf/animal attacks deal no damage; drone continues its task | | |

---

## AE3 — Free-roam + attribution (covers R4, R5)

**Setup:** A second player's claim/deed positioned so the drone's route to its district
crosses it.

**Steps:** Dispatch the drone (F1) so its path crosses the claim.

| Check | Verdict | Evidence |
|---|---|---|
| Drone crosses the claim freely (no blocked path, no permission prompt) | | |
| The crossing is attributable to the drone's owner (`SurveyDrone.OwnerName`/`OwnerId` — inspect via Unity MCP or a debug read if no in-game UI surfaces it yet) | | |

---

## AE4 — Idle when unassigned (covers R6)

**Setup:** A dock with a paired, previously-dispatched drone.

**Steps:** Clear the district assignment (`/drone district` with no name) while the
drone is mid-survey.

| Check | Verdict | Evidence |
|---|---|---|
| Drone stops surveying and returns to the dock | | |
| Status resolves to `Idle` once back at the dock (not stuck `EnRoute`) | | |

---

## AE5 — Per-ore readout (covers R7, R14)

Same setup/steps as F3. Confirm the reported density corresponds to ore you can
independently verify is actually present in the surveyed area (dig a test hole near the
reported densest cell).

| Check | Verdict | Evidence |
|---|---|---|
| Readout reveals ore presence the player could not otherwise know without digging | | |
| A test dig near the reported densest cell corroborates the reading | | |

---

## AE6 — Densest-cell spatial cue (covers R8)

Same setup as AE5. Compare the reported densest cell against a second, deliberately
sparser area the drone also crossed.

| Check | Verdict | Evidence |
|---|---|---|
| Readout names a specific cell, not just "ore present somewhere" | | |
| The named cell is plausibly the richer of two areas surveyed (spot-check with a dig) | | |

---

## AE7 — District assignment dispatches (covers R12)

Same as F1 steps 1–3.

| Check | Verdict | Evidence |
|---|---|---|
| Assigning a district via the command dispatches the drone | | |

---

## AE8 — Immediate re-path on reassignment (covers R13)

**Setup:** A drone mid-survey in district A.

**Steps:** Run `/drone district <B>` (a different, reachable district) while the drone
is `Surveying` in A.

| Check | Verdict | Evidence |
|---|---|---|
| Drone immediately turns toward B from its current position (not back to the dock first) | | |
| Status transitions `Surveying` → `EnRoute` targeting B | | |

---

## AE9 — Unreachable status (covers R15)

**Setup:** A district separated from the dock by open water or another impassable gap
the pathfinder cannot cross.

**Steps:** Assign that district to the dock.

| Check | Verdict | Evidence |
|---|---|---|
| Drone fails to find a path and does not get stuck silently | | |
| Status shows `Unreachable` | | |
| A return-to-dock attempt is made (and either succeeds, or also reports `Unreachable` if the return leg is also blocked) | | |

---

## Closing checklist

- [ ] Every check above has a recorded verdict (pass/partial/fail) + evidence.
- [ ] Any fail is filed as a follow-up (bug or plan amendment) before the feature is
      considered shipped, per the plan's Definition of Done.
- [ ] `docs/plans/2026-07-11-001-feat-survey-drone-plan.md`'s Definition of Done is
      updated to reference this protocol's completion.
