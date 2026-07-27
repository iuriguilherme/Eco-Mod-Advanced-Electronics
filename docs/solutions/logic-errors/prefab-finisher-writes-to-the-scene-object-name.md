---
title: "The prefab finisher writes to the scene GameObject's name, silently forking a duplicate prefab after a rename"
date: 2026-07-27
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
related_components: [EcoServerMod/AdvancedElectronics]
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
reached the prefab assets and the server C# — but not the editor tool's hardcoded names:

```csharp
// Assets/Art/AdvancedElectronics/Editor/AdvancedElectronicsBuildTools.cs:32-36
[MenuItem("Eco Tools/Advanced Electronics/Finish Dock Prefab")]
public static void FinishDockPrefab() => FinishPrefab("DroneDock", isDock: true);

[MenuItem("Eco Tools/Advanced Electronics/Finish Drone Prefab")]
public static void FinishDronePrefab() => FinishPrefab("SurveyDrone", isDock: false);
```

`FinishPrefab` locates the scene object by that expected name (`AdvancedElectronicsBuildTools.cs:137`)
and then writes the asset to whatever that object is called:

```csharp
// AdvancedElectronicsBuildTools.cs:215
var path = $"{ArtFolder}/{go.name}.prefab";
```

So the output path tracks the *scene* name. A rename applied to assets and server code but not to the
scene objects or these constants leaves the tool quietly authoritative for the old name.

## Recovery

Verified on 2026-07-27 after triggering this. The scene changes live only in memory until saved, so
recovery is clean **if you do not save the scene**:

1. Delete the stray prefabs and their `.meta` files (they are untracked, so `git status` shows them
   as new — that is the quickest way to identify them).
2. Reload the scene from disk to discard the in-memory container re-registration. Going through the
   editor UI risks a modal save prompt; calling `OpenScene` directly discards without prompting:

   ```csharp
   var scene = EditorSceneManager.OpenScene("Assets/DroneScene.unity", OpenSceneMode.Single);
   result.Log("Reloaded {0}: isDirty={1}", scene.path, scene.isDirty);
   ```

3. Confirm the container points at the tracked assets again, and that the scene is no longer dirty.
   The container should list exactly `DroneDockObject.prefab` and `SurveyDroneObject.prefab`.
4. Run `./scripts/validate-name-match.sh` — it passes as long as every server `WorldObject`/`Item`
   type has a matching-named client asset.

**Do not save the scene before step 2.** Once saved, the wrong registrations are on disk and the
recovery becomes a manual container edit.

## Prevention

- **Do not run the prefab finishers against this scene** until the constants are updated. They are
  currently only correct for the pre-rename names.
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
- Note the size-derivation step in the same tool only writes `WorldObject.size` when it is currently
  zero (`AdvancedElectronicsBuildTools.cs:185`), so on already-populated prefabs re-running buys
  nothing — there is no reason to run these tools "just to be safe".

## Related

- `docs/solutions/runtime-errors/worldobject-zero-size-blocks-placement.md` — why the size step in
  this tool exists at all.
- `docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md` — the name-match
  contract that makes a wrong-named prefab a silent failure rather than a loud one.
