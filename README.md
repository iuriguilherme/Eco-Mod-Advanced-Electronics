# Advanced Electronics — Eco mod

A mod for [Eco](https://play.eco) (Strange Loop Games) adding a **survey drone**: a
craftable flying drone that a player pairs to a **Drone Dock**, assigns to a survey area
drawn on the map, and dispatches to prospect that area for materials. The dock's window
has two tabs — **Areas** (draw and manage areas, assign the drone) and **Results** (what
was found, one area at a time, filtered to the materials you care about).

Built and tested against **Eco 0.14.0.0**. There is no `Eco.ReferenceAssemblies` package for
0.14 and the shipped server is a single-file bundle with its managed assemblies embedded, so
the reference assemblies are built from a pinned source checkout instead — see
`scripts/gather-eco-refs.sh` and `EcoRefSha` in the csproj.

**Mod page:** [mod.io/g/eco/m/advanced-electronics](https://mod.io/g/eco/m/advanced-electronics)
— released builds are published there. This repository is the source; you only need to
build anything if you are developing the mod. Server admins should take the release zip
from mod.io and follow [Installing a release](#installing-a-release).

**Devlog:** [YouTube playlist](https://www.youtube.com/watch?v=xqkzmVZ5kcM&list=PLHZA8oVAgAd4)
— how and why the mod is being built.

> ### ⚠ Alpha — do not use on a world you care about
>
> This mod is in **alpha**. Expect breaking changes between versions, including ones that
> are not migrated: an update can change how dock and drone state is stored, and older
> saved state may not survive it.
>
> It is also known to **leave orphaned objects in the world** — drones that outlive their
> dock, or objects an update no longer recognises — which may need removing by hand with
> admin tools.
>
> Run it on a test world, or a world where losing placed Drone Docks and their survey data
> is acceptable. Back up your save before updating.

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

## Installing a release

For running the mod on a server — no build tools, no Unity, no clone required.

1. Download the release zip from
   [the mod.io page](https://mod.io/g/eco/m/advanced-electronics).
2. Stop the Eco server.
3. Extract the zip and put the `AdvancedElectronics` folder into the server's
   `Eco_Data/Server/Mods/`, giving you `Mods/AdvancedElectronics/`:
   - `AdvancedElectronics.dll` — the mod
   - `AdvancedElectronics.Navigation.dll` — the navigation core the mod cannot load without
   - `AdvancedElectronics.unity3d` — the client asset bundle, which the server transfers to
     connecting players automatically (players install nothing by hand)
4. **If updating, delete the old copy first** — see the warning below.
5. Start the server and check the mods listing for **Advanced Electronics**.

> **Updating: remove the old copy from `Mods/` entirely, don't just move it aside.**
> Eco scans `Mods/` recursively and keys asset bundles by **filename**, so a second copy
> of `AdvancedElectronics.unity3d` anywhere beneath `Mods/` aborts startup with
> `System.ArgumentException: An item with the same key has already been added. Key:
> AdvancedElectronics.unity3d`. A folder you created and named `Ignore`, `old` or `backup`
> is still scanned — the name means nothing to the server. Move old copies out of `Mods/`
> or delete them.

To uninstall, delete the `Mods/AdvancedElectronics/` folder. Note that removing
the mod discards any placed Drone Docks along with their survey areas and findings.

## Setup after cloning

**The server half needs nothing extra.** `EcoServerMod/` builds straight from a clone —
its Eco dependency comes from the `Eco.ReferenceAssemblies` NuGet package. If you are only
touching server C#, skip this section entirely and go to
[Deploying to a server](#deploying-to-a-server-for-testing).

**The Unity half does need a restore step.** This repository contains only our own work.
The Eco ModKit is distributed by Strange Loop Games from the
[play.eco](https://play.eco) website, behind an account that owns the game — there is no
public download URL, and redistributing it here would route around that gate. So it is
git-ignored rather than vendored, and a fresh clone will not open cleanly in Unity until
you supply your own copy:

| Path (git-ignored) | What it is |
|---|---|
| `Assets/EcoModKit/` | The ModKit itself — `WorldObject`, `ModkitPrefabContainer`, build tooling, template scene |
| `Assets/EcoLibs/` | Eco client utility libraries |
| `Assets/Eco.Client.asmdef` | The Eco client assembly definition. Scripts under `Assets/` with no nearer asmdef — **including ours** — compile into it |
| `Assets/Art/ThirdPartyPublic/` | Vendored third-party libs (DOTween) the ModKit expects |
| `Packages/com.strangeloopgames.eco-shared/` | Precompiled Eco shared DLLs (embedded Unity package) |
| `Packages/com.strangeloopgames.eco-modkit-deps/` | ModKit's package dependencies (embedded Unity package) |
| `Assets/TextMesh Pro/` | Unity's TMP resources — the editor re-imports these on demand |

**You need to own Eco.** Log in to [play.eco](https://play.eco) with the Strange Loop
Games account the game is registered to and download the ModKit for **0.14** from
there; the step-by-step is
[Installing the ModKit](https://wiki.play.eco/en/Installing_the_ModKit) on the Eco wiki.
Note that their public [EcoModKit repo](https://github.com/StrangeLoopGames/EcoModKit)
holds *example mods*, not the kit — you cannot substitute it.

`Packages/manifest.json` and `Packages/packages-lock.json` are tracked here and already
declare the two embedded packages, so dropping those folders into `Packages/` is enough —
do not re-add them through the Package Manager.

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
authored against. Then open
`Assets/Art/AdvancedElectronics/Scenes/AdvancedElectronicsScene.unity` and confirm the
`DroneDockObject`
prefab still shows a **WorldObject** component in the Inspector (not "Missing Script").

## Deploying to a server for testing

### 0. Prerequisites

- An **Eco 0.14 dedicated server** you control. `EcoRefVersion` and `EcoRefSha` in the
  csproj must match the server's game build — see `EcoServerMod/README.md` > Version
  matching if yours differs.
- A **.NET 10 SDK** (`dotnet --list-sdks` shows a 10.x entry). No-admin user-local
  install instructions are in `EcoServerMod/README.md` > Building.
- **Unity 6000.3.19f1** plus the Eco ModKit restored per
  [Setup after cloning](#setup-after-cloning) — only needed to (re)build the client asset
  bundle (step 2); skip both if you already have `AdvancedElectronics.unity3d` from a
  previous build.

### 1. Build and deploy the server DLLs

```bash
dotnet build EcoServerMod/AdvancedElectronics
```

Copy **both** DLLs from `EcoServerMod/AdvancedElectronics/bin/Debug/net10.0/` into the
server's `Mods/` directory:

- `AdvancedElectronics.dll` — the mod
- `AdvancedElectronics.Navigation.dll` — the navigation core the mod cannot load without

Eco loads pre-compiled mods from `Mods/` and any of its subdirectories, so dropping the DLLs
loose in `Mods/` is fine for a dev loop where you are overwriting them constantly. Releases
use `Mods/AdvancedElectronics/` instead — one folder to add or delete. `Mods/UserCode/` is
for source-code mods Eco compiles at runtime, not for a compiled-DLL mod like this one.

Or automate the copy: create `EcoServerMod/AdvancedElectronics/Local.props`
(git-ignored) pointing at your server, and every build deploys both DLLs itself:

```xml
<Project>
  <PropertyGroup>
    <EcoModsDir>C:\path\to\EcoServer\Mods\AdvancedElectronics</EcoModsDir>
  </PropertyGroup>
</Project>
```

### 2. Build and deploy the client asset bundle

The bundle carries the dock/drone prefabs and the item icon. `AssetBundles/` is
git-ignored, so build it locally:

1. Open this repo's root folder in Unity 6000.3.19f1 and open
   `Assets/Art/AdvancedElectronics/Scenes/AdvancedElectronicsScene.unity`.
2. Menu **Eco Tools > Mod Kit > Build Current Bundle** (or "ModKit Tools…" to build all
   bundles). Output lands in `AssetBundles/AdvancedElectronics.unity3d`.
3. Copy `AdvancedElectronics.unity3d` next to the two DLLs on the server. Eco transfers
   mod assets to connecting clients — clients do not install anything by hand. (If objects
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
- Tests: `dotnet test EcoServerMod/AdvancedElectronics.Navigation.Tests` (68 tests over
  the pure navigation/survey/lifecycle core — no Eco dependency, so they run anywhere).
- Documented learnings: `docs/solutions/` — solutions to past problems, organized by
  category with YAML frontmatter.
- Shared vocabulary: `CONCEPTS.md`.

### Cutting a release

`scripts/package-release.sh [--version X.Y.Z]` builds the Release DLLs, runs the tests,
and assembles `dist/AdvancedElectronics-<version>-eco<game>.zip` in the layout
[Installing a release](#installing-a-release) describes — ready to upload to mod.io.

Rebuild the asset bundle in Unity **first**. The script does not build it (that needs the
Editor) but it does refuse to package one that is older than anything under `Assets/Art`,
because the bundle carries the prefabs and the `DockReadoutDisplay` MonoBehaviour — a
stale bundle ships client behaviour that silently disagrees with the DLLs. If a `git`
operation has rewritten source mtimes and you know the bundle is current, `--force`
overrides.

## Known limitations

- **Picking up a Drone Dock discards its survey areas and findings.** The dock item is
  stackable, so replacing it creates a fresh world object rather than restoring the old
  one's state. Tracked in `docs/ideation/2026-07-26-survey-system-improvements.md`.
- Assign buttons cover the first six areas; beyond that use `/drone assignarea <id>`.

## License

    Advanced Electronics -- an Eco mod adding an autonomous flying survey drone.
    Copyright (C) 2026  Iuri Guilherme

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Lesser General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Lesser General Public License for more details.

    You should have received a copy of the GNU Lesser General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.

The full text is in [`LICENSE`](LICENSE). The LGPL incorporates the terms of the
[GNU GPL v3](https://www.gnu.org/licenses/gpl-3.0.txt) by reference and adds permissions on
top of it; only the LGPL text is kept here, since that is the licence this project grants.

LGPL rather than GPL because the mod links against Eco's proprietary assemblies and is
loaded by a proprietary server, which the LGPL explicitly permits and the GPL would not. In
short: you may use, modify and redistribute this mod, including as part of a larger work
that is not itself LGPL -- but changes *to this mod* must be published under the same
licence, and users must be able to replace it with their own modified build.

Eco, the Eco ModKit and their assets remain the property of Strange Loop Games. They are
**not** redistributed here -- they are account-gated downloads for owners of the game, so
mirroring them would route around that gate; see [Setup after cloning](#setup-after-cloning)
for the paths involved and how to supply your own copy. The grant above covers this
project's own work only. It does not extend to anything of theirs, and it does not give you
rights to the game or the ModKit.

### Attribution

The HRVSTR-01 drone -- its mesh, textures, rig and animation clips, under
`Assets/Art/AdvancedElectronics/Sprites/HRVSTR/` -- was created by
[Phlo123](https://github.com/Phlo123) and is licensed under the Creative Commons
Attribution-ShareAlike 4.0 International licence, **not** under the LGPL grant above. The
full text of that licence is in [`LICENSE-ART`](LICENSE-ART).

The model ships inside the released asset bundle as well as living in this repository, so
`LICENSE-ART` travels in the release archive too. If you redistribute a modified version of
the model you must credit Phlo123, say that you changed it, and license your version under
CC BY-SA 4.0 as well. That obligation attaches to the model and its adaptations, not to
this project's code: the two are separate works distributed together, not one derived from
the other.

`HRVSTR_BladesMask.mask`, in the same folder, is this project's own work and falls under the
LGPL grant despite sitting beside the contributed files.
