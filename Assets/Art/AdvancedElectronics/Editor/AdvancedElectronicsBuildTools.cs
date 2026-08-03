using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Keyboard-friendly finishing tools for U9/U10 (docs/guides/2026-07-survey-drone-unity-prefab-guide.md),
/// for anyone who prefers menu commands over dragging/dropping and clicking
/// through Inspector arrays. In Unity 6, these menu items are reachable
/// without the mouse via the "Search Anything" command palette (default
/// keybind is usually a hotkey near Ctrl+K / Cmd+K, or search the Edit menu
/// for "Search" if that binding differs on your machine) -- type the command
/// name below and press Enter.
///
/// Each command expects the named GameObject to already exist in the
/// currently-open scene (built by hand or otherwise) and only handles the
/// mechanical finishing steps that would otherwise need Inspector
/// point-and-click: setting the "ModObject" tag, ensuring the required
/// components exist, saving the GameObject as a prefab asset under
/// Assets/Art/AdvancedElectronics/, and registering that prefab into the
/// scene's ModkitPrefabContainer (found on the "Objects" root per
/// Assets/EcoModKit/Docs/README.md's scene-setup convention). It does not
/// create the GameObject/mesh/Canvas hierarchy itself -- see the guide for
/// what to build by hand first.
/// </summary>
public static class AdvancedElectronicsBuildTools
{
    private const string ArtFolder = "Assets/Art/AdvancedElectronics";

    /// <summary>
    /// The shared placeholder material every mod primitive renders with. Eco's client
    /// needs a curved-world shader; a primitive left on Unity's default HDRP material
    /// renders visibly wrong in game
    /// (docs/solutions/ui-bugs/modkit-prefab-materials-need-curved-shaders.md).
    /// </summary>
    private const string PlaceholderMaterial = ArtFolder + "/AdvancedElectronicsPlaceholder.mat";

    /// <summary>
    /// Every server Item type that needs a placeholder icon, with the flat colour its
    /// icon is filled with. The KEY IS THE SERVER CLASS NAME and it is what binds the
    /// asset to the server -- the scene GameObject created for each entry is named from
    /// this string and nothing else. Colours only need to be distinguishable from each
    /// other at inventory-thumbnail size; that is the entire point of a placeholder.
    ///
    /// Adding a new item is a one-line change here plus a re-run of "Finish All Item
    /// Icons" -- deliberately cheaper than the five hand-built copies this replaced.
    /// </summary>
    private static readonly (string TypeName, Color Fill)[] ItemIcons =
    {
        ("SurveyDroneItem",                      new Color(0.25f, 0.55f, 0.85f, 1f)), // teal-blue (pre-existing)
        ("DroneDockItem",                        new Color(0.40f, 0.45f, 0.50f, 1f)), // steel grey -- shipped without an icon; see below
        ("AdvancedElectronicsSkillBook",         new Color(0.45f, 0.20f, 0.55f, 1f)), // deep purple
        ("AdvancedElectronicsSkillScroll",       new Color(0.85f, 0.75f, 0.50f, 1f)), // parchment
        ("EngineeringResearchPaperPostModernItem", new Color(0.90f, 0.90f, 0.95f, 1f)), // near-white paper
        ("AdvancedElectronicsAssemblyItem",      new Color(0.85f, 0.50f, 0.15f, 1f)), // orange
        ("BatteryItem",                          new Color(0.20f, 0.70f, 0.35f, 1f)), // green
        ("AdvancedElectronicsUpgradeItem",       new Color(0.80f, 0.20f, 0.30f, 1f)), // red -- plugin module
        ("HarvestDroneItem",                     new Color(0.90f, 0.65f, 0.15f, 1f)), // amber -- distinct from SurveyDrone's teal-blue
    };

    /// <summary>
    /// The shared drone chassis: one model and one controller behind EVERY drone type.
    ///
    /// HRVSTR-01 was modelled and animated as a multi-purpose machine, so survey, harvest,
    /// mining and whatever comes next are the same physical robot doing different jobs --
    /// not different robots. Its controller's parameters are named for activities
    /// (Drone_Mining, Drone_Harvest) rather than for any one drone type, which is what
    /// makes that sharing work.
    /// </summary>
    private const string DroneChassisModel      = ArtFolder + "/HRVSTR-01.fbx";
    private const string DroneChassisController = ArtFolder + "/HRVSTR_Animator_Controller.controller";

    /// <summary>
    /// Every server WorldObject class that renders as the shared chassis. ADDING A DRONE IS
    /// ONE LINE HERE plus a re-run of "Finish All Drone Prefabs" -- the same deliberately
    /// cheap shape as <see cref="ItemIcons"/>.
    ///
    /// Each entry becomes its OWN prefab asset named exactly for its server class, because
    /// the filename is the binding (Assets/EcoModKit/Docs/README.md) -- there is no way to
    /// point three server classes at one prefab file. What is actually shared is everything
    /// underneath: mesh, materials, textures and controller are referenced by GUID, so the
    /// bundle carries one copy no matter how many drones list themselves here.
    ///
    /// SurveyDroneObject is deliberately ABSENT. It ships today as a hand-built capsule and
    /// is live on deployed servers; re-running this over it would silently replace shipped
    /// art. Add it when you actually want the survey drone to become the HRVSTR chassis.
    /// </summary>
    private static readonly string[] SharedChassisDrones =
    {
        "HarvestDroneObject",
    };

    // Prefab finishers. The FIRST string is the server WorldObject class name and is what
    // the prefab file is named -- that filename is the binding, and it comes from this
    // parameter alone, never from whatever happens to be selected in the scene.
    //
    // The SECOND string is the scene GameObject to build from, which is a genuinely
    // separate concern: the two existing objects are called "DroneDock" and "SurveyDrone"
    // in the hierarchy while their prefabs are DroneDockObject.prefab and
    // SurveyDroneObject.prefab. Collapsing these into one parameter reads tidier and
    // silently stops finding the scene objects. New content should pass the same string
    // twice, as the assembly does, so there is nothing to keep in sync.
    [MenuItem("Eco Tools/Advanced Electronics/Finish Dock Prefab")]
    public static void FinishDockPrefab() => FinishPrefab("DroneDockObject", "DroneDock", isDock: true);

    [MenuItem("Eco Tools/Advanced Electronics/Finish Drone Prefab")]
    public static void FinishDronePrefab() => FinishPrefab("SurveyDroneObject", "SurveyDrone", isDock: false);

    [MenuItem("Eco Tools/Advanced Electronics/Finish Assembly Prefab")]
    public static void FinishAssemblyPrefab() =>
        FinishPrefab("AdvancedElectronicsAssemblyObject", "AdvancedElectronicsAssemblyObject", isDock: false);

    /// <summary>
    /// Builds one prefab per entry in <see cref="SharedChassisDrones"/>, each from the same
    /// chassis model and controller.
    ///
    /// The only finishers whose source object does not have to exist in the scene first: a
    /// drone is a real imported model rather than a hand-assembled primitive, so there is
    /// nothing for an author to build by hand and the command instantiates the chassis
    /// itself. Everything past that is the shared <see cref="FinishPrefab"/> path.
    ///
    /// Idempotent: a re-run reuses the scene GameObject and prefab a previous run left
    /// behind, so adding a drone to the table and re-running only does the new work.
    /// </summary>
    [MenuItem("Eco Tools/Advanced Electronics/Finish All Drone Prefabs")]
    public static void FinishAllDronePrefabs()
    {
        foreach (var typeName in SharedChassisDrones)
            FinishPrefab(typeName, typeName, isDock: false,
                         sourceModel: DroneChassisModel,
                         animatorController: DroneChassisController);

        Debug.Log($"[AdvancedElectronics] Finished {SharedChassisDrones.Length} drone prefab(s) on the shared chassis. " +
                  "SAVE THE SCENE, then rebuild the bundle (Eco Tools > Mod Kit).");
    }

    /// <summary>
    /// Runs the icon finisher for every entry in <see cref="ItemIcons"/>. Idempotent:
    /// each entry reuses an existing scene GameObject and an existing PNG when present,
    /// so re-running after adding one row only does the new work.
    /// </summary>
    [MenuItem("Eco Tools/Advanced Electronics/Finish All Item Icons")]
    public static void FinishAllItemIcons()
    {
        var done = 0;
        foreach (var (typeName, fill) in ItemIcons)
            if (FinishItemIcon(typeName, fill)) done++;

        Debug.Log($"[AdvancedElectronics] Finished {done}/{ItemIcons.Length} item icons. " +
                  "SAVE THE SCENE, then rebuild the bundle (Eco Tools > Mod Kit).");
    }

    /// <summary>
    /// Disables the root GameObject of every bundled mod object, in both the prefab assets and
    /// the scene. Run this once, then rebuild the bundle.
    ///
    /// The Eco client requires bundle objects to ship DISABLED: it keeps the bundled object as an
    /// inactive TEMPLATE and instantiates an enabled copy per world object. Ours shipped enabled,
    /// and the client says so on every load:
    ///
    ///     Loaded objects should start as DISABLED, but object "DroneDock" in bundle
    ///     "...AdvancedElectronics.unity3d" is not. This can cause many problems, you should
    ///     disable the object by default.
    ///
    /// The observed symptom is a placed Drone Dock that is invisible and non-interactable until
    /// the player leaves the area and returns, at which point it (and any drones) appear. The
    /// tooltip lists the object the whole time, so the server has it and only the client's copy is
    /// wrong -- which is what an active template rather than a per-instance clone looks like.
    /// </summary>
    [MenuItem("Eco Tools/Advanced Electronics/Disable Mod Object Roots (fixes invisible objects)")]
    public static void DisableModObjectRoots()
    {
        var fixedCount = 0;

        // 1. The prefab assets that get bundled.
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { ArtFolder }))
        {
            var path   = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || !prefab.activeSelf) continue;

            var instance = PrefabUtility.LoadPrefabContents(path);
            instance.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(instance, path);
            PrefabUtility.UnloadPrefabContents(instance);
            fixedCount++;
            Debug.Log($"[AdvancedElectronics] Disabled prefab root: {path}");
        }

        // 2. The scene GameObjects the finishers work from, which are what the bundle build reads
        //    when they are registered on a scene root's ModkitPrefabContainer.
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (!child.gameObject.CompareTag("ModObject") || !child.gameObject.activeSelf) continue;
                child.gameObject.SetActive(false);
                fixedCount++;
                Debug.Log($"[AdvancedElectronics] Disabled scene ModObject: {child.name}");
            }
        }

        AssetDatabase.SaveAssets();
        if (fixedCount > 0) EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        Debug.Log(fixedCount == 0
            ? "[AdvancedElectronics] Nothing to do -- every mod object root was already disabled."
            : $"[AdvancedElectronics] Disabled {fixedCount} root(s). SAVE THE SCENE, then rebuild the bundle " +
              "(Eco Tools > Mod Kit) and copy AdvancedElectronics.unity3d to the server's mod folder.");
    }

    /// <summary>
    /// U10's item-icon step (2b in the guide), fully scripted: instantiates
    /// Assets/EcoModKit/Prefabs/ItemTemplate.prefab under the scene's "Items"
    /// root, unpacks it completely (same effect as the README's manual
    /// drag-and-unpack steps), renames it to <paramref name="itemName"/> -- which must
    /// be the exact server Item class name -- and assigns a generated solid-colour
    /// 64x64 PNG as its ItemTemplate.foreground Image sprite. No dragging a sprite
    /// asset into an Inspector field required.
    ///
    /// THE NAME-MATCHED ARTIFACT IS THIS SCENE GameObject, NOT THE PNG. Items are never
    /// saved as their own prefab files -- per the ModKit's item flow they are unpacked
    /// GameObjects living inside the scene under "Items", and that GameObject's name is
    /// the only thing the server binds to. The PNG filename carries no binding at all;
    /// getting it right while the GameObject name is wrong is a silent missing-icon
    /// failure that looks purely cosmetic.
    ///
    /// Re-running is safe: it reuses an existing child with that name instead of
    /// duplicating it, and reuses the generated icon file if one already exists.
    /// </summary>
    /// <returns>True if the item now has an icon; false if it could not be finished.</returns>
    public static bool FinishItemIcon(string itemName, Color fill)
    {
        const string itemsRootName = "Items";

        var itemsRoot = FindInLoadedScenes(itemsRootName);
        if (itemsRoot == null)
        {
            Debug.LogError($"[AdvancedElectronics] No GameObject named '{itemsRootName}' found in the open scene (searched inactive objects too). Open the scene with the mod's scene roots (Objects/Items/Emoji/BlockSets) first, or check it wasn't renamed/moved.");
            return false;
        }

        GameObject go;
        var existing = itemsRoot.transform.Find(itemName);
        if (existing != null)
        {
            go = existing.gameObject;
            Debug.Log($"[AdvancedElectronics] Found existing '{itemName}' under '{itemsRootName}' -- reusing it instead of creating a new one.");
        }
        else
        {
            var templatePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoModKit/Prefabs/ItemTemplate.prefab");
            if (templatePrefab == null)
            {
                Debug.LogError("[AdvancedElectronics] Could not load Assets/EcoModKit/Prefabs/ItemTemplate.prefab.");
                return false;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(templatePrefab, itemsRoot.transform);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = itemName;
            go = instance;
            Debug.Log($"[AdvancedElectronics] Instantiated and unpacked ItemTemplate as '{itemName}' under '{itemsRootName}'.");
        }

        var itemTemplate = go.GetComponent<ItemTemplate>();
        if (itemTemplate == null || itemTemplate.foreground == null)
        {
            Debug.LogError($"[AdvancedElectronics] '{go.name}' doesn't look like an unpacked ItemTemplate (missing the ItemTemplate component, or its 'foreground' Image reference is unset). Delete it and re-run this command to rebuild it from the template.");
            return false;
        }

        var sprite = GetOrCreatePlaceholderIconSprite(itemName, fill);
        itemTemplate.foreground.sprite = sprite;
        EditorUtility.SetDirty(itemTemplate.foreground);
        EditorUtility.SetDirty(go);

        AssetDatabase.SaveAssets();
        Debug.Log($"[AdvancedElectronics] '{go.name}' now has a placeholder foreground icon ({AssetDatabase.GetAssetPath(sprite)}). Swap in real art later by re-importing over that same PNG file, or by assigning a different Sprite to its ItemTemplate 'foreground' Image component.");
        return true;
    }

    /// <summary>
    /// Generates (or reuses) the flat-colour placeholder PNG for one item. The filename
    /// is derived from the server type name purely so a human can tell the files apart
    /// in the project window -- nothing binds to it. See <see cref="FinishItemIcon"/>.
    /// </summary>
    private static Sprite GetOrCreatePlaceholderIconSprite(string itemName, Color fill)
    {
        var path = $"{ArtFolder}/{itemName}_icon.png";

        var existingSprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existingSprite != null)
            return existingSprite;

        EnsureArtFolder();

        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = fill;
        texture.SetPixels(pixels);
        texture.Apply();

        var pngBytes = texture.EncodeToPNG();
        Object.DestroyImmediate(texture);
        System.IO.File.WriteAllBytes(path, pngBytes);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    /// <param name="typeName">
    /// The exact server WorldObject class name. The prefab path is built from this
    /// parameter and nothing else -- deriving the filename from <c>go.name</c> is what
    /// previously wrote a duplicate prefab under the wrong name
    /// (docs/solutions/logic-errors/prefab-finisher-writes-to-the-scene-object-name.md).
    /// </param>
    /// <param name="sceneObjectName">
    /// The GameObject in the open scene to build the prefab from. Usually identical to
    /// <paramref name="typeName"/>, but the pre-existing dock and drone are named without
    /// the "Object" suffix in the hierarchy, so the two cannot be assumed equal.
    /// </param>
    /// <param name="sourceModel">
    /// Asset path of an imported model to instantiate when no scene GameObject named
    /// <paramref name="sceneObjectName"/> exists yet, or null to require one (the
    /// original behaviour, correct for the hand-assembled primitives).
    ///
    /// Passing a model ALSO suppresses the placeholder-material substitution below.
    /// The two travel together on purpose rather than as separate flags: the
    /// substitution exists because a Unity primitive ships on a default HDRP material
    /// that renders wrong under Eco's curved world, and an imported model always
    /// brings its own authored materials instead. Rather than trust that, the shaders
    /// are checked against the placeholder's and any mismatch is reported.
    /// </param>
    /// <param name="animatorController">
    /// Asset path of an AnimatorController to drive the object's bool states, or null
    /// for a static object. Non-null also attaches <c>DroneAnimatorStates</c>, which is
    /// the only thing that turns server state pushes into Animator parameter writes.
    /// </param>
    private static void FinishPrefab(string typeName, string sceneObjectName, bool isDock,
                                     string sourceModel = null, string animatorController = null)
    {
        var go = Selection.activeGameObject;
        if (go == null || go.name != sceneObjectName)
            go = FindInLoadedScenes(sceneObjectName);

        if (go == null && sourceModel != null)
            go = InstantiateSourceModel(sourceModel, sceneObjectName);

        if (go == null)
        {
            Debug.LogError($"[AdvancedElectronics] No GameObject named '{sceneObjectName}' found in the open scene (searched inactive objects too), and nothing matching is selected. Open the scene containing it (or select it in the Hierarchy) first, then re-run this command.");
            return;
        }

        go.tag = "ModObject";

        if (sourceModel != null)
        {
            VerifyModelShaders(go);
        }
        else
        {
            // KTD4: put every child renderer on the shared placeholder material. Assign
            // through sharedMaterial against the AssetDatabase asset -- assigning through
            // renderer.material forks a scene-local "(Instance)" copy that gets serialized
            // into the scene, ships duplicated in the bundle, and stops tracking the .mat
            // asset, so later edits to the placeholder silently miss this object.
            var placeholder = AssetDatabase.LoadAssetAtPath<Material>(PlaceholderMaterial);
            if (placeholder == null)
            {
                Debug.LogWarning($"[AdvancedElectronics] {PlaceholderMaterial} not found -- '{go.name}' keeps whatever material it has. Eco needs a curved-world shader, so a default HDRP material will render wrong in game.");
            }
            else
            {
                foreach (var renderer in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (renderer.sharedMaterial == placeholder) continue;
                    renderer.sharedMaterial = placeholder;
                    EditorUtility.SetDirty(renderer);
                    Debug.Log($"[AdvancedElectronics] Set '{renderer.name}' to the shared placeholder material.");
                }
            }
        }

        var worldObject = go.GetComponent<WorldObject>();
        if (worldObject == null)
        {
            worldObject = go.AddComponent<WorldObject>();
            Debug.Log($"[AdvancedElectronics] Added missing WorldObject component to '{go.name}'.");
        }

        // The ModKit's own World Object Setup tool
        // (Assets/EcoModKit/Scripts/Editor/WorldObjectSetup.cs) always gives a
        // WorldObject prefab a HighlightableObject and a root BoxCollider that
        // encapsulates all child renderers -- the collider is what the client's
        // placement-ghost/interaction raycasts hit. Earlier versions of this command
        // skipped both, which contributed to the dock being unusable in-game.
        if (go.GetComponent<HighlightableObject>() == null)
        {
            go.AddComponent<HighlightableObject>();
            Debug.Log($"[AdvancedElectronics] Added missing HighlightableObject to '{go.name}'.");
        }

        if (go.GetComponent<BoxCollider>() == null)
        {
            var boxCollider = go.AddComponent<BoxCollider>();
            var bounds = new Bounds(go.transform.position, Vector3.zero);
            foreach (var renderer in go.GetComponentsInChildren<Renderer>())
                bounds.Encapsulate(renderer.bounds);
            boxCollider.center = bounds.center - go.transform.position;
            boxCollider.size = bounds.size;
            Debug.Log($"[AdvancedElectronics] Added encapsulating BoxCollider to '{go.name}' (size {boxCollider.size}).");
        }

        // WorldObject.size is the object's block footprint. The client reads it to build
        // the placement-preview occupancy cells (WorldObjectPlacementPreviewer iterates
        // size.ConvertI().XYZIter()); a zero size yields zero cells and a degenerate,
        // seatless ghost that never validates against the ground -- the object then
        // cannot be placed with no server-side error. Neither this tool nor the ModKit's
        // WorldObjectSetup used to set it, so it stayed at Unity's default of zero.
        // Derive it from the encapsulating renderer bounds, ceil to whole blocks, min 1.
        if (worldObject.size == Vector3.zero)
        {
            var sizeBounds = new Bounds(go.transform.position, Vector3.zero);
            foreach (var renderer in go.GetComponentsInChildren<Renderer>())
                sizeBounds.Encapsulate(renderer.bounds);
            worldObject.size = new Vector3(
                Mathf.Max(1, Mathf.CeilToInt(sizeBounds.size.x)),
                Mathf.Max(1, Mathf.CeilToInt(sizeBounds.size.y)),
                Mathf.Max(1, Mathf.CeilToInt(sizeBounds.size.z)));
            Debug.Log($"[AdvancedElectronics] Set WorldObject.size on '{go.name}' to {worldObject.size} (block footprint).");
        }

        if (isDock)
        {
            var display = go.GetComponent<DockReadoutDisplay>();
            if (display == null)
            {
                display = go.AddComponent<DockReadoutDisplay>();
                Debug.Log($"[AdvancedElectronics] Added missing DockReadoutDisplay component to '{go.name}'.");
            }

            DockReadoutDisplay.EnsureStateArrays(worldObject);

            var tmp = go.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (tmp == null)
                Debug.LogWarning($"[AdvancedElectronics] No TMP_Text found under '{go.name}' -- the readout has nothing to write to yet. Add a Canvas (Render Mode: World Space) + a Text (TMP) child before this matters at runtime; the prefab will still save fine without it.");
        }

        if (animatorController != null)
            AttachAnimatorStates(go, worldObject, animatorController);

        EnsureArtFolder();

        var path = $"{ArtFolder}/{typeName}.prefab";

        // Save the prefab DISABLED. The client keeps a bundled object as an inactive template and
        // clones an enabled copy per world object; shipping it active makes placed objects
        // invisible until the area is re-streamed, and the client logs
        // "Loaded objects should start as DISABLED" on every load. Toggle around the save so the
        // scene object the author is looking at is left the way they had it.
        var wasActive = go.activeSelf;
        go.SetActive(false);
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path, out var success);
        go.SetActive(wasActive);
        if (!success || prefab == null)
        {
            Debug.LogError($"[AdvancedElectronics] Failed to save '{go.name}' as a prefab at {path}. Check the Console above for the underlying Unity error.");
            return;
        }

        RegisterInModkitContainer(prefab);

        // Remove the scene copy the tool itself created. THIS IS NOT TIDYING -- leaving it
        // crashes every client that loads the bundle.
        //
        // "Build Current Bundle" tags the SCENE as the asset bundle
        // (ModKitTools.cs: AssetImporter.GetAtPath(scene.path).SetAssetBundleNameAndVariant),
        // so the whole scene ships AND the ModkitPrefabContainer's prefabs ship as
        // dependencies. A scene GameObject named the same as its prefab is therefore two
        // objects with one name in the bundle, and the client builds its object map with
        // Dictionary.Add -- the second insert throws ArgumentException inside the
        // ReceiveModData coroutine, which then silently stops. The player sits on
        // "Preparing your citizen..." forever while the packet backlog grows without bound.
        //
        // The pre-existing dock and survey drone dodge this only because their scene objects
        // are named DroneDock/SurveyDrone while their prefabs are DroneDockObject/
        // SurveyDroneObject. That naming difference is load-bearing, not cosmetic.
        //
        // Only tool-created objects are destroyed: it built this one from the model and can
        // rebuild it on the next run, so nothing hand-authored is at risk. Undo-able anyway.
        if (sourceModel != null)
        {
            Undo.DestroyObjectImmediate(go);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[AdvancedElectronics] Removed the temporary scene copy of '{typeName}' -- the prefab is what ships, and a same-named scene object would collide with it in the bundle.");
        }
        else if (sceneObjectName == typeName)
        {
            Debug.LogError($"[AdvancedElectronics] The scene GameObject '{sceneObjectName}' has the SAME NAME as the prefab just saved, so the bundle will contain that name twice and every client will fail to finish loading with \"An item with the same key has already been added. Key: {typeName}\". Rename the scene object (the dock uses 'DroneDock' for 'DroneDockObject') or delete it -- the prefab is what ships.");
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[AdvancedElectronics] Saved and registered: {path}.");

        ReportDuplicateBundleNames();
    }

    /// <summary>
    /// Drops an imported model into the active scene under the name the finisher expects.
    ///
    /// The instance is UNPACKED COMPLETELY rather than left linked to the model asset. A
    /// linked instance would save as a prefab with the model nested inside it, which keeps
    /// mesh reimports propagating but puts a nested prefab through Eco's asset-bundle
    /// pipeline -- a path nothing in this project has exercised. Unpacking produces the
    /// same flat hierarchy as the hand-built dock and survey-drone prefabs, so the bundle
    /// sees nothing new. The cost is that re-importing the FBX no longer updates an
    /// already-built prefab: delete the scene object and re-run this command instead.
    /// </summary>
    private static GameObject InstantiateSourceModel(string modelPath, string sceneObjectName)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null)
        {
            Debug.LogError($"[AdvancedElectronics] Source model '{modelPath}' not found, so there is nothing to build '{sceneObjectName}' from. Check the file exists and that Unity has imported it.");
            return null;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        if (instance == null)
        {
            Debug.LogError($"[AdvancedElectronics] Unity refused to instantiate '{modelPath}'.");
            return null;
        }

        // PrefabUnpackMode.Completely, not OutermostRoot: an FBX instance nests its own
        // sub-prefabs, and unpacking only the outermost root would leave those linked.
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        instance.name = sceneObjectName;
        Undo.RegisterCreatedObjectUndo(instance, $"Instantiate {sceneObjectName}");
        EditorSceneManager.MarkSceneDirty(instance.scene);

        Debug.Log($"[AdvancedElectronics] No '{sceneObjectName}' in the scene, so instantiated one from {modelPath} and unpacked it.");
        return instance;
    }

    /// <summary>
    /// Reports any renderer material whose shader differs from the placeholder's. An
    /// imported model keeps its own authored materials, which is the point -- but Eco
    /// needs a curved-world shader and a model exported with a stock Standard/HDRP Lit
    /// material renders visibly wrong in game
    /// (docs/solutions/ui-bugs/modkit-prefab-materials-need-curved-shaders.md). This only
    /// warns; substituting the placeholder here would throw the art away.
    /// </summary>
    private static void VerifyModelShaders(GameObject go)
    {
        var placeholder = AssetDatabase.LoadAssetAtPath<Material>(PlaceholderMaterial);
        var expected = placeholder != null ? placeholder.shader : null;
        if (expected == null)
        {
            Debug.LogWarning($"[AdvancedElectronics] {PlaceholderMaterial} not found, so '{go.name}''s materials could not be checked against Eco's curved-world shader. Verify them by hand.");
            return;
        }

        var checkedCount = 0;
        foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null)
                {
                    Debug.LogWarning($"[AdvancedElectronics] '{renderer.name}' has an empty material slot -- that submesh renders magenta in game.");
                    continue;
                }

                checkedCount++;
                if (material.shader != expected)
                    Debug.LogWarning($"[AdvancedElectronics] '{renderer.name}' uses material '{material.name}' on shader '{material.shader.name}', not the curved-world shader '{expected.name}' that {PlaceholderMaterial} uses. Eco's world is curved in the vertex shader, so this will render straight and detach from the terrain. Re-author the material on the curved shader.");
            }
        }

        Debug.Log($"[AdvancedElectronics] Kept '{go.name}''s own materials (imported model) and checked {checkedCount} of them against the curved-world shader.");
    }

    /// <summary>
    /// Gives the object an Animator on <paramref name="controllerPath"/> plus the
    /// <c>DroneAnimatorStates</c> relay, and stamps the WorldObject's bool state names
    /// from that relay's own list.
    ///
    /// The relay is what makes any of this move: the server pushes named booleans via
    /// SetAnimatedState and the ModKit's WorldObject only re-broadcasts them as
    /// UnityEvents. Nothing in the ModKit talks to an Animator, so without the relay the
    /// controller sits on its default state forever and the failure is silent.
    /// </summary>
    private static void AttachAnimatorStates(GameObject go, WorldObject worldObject, string controllerPath)
    {
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
        if (controller == null)
        {
            Debug.LogError($"[AdvancedElectronics] Animator controller '{controllerPath}' not found -- '{go.name}' will render but never animate.");
            return;
        }

        // The FBX importer already puts an Animator on a rigged model's root; reuse it
        // rather than adding a second one, which would leave two competing state machines.
        //
        // Written as an explicit null check rather than `?? go.AddComponent<Animator>()`:
        // UnityEngine.Object overloads == to report a DESTROYED object as null, but ?? uses
        // real CLR null and bypasses that overload. It happens not to bite here (a missed
        // GetComponentInChildren returns true null), but the pattern is worth not copying.
        var animator = go.GetComponentInChildren<Animator>(true);
        if (animator == null) animator = go.AddComponent<Animator>();
        if (animator.runtimeAnimatorController != controller)
        {
            animator.runtimeAnimatorController = controller;
            EditorUtility.SetDirty(animator);
            Debug.Log($"[AdvancedElectronics] Set '{animator.name}''s controller to {controllerPath}.");
        }

        if (go.GetComponent<DroneAnimatorStates>() == null)
        {
            go.AddComponent<DroneAnimatorStates>();
            Debug.Log($"[AdvancedElectronics] Added DroneAnimatorStates to '{go.name}'.");
        }

        DroneAnimatorStates.EnsureStateArrays(worldObject);
        EditorUtility.SetDirty(worldObject);
    }

    /// <summary>
    /// Reports every object name that would appear more than once in the bundle.
    ///
    /// Worth a dedicated check because the client's failure gives you nothing to work with:
    /// the duplicate insert throws inside a coroutine, the coroutine stops, and the only
    /// visible symptom is a player stuck on "Preparing your citizen..." while the pending
    /// packet count climbs forever. Nothing says "duplicate", nothing says "bundle", and the
    /// server log stays clean because the server is fine. Catching it here turns a
    /// build-deploy-connect-wait-give-up loop into a line in the Console.
    ///
    /// Runs automatically after every finisher, and available on its own from the menu to
    /// check before a bundle build.
    /// </summary>
    [MenuItem("Eco Tools/Advanced Electronics/Report Duplicate Bundle Object Names")]
    public static void ReportDuplicateBundleNames()
    {
        // name -> where each occurrence came from, for a message that says what to delete.
        var occurrences = new Dictionary<string, List<string>>();

        void Record(string name, string origin)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (!occurrences.TryGetValue(name, out var origins))
                occurrences[name] = origins = new List<string>();
            origins.Add(origin);
        }

        for (var s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;

            foreach (var root in scene.GetRootGameObjects())
            {
                // Direct children of a scene root are the bundle's addressable entries --
                // "Items/SurveyDroneItem" in the client's own error wording.
                foreach (Transform child in root.transform)
                    Record(child.name, $"scene object {root.name}/{child.name}");

                // ModObject-tagged objects anywhere, which is how world objects are picked up.
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.CompareTag("ModObject") && t.parent != root.transform)
                        Record(t.name, $"scene ModObject {t.name}");
            }
        }

        var container = Object.FindFirstObjectByType<ModkitPrefabContainer>(FindObjectsInactive.Include);
        foreach (var prefab in container?.Prefabs ?? System.Array.Empty<GameObject>())
            if (prefab != null)
                Record(prefab.name, $"registered prefab {AssetDatabase.GetAssetPath(prefab)}");

        var clashes = occurrences.Where(pair => pair.Value.Count > 1).ToArray();
        if (clashes.Length == 0)
        {
            Debug.Log("[AdvancedElectronics] No duplicate bundle object names. Clients will be able to finish loading this bundle.");
            return;
        }

        foreach (var (name, origins) in clashes.Select(pair => (pair.Key, pair.Value)))
            Debug.LogError($"[AdvancedElectronics] '{name}' would appear {origins.Count} times in the bundle: {string.Join("; ", origins)}. Every client will hang on \"Preparing your citizen...\" with \"An item with the same key has already been added. Key: {name}\". Keep ONE -- normally the registered prefab -- and delete or rename the others.");
    }

    private static void EnsureArtFolder()
    {
        if (AssetDatabase.IsValidFolder(ArtFolder))
            return;

        if (!AssetDatabase.IsValidFolder("Assets/Art"))
            AssetDatabase.CreateFolder("Assets", "Art");
        AssetDatabase.CreateFolder("Assets/Art", "AdvancedElectronics");
    }

    /// <summary>
    /// GameObject.Find only searches ACTIVE objects and only in loaded scenes
    /// -- a root that's temporarily disabled (or a multi-scene setup) makes it
    /// silently return null even though the object exists. This walks every
    /// loaded scene's full hierarchy, including inactive GameObjects, so
    /// "not found" here actually means not found.
    /// </summary>
    private static GameObject FindInLoadedScenes(string name)
    {
        for (var s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;

            foreach (var root in scene.GetRootGameObjects())
            {
                var found = FindRecursive(root.transform, name);
                if (found != null) return found.gameObject;
            }
        }
        return null;
    }

    private static Transform FindRecursive(Transform t, string name)
    {
        if (t.name == name) return t;
        for (var i = 0; i < t.childCount; i++)
        {
            var found = FindRecursive(t.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    private static void RegisterInModkitContainer(GameObject prefab)
    {
        var container = Object.FindFirstObjectByType<ModkitPrefabContainer>(FindObjectsInactive.Include);
        if (container == null)
        {
            Debug.LogWarning("[AdvancedElectronics] No ModkitPrefabContainer found in the open scene (expected on the 'Objects' root per the ModKit's scene-setup convention) -- add this prefab to its Prefabs list by hand once the right scene is open.");
            return;
        }

        var existing = container.Prefabs ?? System.Array.Empty<GameObject>();
        if (existing.Contains(prefab))
        {
            Debug.Log($"[AdvancedElectronics] '{prefab.name}' is already registered in {container.name}'s ModkitPrefabContainer.");
            return;
        }

        container.Prefabs = existing.Append(prefab).ToArray();

        EditorUtility.SetDirty(container);
        EditorSceneManager.MarkSceneDirty(container.gameObject.scene);
        Debug.Log($"[AdvancedElectronics] Registered '{prefab.name}' in {container.name}'s ModkitPrefabContainer.");
    }
}
