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
    /// ships, the prefab it renders, and the role tint that render is modulated by.
    ///
    /// THE FIRST STRING IS THE SERVER CLASS NAME and is what the scene GameObject is named --
    /// that name is the whole binding. The second is only a file to load; nothing binds to it.
    /// The two differ by more than a suffix for the dock and the drones, which is exactly why
    /// they are two columns rather than one derived from the other.
    ///
    /// THE TINT EXISTS BECAUSE THE DRONES SHARE A CHASSIS. HRVSTR-01 is one machine doing
    /// different jobs, so photographing survey, mining and harvest from a fixed angle produced
    /// three BYTE-IDENTICAL icons -- the same "two items, one picture" failure this mod already
    /// shipped once, arriving by a new route and past a binding gate that correctly passes it
    /// (each entry does point at its own file; the files merely held identical bytes).
    ///
    /// White means no tint. The dock and the assembly are distinct models that read correctly on
    /// their own, and tinting them would only shift them away from what the player sees placed.
    /// </summary>
    private static readonly (string ItemName, string PrefabName, Color Tint)[] RenderedIcons =
    {
        ("SurveyDroneItem",                 "SurveyDroneObject",  new Color(0.25f, 0.55f, 0.85f)), // teal-blue
        ("MiningDroneItem",                 "MiningDroneObject",  new Color(0.60f, 0.85f, 0.10f)), // lime
        ("HarvestDroneItem",                "HarvestDroneObject", new Color(0.42f, 0.26f, 0.14f)), // chocolate
        ("DroneDockItem",                   "DroneDockObject",                        Color.white),
        ("AdvancedElectronicsAssemblyItem", "AdvancedElectronicsAssemblyObject",      Color.white),
    };

    /// <summary>
    /// How far a tinted render moves from its own albedo toward the role colour.
    ///
    /// The tint is applied as a modulation, not a replacement, so surface detail survives it --
    /// at 1.0 a dark role colour like the harvest drone's would crush the model to a silhouette.
    /// Dial this down if the drones stop looking like the same machine; dial it up if the pair
    /// that matters, mining against survey, is not obvious at inventory-thumbnail size.
    /// </summary>
    private const float TintStrength = 0.65f;

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

        // Keyed by the rendered bytes, so two entries that produced the same picture collide here.
        var byImage = new Dictionary<string, List<string>>();

        foreach (var (itemName, prefabName, tint) in RenderedIcons)
        {
            var path = $"{AdvancedElectronicsBuildTools.IconOutputFolder}/{itemName}_icon.png";

            if (!force && AssetDatabase.LoadAssetAtPath<Sprite>(path) != null)
            {
                skipped.Add(itemName);
                continue;
            }

            if (RenderOne(itemName, prefabName, tint, path))
            {
                rendered.Add(itemName);

                var fingerprint = Fingerprint(path);
                if (!byImage.TryGetValue(fingerprint, out var sharers))
                    byImage[fingerprint] = sharers = new List<string>();
                sharers.Add(itemName);
            }
            else failed.Add(itemName);
        }

        ReportIdenticalRenders(byImage);

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

    /// <summary>
    /// Two items drawing one picture is the exact failure this mod already shipped once, when
    /// MiningDroneItem had no icon of its own and rendered the survey drone's. The binding gate
    /// cannot catch this shape of it: every entry here points at its OWN file, and the files
    /// merely happen to hold identical bytes.
    ///
    /// It is not a bug in the rig. The drones share one chassis on purpose -- HRVSTR-01 is one
    /// machine doing different jobs -- so photographing them from a fixed angle necessarily
    /// produces one image. Differentiating them is a content decision, not a rendering one.
    /// </summary>
    private static void ReportIdenticalRenders(Dictionary<string, List<string>> byImage)
    {
        foreach (var sharers in byImage.Values)
        {
            if (sharers.Count < 2) continue;

            Debug.LogError($"[AdvancedElectronics] These entries rendered to BYTE-IDENTICAL icons and will be indistinguishable in game: {string.Join(", ", sharers)}. Each points at its own file, so scripts/validate-icon-binding.sh passes and nothing else will tell you. They share a model, so give them different role tints in RenderedIcons rather than shipping one picture under several names.");
        }
    }

    /// <summary>A cheap content hash of a written icon, for the duplicate check above.</summary>
    private static string Fingerprint(string path)
    {
        using (var md5 = System.Security.Cryptography.MD5.Create())
            return System.Convert.ToBase64String(md5.ComputeHash(System.IO.File.ReadAllBytes(path)));
    }

    private static bool RenderOne(string itemName, string prefabName, Color tint, string path)
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

        var pixels = RenderPrefab(prefab, itemName, tint);
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
    private static byte[] RenderPrefab(GameObject prefab, string itemName, Color tint)
    {
        var size = AdvancedElectronicsBuildTools.IconSize;

        GameObject rig = null;
        RenderTexture renderTexture = null;
        var temporaryMaterials = new List<Material>();
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

            SubstituteRenderableMaterials(renderers, tint, temporaryMaterials);
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

            // No lights: the substitution below renders unlit, which is the whole point of it.

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
            foreach (var material in temporaryMaterials) Object.DestroyImmediate(material);
            if (renderTexture != null) Object.DestroyImmediate(renderTexture);
            if (rig != null) Object.DestroyImmediate(rig);
        }
    }

    /// <summary>
    /// Shader names tried in order, first hit wins. Unlit on purpose.
    ///
    /// The mod's materials use the ModKit's <c>Curved/Standard</c>, a modified Unity Standard
    /// shader -- Built-in Render Pipeline. THIS PROJECT IS HDRP, and a Built-in shader under HDRP
    /// does not render: it draws Unity's magenta "no valid shader" colour. In game that never
    /// shows, because the Eco client supplies the pipeline those materials were written for, so
    /// the materials are correct and only an in-Editor render is affected.
    ///
    /// Unlit also removes lighting from the problem entirely. An HDRP light's intensity is in
    /// physical units and is driven by HDAdditionalLightData rather than Light.intensity, so a
    /// lit rig assembled from plain UnityEngine types is a coin flip between black and blown out.
    /// A flat, correctly-textured render is worth more than a shaded one that might be neither.
    /// </summary>
    private static readonly string[] UnlitShaders =
    {
        "HDRP/Unlit",
        "Universal Render Pipeline/Unlit",
        "Unlit/Texture",
        "Sprites/Default",
    };

    /// <summary>
    /// Points every renderer at a throwaway unlit material carrying the original's albedo.
    ///
    /// Assigning to sharedMaterials on an INSTANTIATED copy repoints that copy's renderers; it
    /// does not touch the material assets on disk. The rig is destroyed straight afterwards.
    /// </summary>
    private static void SubstituteRenderableMaterials(Renderer[] renderers, Color tint, List<Material> created)
    {
        // Lerped from white rather than used raw, so the albedo still reads through it.
        var roleTint = Color.Lerp(Color.white, tint, TintStrength);

        Shader unlit = null;
        foreach (var name in UnlitShaders)
        {
            unlit = Shader.Find(name);
            if (unlit != null) break;
        }

        if (unlit == null)
        {
            Debug.LogWarning("[AdvancedElectronics] Found no unlit shader to render icons with; falling back to the prefab's own materials, which will come out magenta under this project's render pipeline.");
            return;
        }

        Debug.Log($"[AdvancedElectronics] Rendering icons through '{unlit.name}'.");

        foreach (var renderer in renderers)
        {
            var originals   = renderer.sharedMaterials;
            var substitutes = new Material[originals.Length];

            for (var i = 0; i < originals.Length; i++)
            {
                var material = new Material(unlit) { hideFlags = HideFlags.HideAndDontSave };

                var albedo = FindAlbedo(originals[i]);
                var boundAlbedo = albedo == null || SetFirst(material, AlbedoProperties, albedo);

                var original = originals[i] != null && originals[i].HasProperty("_Color")
                    ? originals[i].GetColor("_Color")
                    : Color.white;
                var boundColor = SetFirst(material, ColorProperties, original * roleTint);

                // A shader whose colour property this does not know about renders every entry the
                // same flat default -- which is how three tinted drones came back byte-identical.
                if (!boundAlbedo || !boundColor)
                    Debug.LogError($"[AdvancedElectronics] '{unlit.name}' exposes none of the {(boundAlbedo ? "colour" : "albedo")} property names this knows ({string.Join(", ", boundAlbedo ? ColorProperties : AlbedoProperties)}). Its own are: {string.Join(", ", PropertyNames(unlit))}. Add the right one to AlbedoProperties/ColorProperties.");

                substitutes[i] = material;
                created.Add(material);
            }

            renderer.sharedMaterials = substitutes;
        }
    }

    /// <summary>
    /// Albedo texture property names, tried in order. HDRP/Unlit calls it _UnlitColorMap, HDRP/Lit
    /// _BaseColorMap, URP _BaseMap, Built-in _MainTex -- and a name this list misses binds nothing
    /// while still rendering, which reads as success.
    /// </summary>
    private static readonly string[] AlbedoProperties =
        { "_UnlitColorMap", "_BaseColorMap", "_BaseMap", "_MainTex", "_AlbedoMap" };

    /// <summary>Tint property names, same story: HDRP/Unlit calls it _UnlitColor.</summary>
    private static readonly string[] ColorProperties =
        { "_UnlitColor", "_BaseColor", "_Color" };

    /// <summary>Sets the first property the material actually has. False if it has none of them.</summary>
    private static bool SetFirst(Material material, string[] properties, Texture value)
    {
        foreach (var property in properties)
            if (material.HasProperty(property)) { material.SetTexture(property, value); return true; }
        return false;
    }

    /// <inheritdoc cref="SetFirst(Material, string[], Texture)"/>
    private static bool SetFirst(Material material, string[] properties, Color value)
    {
        foreach (var property in properties)
            if (material.HasProperty(property)) { material.SetColor(property, value); return true; }
        return false;
    }

    /// <summary>Every property a shader exposes, for the diagnostic above.</summary>
    private static IEnumerable<string> PropertyNames(Shader shader)
    {
        for (var i = 0; i < shader.GetPropertyCount(); i++) yield return shader.GetPropertyName(i);
    }

    /// <summary>The original material's albedo, under whichever property name it uses.</summary>
    private static Texture FindAlbedo(Material material)
    {
        if (material == null) return null;

        foreach (var property in AlbedoProperties)
            if (material.HasProperty(property))
            {
                var texture = material.GetTexture(property);
                if (texture != null) return texture;
            }

        return null;
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

        var opaque  = 0;
        var lit     = 0;
        var magenta = 0;
        var blown   = 0;
        foreach (var p in pixels)
        {
            if (p.a <= 8) continue;
            opaque++;
            if (p.r > 24 || p.g > 24 || p.b > 24) lit++;
            // Unity's "no valid shader for this render pipeline" colour.
            if (p.r > 200 && p.g < 80 && p.b > 200) magenta++;
            if (p.r > 245 && p.g > 245 && p.b > 245) blown++;
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

        if (magenta > opaque / 2)
        {
            Debug.LogError($"[AdvancedElectronics] '{itemName}': the render of {prefabName} is Unity's magenta missing-shader colour ({100f * magenta / opaque:F0}% of the object) -- nothing was written. The material's shader does not render under this project's pipeline; SubstituteRenderableMaterials is supposed to prevent exactly this, so check that one of UnlitShaders resolves.");
            return false;
        }

        if (blown > opaque * 9 / 10)
        {
            Debug.LogError($"[AdvancedElectronics] '{itemName}': the render of {prefabName} came back almost entirely white -- nothing was written. The substitute material lost its albedo, or the render is blown out.");
            return false;
        }

        var coverage = 100f * opaque / pixels.Length;
        if (coverage < 4f)
            Debug.LogWarning($"[AdvancedElectronics] '{itemName}': the object covers only {coverage:F1}% of the icon. It will read as a speck at inventory size -- consider tightening the framing.");

        return true;
    }
}
