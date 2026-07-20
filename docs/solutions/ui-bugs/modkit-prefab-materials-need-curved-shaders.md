---
title: Mod prefabs render solid magenta in the Eco client unless their materials use the ModKit's Curved shaders
date: 2026-07-19
category: ui-bugs
module: AdvancedElectronics
problem_type: ui_bug
component: tooling
severity: medium
symptoms:
  - "Modded WorldObject renders as a solid magenta/pink shape in the game client (the classic Unity missing-shader color), while rendering fine or unnoticed in the editor"
  - "Prefab's renderer references a material GUID that resolves to no asset in the project (dangling reference), or a material using an HDRP shader"
root_cause: config_error
resolution_type: code_fix
tags: [eco-modding, unity, shader, material, magenta, hdrp, curved-standard, modkit, asset-bundle]
related_components: [Assets/Art/AdvancedElectronics, Assets/EcoModKit/Shaders]
---

# Mod prefabs render solid magenta in the Eco client unless their materials use the ModKit's Curved shaders

## Problem

Both mod prefabs (a dock and a drone, placeholder cube/capsule meshes) rendered as giant
solid-magenta shapes in the game client across every live test. Magenta is Unity's
missing/unresolvable-shader fallback: the material either doesn't exist or uses a shader
the running player doesn't ship.

## Symptoms

- In-game: the object is a flat magenta silhouette — no lighting, no texture.
- In the prefab YAML: `m_Materials` pointed at GUID `73c176f402d2c2f4d929aa5da7585d17`,
  which resolves to **no asset** anywhere under `Assets/` — a dangling reference left
  over from primitive creation in an earlier project state.
- Even with a valid material, this Unity project is HDRP (Unity 6000.3, HDRP 17) while
  the Eco game client does not ship HDRP shaders — an HDRP `Lit` material bundles fine
  and still renders magenta in the client.

## What Didn't Work

- Ignoring it as "just placeholder art." The magenta shape dominated every test
  screenshot, masking whether visual changes landed at all, and turned out to double as
  a diagnosis obstacle.

## Solution

Use the shaders the ModKit itself ships — they exist in the game client by construction.
`Assets/EcoModKit/Shaders/CurvedStandard.shader` declares `Shader "Curved/Standard"`, a
modified Unity Standard shader supporting Eco's curved-world vertex displacement (the
ModKit also ships Fade/Particle/4-channel variants alongside it).

1. Create a material on that shader (Standard-style properties: `_Color`, `_MainTex`,
   `_Metallic`, `_Glossiness`). This project's:
   `Assets/Art/AdvancedElectronics/AdvancedElectronicsPlaceholder.mat`, `m_Shader`
   pointing at the CurvedStandard shader's GUID `b317ea5cc069fde4f94662eac4cb8f1e`.
2. Point every renderer's material slot in the mod prefabs at it (both prefabs'
   `m_Materials` entries repointed from the dangling GUID).
3. Verify in-editor before bundling — an editor script confirmed both prefabs'
   renderers resolve to `shader=Curved/Standard`, so the fix is proven without a live
   test. Then rebuild the bundle.

In-editor verification passed; live client confirmation pending as of this writing
(batched with other fixes per
`docs/solutions/workflow-issues/eco-mod-batched-live-testing.md`).

## Why This Works

An asset bundle carries the material and shader it references. A dangling GUID carries
nothing — magenta. An HDRP shader reference carries a shader the client's render
pipeline cannot use — magenta again. The ModKit's `Curved/*` shaders are the vendored,
client-compatible surface: they compile in this project, bundle with the mod, and match
what the game world itself renders with (including the world-curvature vertex shader,
which standard flat shaders lack — an object shaded flat would visibly detach from
Eco's curved horizon at distance).

## Prevention

- When authoring any mod prefab renderer, assign a material on a `Curved/*` ModKit
  shader from the start; never leave a primitive's default material in place. The
  editor won't warn — the default/HDRP material looks normal in the editor and only
  fails in the client.
- Add to the prefab-finishing checklist: grep the prefab YAML's `m_Materials` GUIDs and
  confirm each resolves to an asset in the project (`grep -rl <guid> Assets --include=*.meta`);
  a GUID with no hit is a guaranteed magenta.
- Verification is fully static: an editor script dumping
  `renderer.sharedMaterial.shader.name` per prefab proves shader resolution without a
  live test.

## Related

- `docs/solutions/workflow-issues/eco-mod-batched-live-testing.md` — this fix shipped in
  the batched deploy that workflow rule mandates.
- `docs/solutions/conventions/eco-custom-worldobject-placement-requirements.md` — the
  broader client-prefab checklist this material rule now belongs beside.
