---
title: "One asset tagged into its own bundle splits the build, and nothing renders"
date: 2026-08-08
category: build-errors
module: Assets
problem_type: bug
component: asset-bundle
severity: critical
applies_when:
  - "Objects render as nothing in game -- no model, no placement ghost, no placeholder"
  - "The server log is clean and the mod loads normally"
  - "Prefab names validate, prefab roots are correctly disabled, and references resolve"
  - "Before deploying any rebuilt bundle"
tags: [unity, asset-bundle, modkit, deploy, silent-failure, invisible-object]
related_components: [Assets/Art/AdvancedElectronics, AssetBundles]
---

# One asset tagged into its own bundle splits the build, and nothing renders

## Context

Every drone was invisible in game and the Drone Dock had no placement ghost. Everything that
normally explains that had already been checked and was correct:

- `scripts/validate-name-match.sh` reported `PASS` — prefab names matched their server classes.
- Every prefab root was named correctly and shipped disabled.
- The scene's `ModkitPrefabContainer` entries all resolved to the live prefabs.
- The prefabs' own references — model, materials, animator, relay script — all resolved.
- The server log showed the mod loading, with no error of any kind.
- The deployed bundle and DLLs were current, built minutes apart.

The bundle was incomplete by construction, and nothing in the toolchain says so.

## Guidance

**Exactly one asset in this project should carry an asset-bundle tag: the scene.** "Build
Current Bundle" tags the scene (`ModKitTools.cs` calls `SetAssetBundleNameAndVariant` on the
scene's importer), and every prefab, mesh, material and texture ships as a *dependency* of that
scene. That is the whole design — the prefabs are deliberately untagged.

**An asset that carries its own tag is pulled out of the scene's bundle.** Unity will not put
one asset in two bundles, so a tagged asset becomes its own bundle and the scene's bundle is
emitted with a dependency on it:

```yaml
# AssetBundles/advancedelectronics8.manifest
Assets:
- Assets/Art/AdvancedElectronics/Scenes/AdvancedElectronicsScene.unity
Dependencies:
- .../AssetBundles/advancedelectronics3     # <- one stale material lives here
```

Only one `.unity3d` gets deployed. The dependency cannot resolve on the client, and the
dependency chain breaks before the prefabs are reached.

**The failure is silent at every layer**, which is what makes it expensive:

| Layer | What it sees | What it reports |
|---|---|---|
| Unity build | A valid bundle with a declared dependency | Nothing — this is a legal bundle |
| Deploy | One `.unity3d` file, as always | Nothing |
| Server | Objects placed, ticked, positioned | Nothing — the server has no client assets |
| Client | A bundle whose dependency is absent | Nothing |
| Player | Objects that are not drawn | — |

**Find it before deploying:**

```
Eco Tools > Advanced Electronics > Report Stray Asset Bundle Tags
```

It logs the scene's tag as expected and errors on anything else. Or read the manifest directly
— a `Dependencies:` list naming another `advancedelectronics*` bundle is the tell:

```bash
grep -A 3 '^Dependencies:' AssetBundles/<newest>.manifest
```

**Clear a stray tag** in the Inspector: select the asset, and at the bottom of its panel set
the **AssetBundle** dropdown to **None**. In the `.meta` it is one line:

```yaml
  assetBundleName:            # empty, not a bundle name
```

**Where stray tags come from.** "ModKit Tools…" lets you tag assets into a named bundle, and a
tag set during an experiment persists in the asset's `.meta` indefinitely — through renames,
folder moves, and every later build. It is invisible in the Project window; only the Inspector's
bottom bar and the `.meta` show it.

## Why This Matters

The symptom points away from the cause in the strongest possible way. "Objects do not render"
reads as a prefab problem, so the search goes to prefab names, root active flags, missing
references, the prefab container — all of which can be verified correct while the real fault
sits in a single line of one material's `.meta`, describing not the asset but where it is
*packaged*.

It also survives every check the project already has. The name-match validator compares names.
The duplicate-name reporter compares names. Both pass, because names were never the problem —
bundle composition was, and nothing was looking at it.

And it is total rather than partial. One stray tag on one material took every object in the mod
out of the client, because the broken dependency stops the chain before any prefab loads. A
failure that degraded gracefully — one untextured drone — would have been far easier to read.

## When to Apply

- Any time objects do not render and the name/root/reference checks come back clean.
- Before deploying a rebuilt bundle, as a standing pre-flight check.
- After using "ModKit Tools…" to tag anything, since that is where stray tags originate.
- When a bundle's file size changes sharply for no reason you can name — assets leaving for
  another bundle is one explanation.

## Related

- `docs/solutions/logic-errors/moving-a-prefab-can-hand-its-guid-to-a-backup-copy.md` — the
  other way this project has shipped a bundle that looked right and was not. That one breaks
  *which* asset ships; this one breaks *whether* it ships at all. Both pass the name-match
  validator.
- `docs/solutions/ui-bugs/bundled-mod-objects-must-ship-disabled.md` — the other cause of an
  object that is located but not drawn. Tell them apart by reload: an active-template failure
  recovers when the area reloads, a missing-dependency failure never does.
