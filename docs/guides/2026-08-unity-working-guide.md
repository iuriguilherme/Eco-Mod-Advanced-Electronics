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
| The GameObject you build in the scene | `DroneDock` | No — and it **must not** carry the suffix. |
| The prefab asset saved from it | `DroneDockObject` | **Yes.** Must equal the server class exactly. |
| The server C# class | `DroneDockObject` | — |

**The scene object must not share the prefab's name.** This is not tidiness. "Build Current
Bundle" tags the *scene* as the asset bundle, so the scene object ships **and** the container's
prefabs ship — two objects with one name. The client builds its object map with
`Dictionary.Add`, the second insert throws `ArgumentException` inside the `ReceiveModData`
coroutine, and that coroutine silently stops. Every player sits on **"Preparing your
citizen…"** forever with no error anywhere.

So the naming is the opposite of what it looks like: the *prefab* gets the suffix, the *scene
object* never does.

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

If you reorganise, **update the folder constants** near the top of
`Assets/Art/AdvancedElectronics/Editor/AdvancedElectronicsBuildTools.cs` — search that file for
`ArtFolder` and you will find all of them together:

```csharp
private const string ArtFolder      = "Assets/Art/AdvancedElectronics";
private const string PrefabFolder   = ArtFolder + "/Prefabs";
private const string IconFolder     = ArtFolder + "/Sprites/Icons";
private const string MaterialFolder = ArtFolder + "/Materials";
private const string DroneChassisModel      = ArtFolder + "/Sprites/HRVSTR/HRVSTR-01.fbx";
private const string DroneChassisController = ArtFolder + "/Animators/HRVSTR_Animator_Controller.controller";
```

A stale constant makes a finisher write a stray copy at the old path, and the two drift apart
with nothing to warn you.

---

## The commands

All under **Eco Tools > Advanced Electronics**.

| Command | Does |
|---|---|
| **Finish All Drone Prefabs** | Builds `SurveyDroneObject`, `MiningDroneObject`, `HarvestDroneObject` from the shared HRVSTR chassis. Adding a drone is one line in `SharedChassisDrones` plus a re-run — see below. |
| **Finish Dock Prefab** | Builds `DroneDockObject` from a scene object named `DroneDock`. |
| **Finish Assembly Prefab** | Builds `AdvancedElectronicsAssemblyObject`. |
| **Finish All Item Icons** | Generates a placeholder PNG per entry in `ItemIcons`. |
| **Report Duplicate Bundle Object Names** | Lists names appearing twice in the bundle. Run before every build. |
| **Disable Mod Object Roots** | Fixes invisible in-game objects. See below. |

There is deliberately **no** standalone drone finisher. It used to build the survey drone from
a hand-made capsule; now that the survey drone is on the shared chassis, running it would
overwrite the chassis prefab and drop its animation states.

### Adding a drone

`SharedChassisDrones` lives in the same file,
`Assets/Art/AdvancedElectronics/Editor/AdvancedElectronicsBuildTools.cs` — search for
`SharedChassisDrones`:

```csharp
private static readonly string[] SharedChassisDrones =
{
    "SurveyDroneObject",
    "MiningDroneObject",
    "HarvestDroneObject",
};
```

Add the **server class name**, with the `Object` suffix, then re-run **Finish All Drone
Prefabs**. Each entry becomes its own prefab asset named for its class, because the filename is
the binding; the mesh, materials and controller underneath are shared by GUID, so the bundle
carries one copy however many drones list themselves here.

Leaving a drone out of this table is not neutral: the finisher is what stamps each prefab's
declared animation-state list, so an absent drone keeps whatever names it was last built with
and animates nothing.

### What the finisher does to your scene object

Worth knowing before you run it, because one branch deletes things:

- **Drones** (`sourceModel` is set): the tool instantiates the chassis if it cannot find a
  scene object of that name, saves the prefab, then **destroys the scene object** — deliberately,
  since a same-named scene object would collide with the prefab in the bundle. If you
  hand-authored a scene object named `SurveyDroneObject`, the tool finds and destroys *that*.
  `Undo` brings it back, but do not build custom work on an object whose name matches a prefab.
- **Dock**: looks for a scene object named `DroneDock` and errors out if it is missing. It never
  deletes it, because it did not create it.
- **Assembly**: passes the same name for scene object and prefab, so it saves and then logs the
  duplicate-name error. Rename or delete the scene object; the prefab is what ships.

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
- **Client hangs at "Preparing your citizen…"** — duplicate object names in the bundle. Almost
  always a scene object sharing a name with a prefab. Run **Report Duplicate Bundle Object
  Names**; the fix is to rename or delete the scene object, never the prefab.

`docs/solutions/` is organised by category and holds the full write-up for each of these.
