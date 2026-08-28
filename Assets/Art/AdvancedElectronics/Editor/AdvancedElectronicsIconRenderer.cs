using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Renders an item's icon from the 3D object it represents, which is how Eco makes its own.
///
/// Vanilla's UISpriteBaker does not slice art out of a sheet: it composes a GameObject in a
/// scene, points a camera at it and takes a "screenshot" into the baked atlas
/// (Client/Assets/Editor/EcoTools/UI/UISpriteBaker.cs -- see the RenderTexture at :505-520 and
/// the transparent-clear camera at :583-587). The baked sprite is then named from the
/// GameObject, never from the art file (:654).
///
/// A mod cannot add to that atlas, but it can do the same thing into its own bundle. For
/// anything the mod already models -- the drones, the dock, the assembly -- that produces real,
/// accurate artwork with no artist and no drawing: the icon IS the object the player places.
///
/// This is deliberately NOT the flat-fill placeholder generator in
/// AdvancedElectronicsBuildTools. A flat colour is worse than shipping no icon at all, because
/// the client's own missing-icon sprite is a competent drawing and costs nothing
/// (IconManager.GetIcon). Entries that can neither be rendered nor named against vanilla's
/// library are the only ones that need art commissioned.
///
/// See docs/solutions/architecture-patterns/mod-icons-reference-vanilla-art-by-name.md.
/// </summary>
public static class AdvancedElectronicsIconRenderer
{
    private const string PrefabFolder = "Assets/Art/AdvancedElectronics/Prefabs";

    /// <summary>
    /// Every server Item type whose icon is a render of a WorldObject prefab this mod already
    /// ships, and the prefab it renders.
    ///
    /// THE FIRST STRING IS THE SERVER CLASS NAME and is what the scene GameObject is named --
    /// that name is the whole binding. The second is only a file to load; nothing binds to it.
    /// The two differ by more than a suffix for the dock and the drones, which is exactly why
    /// they are two columns rather than one derived from the other.
    /// </summary>
    private static readonly (string ItemName, string PrefabName)[] RenderedIcons =
    {
        ("SurveyDroneItem",                 "SurveyDroneObject"),
        ("MiningDroneItem",                 "MiningDroneObject"),
        ("HarvestDroneItem",                "HarvestDroneObject"),
        ("DroneDockItem",                   "DroneDockObject"),
        ("AdvancedElectronicsAssemblyItem", "AdvancedElectronicsAssemblyObject"),
    };

    /// <summary>
    /// Camera angle. Eco's object icons read as a three-quarter view from slightly above, so the
    /// silhouette shows depth rather than a flat elevation. Tweak here rather than per entry --
    /// a consistent angle across the set is what makes the icons look like one family.
    /// </summary>
    private static readonly Vector3 ViewAngle = new Vector3(22.5f, 135f, 0f);

    /// <summary>Fraction of the frame the object fills. Below 1 leaves the icon a margin.</summary>
    private const float Fill = 0.82f;

    /// <summary>
    /// Where the throwaway render rig is built. Far enough from any authored content that an
    /// orthographic camera framed on the rig cannot catch scene geometry in the background.
    /// </summary>
    private static readonly Vector3 RigOrigin = new Vector3(0f, -10000f, 0f);

    [MenuItem("Eco Tools/Advanced Electronics/Render Object Icons")]
    public static void RenderObjectIcons() => RenderObjectIcons(force: false);

    /// <summary>
    /// The same pass, except an icon that already exists is re-rendered rather than left alone.
    /// Separate menu command rather than a flag for the same reason the placeholder force pass
    /// is: the Editor is reached in one grant and the ordering is followed as a script, so a
    /// step that cannot be found in a menu cannot be followed.
    /// </summary>
    [MenuItem("Eco Tools/Advanced Electronics/Render Object Icons (Force Re-render)")]
    public static void ForceRenderObjectIcons() => RenderObjectIcons(force: true);

    private static void RenderObjectIcons(bool force)
    {
        var rendered = new List<string>();
        var skipped  = new List<string>();
        var failed   = new List<string>();

        foreach (var (itemName, prefabName) in RenderedIcons)
        {
            var path = $"{AdvancedElectronicsBuildTools.IconOutputFolder}/{itemName}_icon.png";

            if (!force && AssetDatabase.LoadAssetAtPath<Sprite>(path) != null)
            {
                skipped.Add(itemName);
                continue;
            }

            if (RenderOne(itemName, prefabName, path)) rendered.Add(itemName);
            else                                      failed.Add(itemName);
        }

        var report = new System.Text.StringBuilder();
        report.Append($"[AdvancedElectronics] {(force ? "Render Object Icons (Force Re-render)" : "Render Object Icons")} over {RenderedIcons.Length} entr(ies).");
        report.Append(Summarise($"rendered at {AdvancedElectronicsBuildTools.IconSize}x{AdvancedElectronicsBuildTools.IconSize}", rendered));
        report.Append(Summarise("left alone -- an icon already exists; use the Force Re-render command to replace it", skipped));
        report.Append(Summarise("FAILED -- see the errors above", failed));
        report.Append(" SAVE THE SCENE, then rebuild the bundle (Eco Tools > Mod Kit).");

        if (failed.Count > 0) Debug.LogError(report.ToString());
        else                  Debug.Log(report.ToString());

        string Summarise(string label, List<string> names) =>
            names.Count == 0
                ? string.Empty
                : System.Environment.NewLine + $"  {label} ({names.Count}): {string.Join(", ", names)}";
    }

    private static bool RenderOne(string itemName, string prefabName, string path)
    {
        var prefabPath = $"{PrefabFolder}/{prefabName}.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[AdvancedElectronics] No prefab at {prefabPath} to render '{itemName}' from. Run 'Finish All Drone Prefabs' first, or fix the table row.");
            return false;
        }

        // The scene object has to exist before the sprite is assigned to it, and it is also the
        // thing the whole binding rests on, so build it first and bail before rendering anything
        // if it cannot be made.
        var itemTemplate = AdvancedElectronicsBuildTools.EnsureItemObject(itemName);
        if (itemTemplate == null) return false;

        var pixels = RenderPrefab(prefab, itemName);
        if (pixels == null) return false;

        AdvancedElectronicsBuildTools.EnsureIconFolder();
        System.IO.File.WriteAllBytes(path, pixels);

        // Written over any existing file rather than deleted and recreated: the scene reaches the
        // sprite through the GUID in the PNG's .meta sidecar, and deleting the pair re-mints that
        // GUID and dangles every reference to it.
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType      = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled    = false;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();

        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
        {
            Debug.LogError($"[AdvancedElectronics] Wrote {path} but could not load a Sprite back from it.");
            return false;
        }

        AdvancedElectronicsBuildTools.AssignIconSprite(itemTemplate, sprite);
        Debug.Log($"[AdvancedElectronics] '{itemName}' now draws a render of {prefabName} ({path}).");
        return true;
    }

    /// <summary>
    /// Builds a throwaway camera-and-light rig far below the scene, frames the prefab in it, and
    /// returns the PNG bytes. Returns null and logs why when the render came back empty.
    /// </summary>
    private static byte[] RenderPrefab(GameObject prefab, string itemName)
    {
        var size = AdvancedElectronicsBuildTools.IconSize;

        GameObject rig = null;
        RenderTexture renderTexture = null;
        var previousActive = RenderTexture.active;

        try
        {
            rig = new GameObject($"__IconRig_{itemName}");
            rig.transform.position = RigOrigin;
            rig.hideFlags = HideFlags.HideAndDontSave;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, rig.transform);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            // Bundled mod objects ship DISABLED on purpose (the client keeps them as inactive
            // templates), so a straight instantiate renders nothing at all.
            instance.SetActive(true);
            foreach (var child in instance.GetComponentsInChildren<Transform>(true))
                child.gameObject.SetActive(true);

            var renderers = instance.GetComponentsInChildren<Renderer>(false)
                                    .Where(r => r.enabled && r.bounds.size != Vector3.zero)
                                    .ToArray();
            if (renderers.Length == 0)
            {
                Debug.LogError($"[AdvancedElectronics] '{itemName}': {prefab.name} has no enabled Renderer with non-zero bounds, so there is nothing to photograph.");
                return null;
            }

            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);

            var cameraObject = new GameObject("__IconCamera");
            cameraObject.transform.SetParent(rig.transform, false);
            cameraObject.transform.rotation = Quaternion.Euler(ViewAngle);

            var extent = bounds.extents.magnitude;
            cameraObject.transform.position = bounds.center - cameraObject.transform.forward * (extent * 4f);

            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic     = true;
            camera.orthographicSize = extent / Fill;
            camera.nearClipPlane    = 0.01f;
            camera.farClipPlane     = extent * 10f;
            camera.clearFlags       = CameraClearFlags.SolidColor;
            camera.backgroundColor  = new Color(0f, 0f, 0f, 0f);
            camera.allowHDR         = false;
            camera.allowMSAA        = false;

            // The rig carries its own key and fill so the render does not depend on whatever
            // lighting the currently-open scene happens to have.
            AddLight(cameraObject.transform, new Vector3(35f, -30f, 0f), 1.4f);
            AddLight(cameraObject.transform, new Vector3(-15f, 140f, 0f), 0.6f);

            renderTexture = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4,
                useMipMap    = false,
                filterMode   = FilterMode.Bilinear,
            };

            camera.targetTexture = renderTexture;
            camera.Render();

            RenderTexture.active = renderTexture;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            texture.Apply();
            camera.targetTexture = null;

            if (!LooksLikeAnIcon(texture, itemName, prefab.name))
            {
                Object.DestroyImmediate(texture);
                return null;
            }

            var png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);
            return png;
        }
        finally
        {
            RenderTexture.active = previousActive;
            if (renderTexture != null) Object.DestroyImmediate(renderTexture);
            if (rig != null) Object.DestroyImmediate(rig);
        }
    }

    private static void AddLight(Transform parent, Vector3 euler, float intensity)
    {
        var lightObject = new GameObject("__IconLight");
        lightObject.transform.SetParent(parent, false);
        lightObject.transform.localRotation = Quaternion.Euler(euler);

        var light = lightObject.AddComponent<Light>();
        light.type      = LightType.Directional;
        light.intensity = intensity;
        light.color     = Color.white;
        light.shadows   = LightShadows.None;
    }

    /// <summary>
    /// A render pipeline that is not set up the way this rig assumes fails by producing a
    /// transparent or an unlit-black image rather than by throwing, and a black square written to
    /// disk is indistinguishable from the placeholder problem this whole exercise exists to end.
    /// So the pixels are inspected before anything is written.
    /// </summary>
    private static bool LooksLikeAnIcon(Texture2D texture, string itemName, string prefabName)
    {
        var pixels = texture.GetPixels32();

        var opaque = 0;
        var lit    = 0;
        foreach (var p in pixels)
        {
            if (p.a <= 8) continue;
            opaque++;
            if (p.r > 24 || p.g > 24 || p.b > 24) lit++;
        }

        if (opaque == 0)
        {
            Debug.LogError($"[AdvancedElectronics] '{itemName}': the render of {prefabName} came back fully transparent -- nothing was written. The object was either outside the camera frustum or culled. Check that {prefabName} has renderers on a layer the camera draws.");
            return false;
        }

        if (lit == 0)
        {
            Debug.LogError($"[AdvancedElectronics] '{itemName}': the render of {prefabName} is a black silhouette ({opaque} opaque pixels, none lit) -- nothing was written. The rig's lights did not reach it, which on HDRP usually means the directional lights need pipeline-specific intensity. Fix the rig rather than shipping this.");
            return false;
        }

        var coverage = 100f * opaque / pixels.Length;
        if (coverage < 4f)
            Debug.LogWarning($"[AdvancedElectronics] '{itemName}': the object covers only {coverage:F1}% of the icon. It will read as a speck at inventory size -- consider tightening the framing.");

        return true;
    }
}
