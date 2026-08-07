# Unity Working Guide

Supersedes `docs/guides/2026-07-survey-drone-unity-prefab-guide.md`, which describes a folder
layout and a set of menu commands that no longer exist, and which gets the prefab naming
wrong (see "The two names" below — that error cost a session).

Written for a human at the Editor. Everything here is something an agent cannot do for you:
Unity is not scriptable from outside, so these steps are yours.

---

## The two names

**This is the rule that breaks silently, so it goes first.**

Every world object has *two* names, and they are not the same:

| Thing | Named | Does it bind? |
|---|---|---|
| The GameObject you build in the scene | `DroneDock` | No. Call it anything. |
| The prefab asset saved from it | `DroneDockObject` | **Yes.** Must equal the server class exactly. |
| The server C# class | `DroneDockObject` | — |

The suffix is not decoration. The ModKit's own tool adds it for you —
`Assets/EcoModKit/Scripts/Editor/WorldObjectSetup.cs`:

```csharp
cleanName = CleanName(prefab.name) + "Object";
currentPrefabPath = fbxPath + "/" + cleanName + ".prefab";
```

So a source object named `DroneDock` becomes `DroneDockObject.prefab`. Our own finishers pass
both names explicitly for the same reason: `FinishPrefab("DroneDockObject", "DroneDock")` —
target first, scene source second.

**A prefab named `DroneDock.prefab` binds to nothing.** The server loads and behaves
correctly, the object renders as a missing-model placeholder, and no log on either side says
why. Check it with:

```bash
bash scripts/validate-name-match.sh    # expect: PASS
```

Items work the same way but bind through the **scene object's** name, not the sprite's — a
correctly named PNG next to a wrongly named GameObject fails exactly like any other mismatch.

---

## Where things live

```
Assets/Art/AdvancedElectronics/
  Animators/     HRVSTR_Animator_Controller.controller
  Materials/     *.mat
  Prefabs/       <ServerClassName>Object.prefab      <- the binding names
  Scenes/        AdvancedElectronicsScene.unity
  Sprites/
    Icons/       <ItemClassName>_icon.png
    HRVSTR/      HRVSTR-01.fbx + its textures
  Editor/        AdvancedElectronicsBuildTools.cs    <- every command below
```

Moving assets between these folders is safe — paths carry no binding. Renaming them is not.

If you reorganise, **update the folder constants** at the top of
`AdvancedElectronicsBuildTools.cs` (`PrefabFolder`, `IconFolder`, `MaterialFolder`,
`DroneChassisModel`, `DroneChassisController`). A stale constant makes a finisher write a
stray copy at the old path, and the two drift apart with nothing to warn you.

---

## The commands

All under **Eco Tools > Advanced Electronics**.

| Command | Does |
|---|---|
| **Finish All Drone Prefabs** | Builds `SurveyDroneObject`, `MiningDroneObject`, `HarvestDroneObject` from the shared HRVSTR chassis. Adds a drone = one line in `SharedChassisDrones` plus a re-run. |
| **Finish Dock Prefab** | Builds `DroneDockObject` from a scene object named `DroneDock`. |
| **Finish Assembly Prefab** | Builds `AdvancedElectronicsAssemblyObject`. |
| **Finish All Item Icons** | Generates a placeholder PNG per entry in `ItemIcons`. |
| **Report Duplicate Bundle Object Names** | Lists names appearing twice in the bundle. Run before every build. |
| **Disable Mod Object Roots** | Fixes invisible in-game objects. See below. |

There is deliberately **no** standalone drone finisher. It used to build the survey drone from
a hand-made capsule; now that the survey drone is on the shared chassis, running it would
overwrite the chassis prefab and drop its animation states.

---

## Normal working order

1. **Make your changes** — mesh, materials, animator, scene objects.

2. **Run the finishers** for whatever you touched. They are idempotent; re-running only does
   new work.

3. **Run Finish All Drone Prefabs a second time.** The first run logs any corrected
   `WorldObject.size`; the second should log nothing. A second run that still reports changes
   means something is recomputing differently each pass — stop and look.

4. **Check the prefab container.** This is the list that decides what actually ships, and it
   references prefabs by GUID, not by name — so it can point at the wrong asset while every
   filename looks right:

   ```bash
   grep -A 10 '^  Prefabs:' Assets/Art/AdvancedElectronics/Scenes/AdvancedElectronicsScene.unity |
     grep -o 'guid: [a-f0-9]*' | awk '{print $2}' |
     while read g; do
       printf '%s -> %s\n' "$g" "$(grep -rl "$g" Assets --include=*.meta | head -1)"
     done
   ```

   Anything resolving to an `Old*` file, or to nothing, ships the wrong asset or no asset.
   Re-drag the correct prefabs into the container in the Inspector. Full background:
   `docs/solutions/logic-errors/moving-a-prefab-can-hand-its-guid-to-a-backup-copy.md`.

5. **Report Duplicate Bundle Object Names** — a duplicate crashes the client at
   "Preparing your citizen..." with no error, because the bundle loader adds names to a
   dictionary and the throw happens inside a coroutine that then silently stops.

6. **Save the scene.** The finishers say so in the Console for a reason: an unsaved scene
   builds a bundle from the last saved state.

7. **Build the bundle** — Eco Tools > Mod Kit.

8. **Validate before deploying:**

   ```bash
   bash scripts/validate-name-match.sh    # expect PASS
   ```

---

## Things that fail silently

Each of these produces a working build, a clean server log, and wrong behaviour in game.

**Prefab name ≠ server class name.** Missing-model placeholder. Caught by
`validate-name-match.sh`.

**Animation state name ≠ animator parameter name.** The object renders and moves and animates
nothing. The five names must match across three places that never check each other: the server
constants in `EcoServerMod/AdvancedElectronics.Navigation/DroneAnimationState.cs`, the array in
`Assets/Art/AdvancedElectronics/DroneAnimatorStates.cs`, and the controller's own parameters.
The finisher stamps the prefab from the relay's array, so those two stay in step
automatically — the controller is the one manual match left. Verify in the Inspector that each
drone prefab's `States` array reads exactly:

```
IsAtHomeDock  IsWorking  ModeMining  ModeHarvest  Operating
```

**A prefab shipped active.** The client holds one inactive copy and clones it per object. An
active template breaks the cloning: the object is located correctly by the server and nothing
is drawn, recovering only on area reload. Run **Disable Mod Object Roots**.

**A stale `WorldObject.size`.** It is the block footprint the placement ghost is drawn from.
The dock previewed a 2,500-block hologram for weeks because the value was derived once from a
Unity Plane and never recomputed. The finisher now re-derives it every run — which is why step
3's two-run check matters.

---

## In-game verification

Deploy the DLLs and the rebuilt bundle, restart, then walk the Acceptance Examples in the plan
under `docs/plans/`. For the current drone work that is AE1–AE5: assignment starts the spin-up,
arrival starts the work loop, the arm matches the drone, coming home stops everything, and the
placement ghost matches the dock's real footprint.

Test protocol steps live in `docs/protocols/`.

---

## When something is wrong

- **Object invisible, name label present** — the client could not build the view. Not a naming
  problem. See `docs/solutions/` for the unrendered-object cases.
- **Object is a placeholder capsule/cube** — name mismatch. Run the validator.
- **Object renders but never animates** — state-name mismatch. Check the three places above.
- **Change does not appear at all** — check the prefab container GUIDs (step 4) before
  suspecting the server.
- **Client hangs at "Preparing your citizen…"** — duplicate object names in the bundle.

`docs/solutions/` is organised by category and holds the full write-up for each of these.
