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

**Keyboard-only path (recommended).** `DockReadoutDisplay.cs` is now
self-wiring (no dragging, no Inspector event arrays to fill in by hand — see
its class doc comment for exactly what it does automatically), and
`Assets/Art/AdvancedElectronics/Editor/AdvancedElectronicsBuildTools.cs` adds
a menu command that does the mechanical finishing work (tag, save-as-prefab,
register in the container) in one shot. Unity 6's "Search Anything" command
palette lets you reach any menu item by typing its name and pressing Enter —
no mouse needed for that step either (check Edit menu / your keybinding if you
don't already know the shortcut on your machine; it's usually bound near
Ctrl+K).

1. In the Hierarchy, create an empty GameObject at the scene root (`Ctrl+Shift+N`
   creates an empty GameObject under the current selection/root; rename via
   the Hierarchy's inline rename, `F2` on Windows).
2. **Rename it to exactly `DroneDock`** (case-sensitive, no suffix).
3. Add a mesh child however you prefer (a primitive `Cube` is fine — real art
   is a follow-up, not required here).
4. Add a `Canvas` child, set its **Render Mode** to `World Space` (Overlay
   would cover the whole screen for every dock in the world), and add a
   `Text (TMP)` child under it — this just needs to exist somewhere under
   `DroneDock`; `DockReadoutDisplay` finds it automatically at runtime via
   `GetComponentInChildren<TMP_Text>()`, no reference to assign.
5. Select `DroneDock` and **Add Component** → type `WorldObject` (the ModKit's
   `Assets/EcoModKit/Scripts/WorldObject.cs`, not the C# server-side class of
   the same name) → Enter.
6. **Add Component** → type `Dock Readout Display` → Enter. The moment this
   component is added, its `Reset()` callback fires automatically and sets
   `WorldObject`'s `StringStates`/`FloatStates` arrays to the exact 7 + 1 slot
   names `EcoServerMod/AdvancedElectronics/DroneDock.cs`'s `RefreshReadout()`
   expects (`ReadoutStatus`, `ReadoutOre0`..`ReadoutOre5`, `ReadoutCoverage`) —
   nothing left to type into the custom WorldObject Inspector's "Add"/"Handler"
   UI. (If you'd already added a `ReadoutStatus` entry by hand before this —
   as seen in an earlier screenshot in this conversation — it gets overwritten
   with the same full canonical set; no harm.)
7. Run the finishing command: open the command palette, type
   `Advanced Electronics/Finish Dock Prefab`, press Enter (or use the
   `Eco Tools` menu if you'd rather navigate it directly). With `DroneDock`
   selected, this: sets the `ModObject` tag, saves the GameObject as
   `Assets/Art/AdvancedElectronics/DroneDock.prefab`, and registers that
   prefab into the `Objects` root's `ModkitPrefabContainer` — check the
   Console for its `[AdvancedElectronics]`-prefixed log lines confirming each
   step (or a warning telling you exactly what's still missing, e.g. no TMP
   text found, or no `ModkitPrefabContainer` in the open scene).

---

## 2. U10 — Drone prefab + item icon (`SurveyDrone`, `SurveyDroneItem`)

**Goal (from the plan):** a `SurveyDrone` client prefab with locomotion-appropriate
animation, and a 64×64 item icon, exact-name-matched to the server classes.

### 2a. Drone prefab

1. Create an empty GameObject at the scene root, **rename it to exactly
   `SurveyDrone`**.
2. Add a visual placeholder: a child `Capsule` scaled roughly drone-sized (e.g.
   0.6 × 0.6 × 0.6) is a reasonable ground-rover placeholder.
3. **Add Component** → `WorldObject` (same component as the dock). No
   `DockReadoutDisplay` here — the dock owns the readout (see U9); the drone
   itself has no synced text/gauge state to wire.
4. If you want locomotion animation now: add an `Animator` component and drive
   it from movement — this needs the drone's actual server-synced position
   deltas to look right, which you can't fully verify without a live server (see
   `docs/protocols/2026-07-survey-drone-manual-protocol.md`, F2). A static
   placeholder mesh is acceptable for this pass; note the animation as a
   follow-up if you skip it.
5. Run the finishing command (same "Search Anything" palette as U9): type
   `Advanced Electronics/Finish Drone Prefab`, press Enter, with `SurveyDrone`
   selected. Sets the `ModObject` tag, saves
   `Assets/Art/AdvancedElectronics/SurveyDrone.prefab`, and registers it in the
   `Objects` root's `ModkitPrefabContainer` — check the Console for
   confirmation.

### 2b. Item icon

**Keyboard-only path (recommended):** this whole step is now one command.
`ItemTemplate` (`Assets/EcoLibs/Utils/MiscUtils/ItemTemplate.cs`) is the
script the ModKit's `ItemTemplate` prefab carries at its root; its
`foreground`/`background` fields are plain `UnityEngine.UI.Image` components
— "editing the sprite" means assigning a `Sprite` asset to an `Image`'s
`Source Image` field. `FinishItemIcon()` in
`Assets/Art/AdvancedElectronics/Editor/AdvancedElectronicsBuildTools.cs` does
all of the following in code, no dragging required:

1. Open the command palette, type `Advanced Electronics/Finish Item Icon`,
   press Enter.
2. It instantiates `Assets/EcoModKit/Prefabs/ItemTemplate.prefab` under the
   scene's `Items` root, unpacks it completely (the same effect as the
   README's manual drag-and-unpack steps), and renames it to exactly
   `SurveyDroneItem` — matching the C# `Item` subclass name in
   `EcoServerMod/AdvancedElectronics/SurveyDrone.cs` per this doc's naming
   table above. (This exact-name choice is still an ASSUMPTION about Eco's
   item name-matching convention, made by analogy with the WorldObject naming
   rule confirmed in `CLAUDE.md` — verify once you can deploy to a live
   server; if the item's icon doesn't show up correctly in-game, this is the
   first thing to re-check.)
3. It generates a solid placeholder 64×64 PNG
   (`Assets/Art/AdvancedElectronics/SurveyDroneItem_icon.png`, teal-blue) and
   assigns it to the `foreground` Image's sprite. `background` is left at the
   template's default (the shared item-plate frame every vanilla item icon
   sits on) — only `foreground` needs to be distinct per item.
4. Check the Console for its `[AdvancedElectronics]` confirmation line. It's
   safe to re-run: it reuses the existing `SurveyDroneItem` GameObject and
   icon file instead of duplicating them, so running it again after swapping
   in real art (by re-importing over the same PNG path) won't undo that.

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
