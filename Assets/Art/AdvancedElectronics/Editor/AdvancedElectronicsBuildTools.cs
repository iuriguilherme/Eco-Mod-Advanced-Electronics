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

    [MenuItem("Eco Tools/Advanced Electronics/Finish Dock Prefab")]
    public static void FinishDockPrefab() => FinishPrefab("DroneDock", isDock: true);

    [MenuItem("Eco Tools/Advanced Electronics/Finish Drone Prefab")]
    public static void FinishDronePrefab() => FinishPrefab("SurveyDrone", isDock: false);

    /// <summary>
    /// U10's item-icon step (2b in the guide), fully scripted: instantiates
    /// Assets/EcoModKit/Prefabs/ItemTemplate.prefab under the scene's "Items"
    /// root, unpacks it completely (same effect as the README's manual
    /// drag-and-unpack steps), renames it to the exact server Item class name
    /// (SurveyDroneItem), and assigns a generated solid-color 64x64 PNG as its
    /// ItemTemplate.foreground Image sprite -- no dragging a sprite asset into
    /// an Inspector field required. Re-running this command is safe: it finds
    /// and reuses an existing 'SurveyDroneItem' child instead of duplicating
    /// it, and reuses the generated icon file if one already exists.
    /// </summary>
    [MenuItem("Eco Tools/Advanced Electronics/Finish Item Icon")]
    public static void FinishItemIcon()
    {
        const string itemName = "SurveyDroneItem";
        const string itemsRootName = "Items";

        var itemsRoot = FindInLoadedScenes(itemsRootName);
        if (itemsRoot == null)
        {
            Debug.LogError($"[AdvancedElectronics] No GameObject named '{itemsRootName}' found in the open scene (searched inactive objects too). Open the scene with the mod's scene roots (Objects/Items/Emoji/BlockSets) first, or check it wasn't renamed/moved.");
            return;
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
                return;
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
            return;
        }

        var sprite = GetOrCreatePlaceholderIconSprite();
        itemTemplate.foreground.sprite = sprite;
        EditorUtility.SetDirty(itemTemplate.foreground);
        EditorUtility.SetDirty(go);

        AssetDatabase.SaveAssets();
        Debug.Log($"[AdvancedElectronics] '{go.name}' now has a placeholder foreground icon ({AssetDatabase.GetAssetPath(sprite)}). Swap in real art later by re-importing over that same PNG file, or by assigning a different Sprite to its ItemTemplate 'foreground' Image component.");
    }

    private static Sprite GetOrCreatePlaceholderIconSprite()
    {
        const string path = ArtFolder + "/SurveyDroneItem_icon.png";

        var existingSprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existingSprite != null)
            return existingSprite;

        EnsureArtFolder();

        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var fill = new Color(0.25f, 0.55f, 0.85f, 1f); // placeholder teal-blue; swap for real art later
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

    private static void FinishPrefab(string expectedName, bool isDock)
    {
        var go = Selection.activeGameObject;
        if (go == null || go.name != expectedName)
            go = FindInLoadedScenes(expectedName);

        if (go == null)
        {
            Debug.LogError($"[AdvancedElectronics] No GameObject named '{expectedName}' found in the open scene (searched inactive objects too), and nothing matching is selected. Open the scene containing it (or select it in the Hierarchy) first, then re-run this command.");
            return;
        }

        go.tag = "ModObject";

        var worldObject = go.GetComponent<WorldObject>();
        if (worldObject == null)
        {
            worldObject = go.AddComponent<WorldObject>();
            Debug.Log($"[AdvancedElectronics] Added missing WorldObject component to '{go.name}'.");
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

        EnsureArtFolder();

        var path = $"{ArtFolder}/{go.name}.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path, out var success);
        if (!success || prefab == null)
        {
            Debug.LogError($"[AdvancedElectronics] Failed to save '{go.name}' as a prefab at {path}. Check the Console above for the underlying Unity error.");
            return;
        }

        RegisterInModkitContainer(prefab);

        AssetDatabase.SaveAssets();
        Debug.Log($"[AdvancedElectronics] Saved and registered: {path}. The original scene GameObject '{go.name}' was left as-is (not deleted) -- delete it by hand if you want to avoid a duplicate loose copy sitting in the scene; only the prefab asset is what gets bundled.");
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
