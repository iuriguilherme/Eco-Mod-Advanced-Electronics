# Survey Drone — Manual Unity Prefab Guide (U9/U10/U11)

Unity MCP is unavailable in this environment (subscription-gated), so U9 (dock
prefab), U10 (drone prefab + item icon), and U11 (bundle build + name-match gate)
from `docs/plans/2026-07-11-001-feat-survey-drone-plan.md` need to be done by hand
in the Unity Editor. This guide gives exact steps, exact names, and exact values —
follow it literally; the name-matching rules below are not stylistic, they are
how the server and client sides of the mod find each other (see this repo's
`CLAUDE.md`: "objects match by name — a `WorldObject` prefab name here must equal
the server object name exactly").

Placeholder art (a primitive cube/capsule) is fine for v1 — nothing in the plan
requires custom meshes, and swapping in real art later doesn't change any of the
naming/wiring below.

**Server-side names you must match exactly** (confirmed by grep against
`EcoServerMod/AdvancedElectronics/`, current as of commit `4e21daa`):

| Server C# class | Base type | What it needs client-side |
|---|---|---|
| `DroneDock` | `WorldObject` | A prefab named exactly `DroneDock` |
| `SurveyDrone` | `WorldObject` | A prefab named exactly `SurveyDrone` |
| `SurveyDroneItem` | `Item` | An icon asset named exactly `SurveyDroneItem` |

`DroneDockItem` (`WorldObjectItem<DroneDock>`) is not in this table on purpose —
a `WorldObjectItem<T>` represents the placement item for a WorldObject and is
expected to reuse `DroneDock`'s own prefab/icon rather than needing a second,
separately-named asset. If in-game testing later shows the dock's inventory icon
is blank/wrong, this is the first assumption to revisit.

---

## 0. One-time scene setup

Skip this section if `Assets/EcoModKit/TemplateScene.unity` (or another scene)
already has `Objects`/`Items`/`Emoji`/`BlockSets` root GameObjects — check first,
this repo is a fresh ModKit setup per `CLAUDE.md` and likely does not yet.

1. Open `Assets/EcoModKit/TemplateScene.unity` (or create a new scene dedicated to
   this mod's content — either works, `ModkitPrefabContainer` just needs to exist
   somewhere in whichever scene you build bundles from).
2. Per `Assets/EcoModKit/Docs/README.md` → "Setting up the scene": create empty
   root GameObjects named `Objects` (add component `ModkitPrefabContainer`),
   `Items`, `Emoji` (add component `ChatEmoteSetOld`), `BlockSets` (add component
   `BlockSetContainer`). This mod doesn't need Emoji/BlockSets content, but the
   ModKit tooling expects the roots to exist.
3. Save the scene.

---

## 1. U9 — Dock prefab (`DroneDock`)

**Goal (from the plan):** a `DroneDock` client prefab with launch/return/working
animation states and a readout surface, exact-name-matched to the server dock
class.

1. In the Hierarchy, create an empty GameObject at the scene root.
2. **Rename it to exactly `DroneDock`** (case-sensitive, no suffix).
3. Set its **Tag** to `ModObject` (Inspector → Tag dropdown → if `ModObject` isn't
   listed, use "Add Tag..." — it should already be registered by the ModKit, but
   confirm).
4. Click **Add Component** → type `WorldObject` → add it (this is the ModKit's
   `Assets/EcoModKit/Scripts/WorldObject.cs` component, distinct from the C#
   server-side `Eco.Gameplay.Objects.WorldObject` class of the same name — you
   want the one that shows Unity `MonoBehaviour` fields like `States`,
   `StringStates`, `FloatStates` in the Inspector).
5. Add a visual placeholder: create a child `Cube` (GameObject → 3D Object →
   Cube), scale it to roughly dock-sized (e.g. 1.5 × 1 × 1.5), and give it any
   material. Real art is a follow-up, not required for this unit.
6. **Wire the readout state slots.** On the `WorldObject` component, set:
   - **String States** (size 7): `ReadoutStatus`, `ReadoutOre0`, `ReadoutOre1`,
     `ReadoutOre2`, `ReadoutOre3`, `ReadoutOre4`, `ReadoutOre5`
   - **Float States** (size 1): `ReadoutCoverage`

   These exact names come from `EcoServerMod/AdvancedElectronics/DroneDock.cs`'s
   `RefreshReadout()` method (`StatusStateName = "ReadoutStatus"`,
   `OreLineStateNamePrefix = "ReadoutOre"` for indices 0–5 per
   `DockReadout.MaxOreLines`, `CoverageStateName = "ReadoutCoverage"`) — if that
   file changes these constants later, update this list to match, not the other
   way around; the C# source is authoritative.
7. **Give the readout somewhere to render.** `Assets/TextMesh Pro/` is already
   imported in this project. Add a child `TextMeshPro - Text` object (or a
   `Canvas` + `TextMeshPro - Text (UI)` if you want a screen-space panel) as a
   readout surface, then wire each `OnStringStateChanged`/`OnFloatStateChanged`
   event on the `WorldObject` component (one array slot per state name above) to
   update that text — e.g. `OnStringStateChanged[0]` (for `ReadoutStatus`) calls a
   small method that sets the TMP text's first line, and so on for the ore lines
   and the coverage float. A single small script (e.g.
   `Assets/Art/AdvancedElectronics/DockReadoutDisplay.cs`, a plain
   `MonoBehaviour`, client-only, no relation to the server-side `DockReadout.cs`)
   that concatenates the wired-up values into the TMP text is the simplest
   approach — build it however reads cleanly; this is presentation-only glue with
   no correctness requirement beyond "the text updates when the state does."
8. **Save as a prefab:** drag the `DroneDock` GameObject from the Hierarchy into
   `Assets/Art/AdvancedElectronics/` in the Project window (create that folder
   first if it doesn't exist). Confirm the resulting file is named
   `DroneDock.prefab`.
9. Delete the GameObject instance from the scene (the README's documented
   pattern — the prefab asset is what matters, not a scene instance).
10. **Register it:** select the `Objects` root GameObject, expand its
    `ModkitPrefabContainer` component's `Prefabs` list, click `+`, and drag in
    the new `DroneDock` prefab.

---

## 2. U10 — Drone prefab + item icon (`SurveyDrone`, `SurveyDroneItem`)

**Goal (from the plan):** a `SurveyDrone` client prefab with locomotion-appropriate
animation, and a 64×64 item icon, exact-name-matched to the server classes.

### 2a. Drone prefab

1. Create an empty GameObject at the scene root, **rename it to exactly
   `SurveyDrone`**.
2. Tag `ModObject`, **Add Component** → `WorldObject` (same component as the
   dock).
3. Add a visual placeholder: a child `Capsule` scaled roughly drone-sized (e.g.
   0.6 × 0.6 × 0.6) is a reasonable ground-rover placeholder.
4. No `StringStates`/`FloatStates` needed on this prefab — the dock owns the
   readout (see U9); the drone itself has no synced text/gauge state to wire.
5. If you want locomotion animation now: add an `Animator` component and drive
   it from movement — this needs the drone's actual server-synced position
   deltas to look right, which you can't fully verify without a live server (see
   `docs/protocols/2026-07-survey-drone-manual-protocol.md`, F2). A static
   placeholder mesh is acceptable for this pass; note the animation as a
   follow-up if you skip it.
6. Save as a prefab into `Assets/Art/AdvancedElectronics/SurveyDrone.prefab`
   (same drag-into-Project-window pattern as the dock), delete the scene
   instance, and register it in the `Objects` root's `ModkitPrefabContainer`
   `Prefabs` list.

### 2b. Item icon

1. In the Project window, browse to `Assets/EcoModKit/Prefabs`.
2. Drag `ItemTemplate` onto the `Items` root GameObject in the Hierarchy — this
   creates a child GameObject called `ItemTemplate`.
3. Right-click that new child → **Prefab → Unpack Completely** (removes the
   prefab link so you can freely edit/rename it).
4. **Rename the GameObject to exactly `SurveyDroneItem`** — matching the C#
   `Item` subclass name in `EcoServerMod/AdvancedElectronics/SurveyDrone.cs`, per
   this doc's naming table above. (This is an ASSUMPTION about Eco's item
   name-matching convention, made by analogy with the WorldObject naming rule
   confirmed in `CLAUDE.md` — verify this is correct once you can deploy to a
   live server; if the item's icon doesn't show up correctly in-game, this
   exact-name choice is the first thing to re-check.)
5. Edit the foreground/background sprites per the `ItemTemplate`'s fields to a
   64×64 placeholder icon (any simple shape/color is fine for this pass — a
   distinct silhouette so it's visually distinguishable from other items is the
   only real constraint).

---

## 3. U11 — Asset bundle build + name-match validation

1. **Tag the scene root for bundling:** select your `Objects` GameObject (or
   whichever root the ModKit's bundle-tagging step expects — see
   `Assets/EcoModKit/Scripts/Editor/ModKitTools.cs`), then use
   **Eco Tools → Mod Kit → ModKit Tools...** to tag it into a named bundle (e.g.
   `AdvancedElectronics`).
2. Build: **Eco Tools → Mod Kit → Build Current Bundle** (or "build all bundles"
   from the ModKit Tools window, per `CLAUDE.md`'s Building/exporting section) —
   output lands in `AssetBundles/` (git-ignored, per the plan's U11 Files note).
3. **Run the name-match check** — this is scripted and needs no Unity Editor,
   only the assets to exist on disk:

   ```bash
   ./scripts/validate-name-match.sh
   ```

   It cross-checks every server `WorldObject`/`Item` class in
   `EcoServerMod/AdvancedElectronics/` against asset basenames under
   `Assets/Art/AdvancedElectronics/` and exits non-zero with a `MISMATCH:` line
   for anything unmatched. Run it after step 8/2b above (prefabs saved) even
   before building the bundle — it only reads file names, not bundle contents.
   A clean run looks like:

   ```
   Server WorldObject types (need a name-matching prefab): DroneDock SurveyDrone
   Server Item types (need a name-matching icon asset):    SurveyDroneItem
   Client asset basenames found: DroneDock SurveyDrone SurveyDroneItem ...
   PASS: every server WorldObject/Item type has a matching-named client asset.
   ```

4. Record the bundle build + name-match result in
   `docs/plans/2026-07-11-001-feat-survey-drone-plan.md`'s Definition of Done
   once both pass.

---

## After this guide

Once U9/U10/U11 are done, the plan's global Definition of Done is fully
satisfied server-side and client-side. The only remaining step is running
`docs/protocols/2026-07-survey-drone-manual-protocol.md`'s live-server F1–F3/
AE1–AE9 checks on an actual Eco 0.13.0.4 dedicated server with the bundle
installed.
