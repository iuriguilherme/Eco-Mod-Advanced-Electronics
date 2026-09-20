---
title: "The prefab finisher writes to the scene GameObject's name, silently forking a duplicate prefab after a rename"
date: 2026-07-27
last_updated: 2026-09-15
category: logic-errors
module: AdvancedElectronics
problem_type: logic_error
component: tooling
severity: high
symptoms:
  - "Running Eco Tools > Advanced Electronics > Finish Dock Prefab creates DroneDock.prefab instead of updating the tracked DroneDockObject.prefab"
  - "Two prefabs per world object appear in Assets/Art/AdvancedElectronics — one tracked, one untracked"
  - "The scene's ModkitPrefabContainer silently re-points at the new wrong-named prefabs; the scene goes dirty with no visible error"
  - "Console reports success ('Saved and registered: ...') — nothing signals that the wrong asset was written"
root_cause: incomplete_setup
resolution_type: workflow_improvement
applies_when:
  - "Running the scripted prefab finishers in the Unity ModKit project"
  - "Renaming a WorldObject prefab asset to satisfy Eco's server-class name-match contract"
  - "A bundle builds cleanly but its objects render as missing-model placeholders in game"
tags: [eco-modding, unity, prefab, editor-tooling, name-match, modkit, rename]
related_components: [Assets/Art/AdvancedElectronics/Editor, Assets/Art/AdvancedElectronics/Prefabs]
---

# The prefab finisher writes to the scene GameObject's name, silently forking a duplicate prefab after a rename

## Problem

`AdvancedElectronicsBuildTools.FinishPrefab` derives the prefab asset path from the **scene
GameObject's name**, not from the tracked asset it is meant to update. The scene objects are still
named `DroneDock` / `SurveyDrone`, while the shipped prefabs were renamed to `DroneDockObject` /
`SurveyDroneObject` to satisfy Eco's name-match contract. Running the finishers therefore creates a
second, wrong-named prefab and re-registers *that* in the scene's `ModkitPrefabContainer`.

## Symptoms

- `Assets/Art/AdvancedElectronics/` gains `DroneDock.prefab` and `SurveyDrone.prefab` alongside the
  tracked `DroneDockObject.prefab` and `SurveyDroneObject.prefab`.
- The console reports success. There is no warning, because from the tool's point of view nothing
  went wrong:

  ```
  [AdvancedElectronics] Registered 'DroneDock' in Objects's ModkitPrefabContainer.
  [AdvancedElectronics] Saved and registered: Assets/Art/AdvancedElectronics/DroneDock.prefab.
  ```

- The scene becomes dirty. If saved and bundled, the bundle carries prefabs whose names no longer
  match the server's `WorldObject` classes — and Eco links client assets to server objects **by
  name**, so the objects would render as missing-model placeholders in game with no server error.

## Root cause

The world objects were renamed to the `XObject` form to match the server classes, and the rename
reached the prefab assets and the server C# — but not the editor tool's hardcoded names. Each finisher
was a menu item carrying the expected scene-object name as a literal; `FinishPrefab` located the scene
object by that name and then wrote the asset to whatever that object was called:

```csharp
// the shape at the time -- no longer present in the tree
var path = $"{ArtFolder}/{go.name}.prefab";
```

So the output path tracked the *scene* name. A rename applied to assets and server code but not to the
scene objects or these constants left the tool quietly authoritative for the old name.

**This root cause is fixed in the current tree**, by the very change this doc recommends below.
`FinishPrefab` now takes the target type name and the scene object name as two separate parameters
(`Assets/Art/AdvancedElectronics/Editor/AdvancedElectronicsBuildTools.cs:647-648`), and the output path
is built from the type name alone (`AdvancedElectronicsBuildTools.cs:765`). The source cites this doc by
path as the reason (`AdvancedElectronicsBuildTools.cs:618-622`). One standalone finisher is gone — the survey
drone's, because the drone moved onto the shared chassis and re-running it would have
overwritten that chassis with the old hand-built capsule. `FinishAllDronePrefabs`
(`AdvancedElectronicsBuildTools.cs:179-189`) covers all three drones instead. The dock's and the
assembly's standalone finishers remain (`:154-155` and `:163-165`) and are safe, because each
now passes its target type name and its scene object name as two separate arguments. What follows
is kept for the mechanism and the prevention rule, which still bind any future tool that infers an
identity from something other than its authoritative source.

## Recovery

Verified on 2026-07-27 after triggering this. The scene changes live only in memory until saved, so
recovery is clean **if you do not save the scene**:

1. Delete the stray prefabs and their `.meta` files (they are untracked, so `git status` shows them
   as new — that is the quickest way to identify them).
2. Reload the scene from disk to discard the in-memory container re-registration. Going through the
   editor UI risks a modal save prompt; calling `OpenScene` directly discards without prompting:

   ```csharp
   var scene = EditorSceneManager.OpenScene("Assets/Art/AdvancedElectronics/Scenes/AdvancedElectronicsScene.unity", OpenSceneMode.Single);
   result.Log("Reloaded {0}: isDirty={1}", scene.path, scene.isDirty);
   ```

3. Confirm the container points at the tracked assets again, and that the scene is no longer dirty.
   The container should list exactly `DroneDockObject.prefab` and `SurveyDroneObject.prefab`.
4. Run `./scripts/validate-name-match.sh` — it passes as long as every server `WorldObject`/`Item`
   type has a matching-named client asset.

**Do not save the scene before step 2.** Once saved, the wrong registrations are on disk and the
recovery becomes a manual container edit.

## Prevention

- **Check what a finisher derives its output path from before running it.** In this tree that
  question is already settled — each finisher is handed its target type name explicitly — so
  running them is safe. The rule survives the fix because it binds the next tool, not this one.
- **The real fix is to pass the target asset name explicitly** rather than inferring it from the
  scene object — the tool should know it is maintaining `DroneDockObject.prefab` regardless of what
  the scene object happens to be called. Renaming the scene objects to match would also work, but
  leaves the same trap for the next rename.
- **Treat "creates a new file" as a failure mode for any idempotent-looking tool.** A tool meant to
  *update* an artifact that instead *creates* one reports success either way. The signal to watch is
  `git status` showing untracked siblings of tracked assets, not the console.
- **When renaming an asset that a script references by name, grep for the old name across editor
  tooling**, not just source and assets. The rename here was otherwise complete; only the tool was
  missed, and the tool is the thing that regenerates the artifact.
- The size-derivation step in the same tool now re-derives `WorldObject.size` from the renderer
  bounds on every run (`AdvancedElectronicsBuildTools.cs:733-752`), having previously written it
  only when it was still zero. That earlier form was an initialization wearing a derivation's
  clothes: the dock's footprint was taken from a Plane primitive and survived at 50 x 1 x 50
  after the mesh became a platform-shaped cube, and re-running could not correct it because the
  value was no longer zero. Re-running after a mesh edit is now the right move rather than a
  no-op — see `docs/solutions/runtime-errors/worldobject-zero-size-blocks-placement.md`, which
  prescribes the same thing.

## Related

- `docs/solutions/runtime-errors/worldobject-zero-size-blocks-placement.md` — why the size step in
  this tool exists at all.
- `docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md` — the name-match
  contract that makes a wrong-named prefab a silent failure rather than a loud one.
