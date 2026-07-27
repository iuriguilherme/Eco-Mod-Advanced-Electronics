# Advanced Electronics — Eco mod

A mod for [Eco](https://play.eco) (Strange Loop Games) adding a **survey drone**: a
craftable ground rover that a player pairs to a **Drone Dock**, assigns to a survey area
drawn on the map, and dispatches to prospect that area for materials. The dock's window
has two tabs — **Areas** (draw and manage areas, assign the drone) and **Results** (what
was found, one area at a time, filtered to the materials you care about).

Built and tested against **Eco 0.13.0.4** (`Eco.ReferenceAssemblies 0.13.0.4-beta-release-1024`).

## Repository layout

| Path | What it is |
|---|---|
| `EcoServerMod/AdvancedElectronics/` | Server half of the mod (.NET 10 class library) — WorldObjects, items, recipes, chat commands, drone logic. See `EcoServerMod/README.md` for build details. |
| `EcoServerMod/AdvancedElectronics.Navigation/` | Pure-C# navigation core (A* pathfinder, survey grid, lifecycle state machine) — unit-tested, zero Eco dependency, shipped alongside the mod DLL. |
| `EcoServerMod/AdvancedElectronics.Spike/` | Feasibility-spike reference project (kept deliberately as documentation of live-verified Eco API findings — not part of the shipped mod). |
| Repo root (Unity project) | Client half: Unity **6000.3.19f1** ModKit project that builds the asset bundle (dock/drone prefabs, item icon). |
| `AssetBundles/` | Bundle build output (git-ignored — rebuild it locally, see step 2). |
| `docs/` | Plans, spike findings, manual test protocol, documented learnings (`docs/solutions/`). |
| `CONCEPTS.md` | Shared domain vocabulary (survey area, plot, finding, coverage, assignment). |

## Setup after cloning

**The server half needs nothing extra.** `EcoServerMod/` builds straight from a clone —
its Eco dependency comes from the `Eco.ReferenceAssemblies` NuGet package. If you are only
touching server C#, skip this section entirely and go to
[Deploying to a server](#deploying-to-a-server-for-testing).

**The Unity half does need a restore step.** This repository contains only our own work.
Strange Loop Games' ModKit and client libraries are theirs to distribute, so they are
git-ignored rather than vendored here, and a fresh clone will not open cleanly in Unity
until you put them back:

| Path (git-ignored) | What it is |
|---|---|
| `Assets/EcoModKit/` | The ModKit itself — `WorldObject`, `ModkitPrefabContainer`, build tooling, template scene |
| `Assets/EcoLibs/` | Eco client utility libraries |
| `Assets/Eco.Client.asmdef` | The Eco client assembly definition. Scripts under `Assets/` with no nearer asmdef — **including ours** — compile into it |
| `Assets/Art/ThirdPartyPublic/` | Vendored third-party libs (DOTween) the ModKit expects |
| `Packages/com.strangeloopgames.eco-shared/` | Precompiled Eco shared DLLs (embedded Unity package) |
| `Packages/com.strangeloopgames.eco-modkit-deps/` | ModKit's package dependencies (embedded Unity package) |
| `Assets/TextMesh Pro/` | Unity's TMP resources — the editor re-imports these on demand |

Restore them from the **official Eco ModKit distribution for 0.13.0.4**, which Strange Loop
Games publishes for mod authors — start from the
[Mod Development wiki](https://wiki.play.eco/en/Mod_Development). (Their
[EcoModKit repo](https://github.com/StrangeLoopGames/EcoModKit) holds *example mods*, not
the kit itself.) `Packages/manifest.json` and `Packages/packages-lock.json` are tracked
here and already declare the two embedded packages, so dropping those folders into
`Packages/` is enough — do not re-add them through the Package Manager.

### Why the copy has to be the official one

Unity references assets by the GUID recorded in each `.meta` file. Our tracked
`DroneDockObject.prefab` references **four** GUIDs that live in the folders above. Import
the official distribution (which carries the original `.meta` files) and they match. Copy
the files loose, re-import them, or regenerate them any other way, and Unity mints *fresh*
GUIDs — at which point the prefab silently loses its `WorldObject` component and the mod
builds into a bundle that does nothing. There is no error message for this.

**Check the restore worked** before opening Unity:

```bash
grep guid Assets/EcoModKit/Scripts/WorldObject.cs.meta
# expected: guid: 22281bf2bb54279449ac8e3fbf199314
```

If that GUID differs, stop — your ModKit copy is not the one this repo's prefabs were
authored against. Then open `Assets/DroneScene.unity` and confirm the `DroneDockObject`
prefab still shows a **WorldObject** component in the Inspector (not "Missing Script").

## Deploying to a server for testing

### 0. Prerequisites

- An **Eco 0.13.0.4 dedicated server** you control. The `Eco.ReferenceAssemblies`
  version pinned in the csproj must match the server's game build — see
  `EcoServerMod/README.md` > Version matching if yours differs.
- A **.NET 10 SDK** (`dotnet --list-sdks` shows a 10.x entry). No-admin user-local
  install instructions are in `EcoServerMod/README.md` > Building.
- **Unity 6000.3.19f1** — only needed to (re)build the client asset bundle (step 2);
  skip if you already have `AdvancedElectronics.unity3d` from a previous build.

### 1. Build and deploy the server DLLs

```bash
dotnet build EcoServerMod/AdvancedElectronics
```

Copy **both** DLLs from `EcoServerMod/AdvancedElectronics/bin/Debug/net10.0/` into the
server's `Mods/UserCode/` directory:

- `AdvancedElectronics.dll` — the mod
- `AdvancedElectronics.Navigation.dll` — the navigation core the mod cannot load without

Or automate the copy: create `EcoServerMod/AdvancedElectronics/Local.props`
(git-ignored) pointing at your server, and every build deploys both DLLs itself:

```xml
<Project>
  <PropertyGroup>
    <EcoModsDir>C:\path\to\EcoServer\Mods\UserCode</EcoModsDir>
  </PropertyGroup>
</Project>
```

### 2. Build and deploy the client asset bundle

The bundle carries the dock/drone prefabs and the item icon. `AssetBundles/` is
git-ignored, so build it locally:

1. Open this repo's root folder in Unity 6000.3.19f1 and open `Assets/DroneScene.unity`.
2. Menu **Eco Tools > Mod Kit > Build Current Bundle** (or "ModKit Tools…" to build all
   bundles). Output lands in `AssetBundles/AdvancedElectronics.unity3d`.
3. Copy `AdvancedElectronics.unity3d` to the server, into a mod folder such as
   `Mods/AdvancedElectronics/`. Eco transfers mod assets to connecting clients — clients
   do not install anything by hand. (Bundle placement follows the
   [Eco mod development wiki](https://wiki.play.eco/en/Mod_Development); if objects
   appear as missing-model placeholders in-game, bundle placement is the first thing to
   re-check.)

Optional sanity gate before deploying: `./scripts/validate-name-match.sh` confirms every
server WorldObject/Item class has a matching-named client asset (the name match is how
Eco links the two — a silent-failure seam if it drifts).

### 3. Restart the server and verify it loaded

Restart the Eco server and check the mods listing (server UI or console) for
**Advanced Electronics** / the status line `Advanced Electronics mod loaded`.

### 4. Test in-game

Quick smoke test:

1. Craft a **Drone Dock** and a **Survey Drone** (both at the Electric Machinist Table)
   and place the dock.
2. Open the dock, **Areas** tab, click **Manage Areas on Map**. Draw one or more survey
   areas, name them, confirm.
3. Click **Assign Area 1**. The assignment line updates; clicking the same button again
   unassigns.
4. Insert the Survey Drone item into the dock's slot — a drone spawns beside the dock and
   heads for the assigned area. Removing the item destroys the drone and resets its state.
5. Watch the **Areas** tab for drone status, then the **Results** tab for what it found:
   one area at a time (Previous/Next Area), with per-material quantity, location and
   depth. **Material Targets** narrows the display; leave it empty to show everything.

Findings persist with their area — reassigning the drone elsewhere and back does not lose
them. Redrawing an area's geometry deliberately clears its findings (it is effectively a
different area); renaming does not.

Diagnostics available in chat: `/drone areas`, `/drone assignarea <id>` (the fallback for
areas past the sixth assign button), `/drone results`, `/drone filter [material]`,
`/drone state`, `/drone tags`.

The full owner-run verification protocol (all flows and acceptance checks, with verdict
tables to fill in) is `docs/protocols/2026-07-survey-drone-manual-protocol.md`.

**Heads-up for the first live run:** several Eco API behaviors could not be verified
offline (the reference assemblies ship with method bodies stripped) and are marked with
`ASSUMPTION` comments in the server code — the manual protocol is what confirms them.
Known open items from code review are tracked on
[PR #1](https://github.com/iuriguilherme/Eco-Mod-Advanced-Electronics/pull/1), including
one to verify early: a server restart with a drone deployed strands it (dock/drone
pairing state is not yet persisted).

## Development

- Server code: `EcoServerMod/README.md` (projects, version pinning, building).
- Client assets: `docs/guides/2026-07-survey-drone-unity-prefab-guide.md` (keyboard-only
  prefab workflow, name-matching rules).
- Tests: `dotnet test EcoServerMod/AdvancedElectronics.Navigation.Tests` (69 tests over
  the pure navigation/survey/lifecycle core — no Eco dependency, so they run anywhere).
- Documented learnings: `docs/solutions/` — solutions to past problems, organized by
  category with YAML frontmatter.
- Shared vocabulary: `CONCEPTS.md`.

## Known limitations

- **Picking up a Drone Dock discards its survey areas and findings.** The dock item is
  stackable, so replacing it creates a fresh world object rather than restoring the old
  one's state. Tracked in `docs/ideation/2026-07-26-survey-system-improvements.md`.
- Assign buttons cover the first six areas; beyond that use `/drone assignarea <id>`.

## Third-party content and licensing

Everything tracked in this repository is our own work. Strange Loop Games' ModKit, client
libraries and embedded Unity packages are **not** redistributed here — see
[Setup after cloning](#setup-after-cloning) for the paths involved and how to restore
them. Eco, the ModKit and their assets remain the property of Strange Loop Games and are
used under their mod-development terms.

This project does not yet declare a licence of its own; until it does, no permissions are
granted beyond viewing the source. Open an issue if you want to use it for something.
