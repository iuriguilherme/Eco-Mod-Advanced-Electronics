[![Conventional Code](https://img.shields.io/badge/code-conventional%20🏭-red?style=for-the-badge)](https://github.com/zwbao/certified-organic-code)

# Advanced Electronics — Eco mod

A mod for [Eco](https://play.eco) (Strange Loop Games) adding **autonomous drones**: craftable
flying machines that a player slots into a **Drone Dock** and assigns to an area drawn on the map,
which they then fly out to and work unattended.

Two drones ship:

- **Survey Drone** — prospects an assigned area and reports what is under it, per material, with
  quantity, location and depth.
- **Mining Drone** — digs out the plots a survey dock has already surveyed, fifteen layers down,
  and unloads what it breaks into storage you link to the dock. **It mines as you**: every removal
  is performed as the citizen who assigned the area, so settlement laws and private property refuse
  it exactly as they would refuse that citizen digging by hand.

The dock's window grows a **Survey** or **Mining** tab depending on which drone is slotted, plus a
standard **Storage** tab whose Take From / Put Into controls choose where a mining drone unloads.

Live-tested against **Eco 0.14.0.3**; the reference assemblies it compiles against are older,
pinned by `EcoRefSha`. Those are two different things and both are deliberate — the pin keeps
builds reproducible, and the server the mod actually runs on is what proves it works. There is
no `Eco.ReferenceAssemblies` package for 0.14 and the shipped server is a single-file bundle
with its managed assemblies embedded, so the reference assemblies are built from a pinned
source checkout instead — see `scripts/gather-eco-refs.sh` and `EcoRefSha` in the csproj.

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
| `EcoServerMod/AdvancedElectronics.Navigation.Tests/` | xUnit suite over the navigation core — no Eco dependency, so it runs from a bare clone with only a .NET SDK. |
| `EcoServerMod/AdvancedElectronics.Spike/` | Feasibility-spike reference project (kept deliberately as documentation of live-verified Eco API findings — not part of the shipped mod). |
| `EcoServerMod/UserCode/` | Whole-file `.override` copies of vanilla server files — Eco's escape hatch for attributes a mod assembly cannot extend. Installed by `scripts/deploy-usercode-overrides.sh`. |
| Repo root (Unity project) | Client half: Unity **6000.3.19f1** ModKit project that builds the asset bundle (dock/drone prefabs, item icon). |
| `scripts/` | Reference-assembly gathering, release packaging, the client/server name-match gate, UserCode override install. |
| `AssetBundles/`, `dist/` | Build output (git-ignored — rebuild locally, see step 2). |
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

**The server half needs no Unity and no ModKit**, but it is not dependency-free. There are
three tiers, and which one you need depends on what you are touching:

- **`EcoServerMod/AdvancedElectronics.Navigation` and its test project** build and test from a
  bare clone with nothing but a .NET SDK. They deliberately carry no Eco dependency at all.
- **The mod itself (`EcoServerMod/AdvancedElectronics`)** needs Eco's server reference
  assemblies. As noted at the top of this file, there is no `Eco.ReferenceAssemblies` package
  for 0.14, so run `scripts/gather-eco-refs.sh` against an Eco checkout and set
  `EcoRefAssembliesDir` in `EcoServerMod/AdvancedElectronics/Local.props` (git-ignored). The
  csproj errors with instructions if it is unset.
- **The Unity client project** needs the ModKit restore described below.

If you are only touching server C#, you can skip the Unity section entirely and go to
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

1. Craft a **Drone Dock**, a **Survey Drone** and a **Mining Drone** (all three at the
   Robotic Assembly Line) and place the dock.
2. Open the dock, **Survey** tab, click **Manage Areas on Map**. Draw one or more survey
   areas, name them, confirm. A dock holds at most ten.
3. Set **View Position** to the area you want, then click **Assign Selected Area**. The
   assignment line updates. **Unassign Area** stops the drone.
4. Insert the Survey Drone item into the dock's slot — a drone spawns beside the dock and
   heads for the assigned area. Removing the item destroys the drone and resets its state.
5. Watch the same tab for drone status and findings: the selector chooses which area you
   are reading, with per-material quantity, location and depth. **Material Targets**
   narrows the display; leave it empty to show everything.

Moving the selector only changes what you are **looking at** — it never reassigns a
working drone. Assign and Unassign are the only controls that change what the drone does.

Findings persist with their area — reassigning the drone elsewhere and back does not lose
them. Redrawing an area's geometry deliberately clears its findings (it is effectively a
different area); renaming does not.

To mine what you surveyed, place a **second** dock and slot a Mining Drone. Its **Mining** tab
lists the areas published by survey docks you own; pick one and press **Assign Selected Area**. The
drone mines only plots that were actually surveyed, and skips ones it cannot reach. One pass takes
fifteen layers — re-survey the pit floor to send it another fifteen deeper. Open that dock's
**Storage** tab and use **Take From / Put Into** to choose where it unloads; with nothing linked it
fills up and waits at the dock.

Diagnostics available in chat: `/drone areas`, `/drone assignarea <id>`, `/drone survey`,
`/drone filter [material]`, `/drone status`, `/drone tags`, `/drone link [n]`, `/drone animwatch`.
Admin-only: `/drone haltmining <on|off>`, `/drone orphans [destroy]`.

The full owner-run verification protocol (all flows and acceptance checks, with verdict
tables to fill in) is `docs/protocols/2026-07-survey-drone-manual-protocol.md`.

**Heads-up for the first live run:** several Eco API behaviors cannot be verified offline (the
reference assemblies ship with method bodies stripped) and are marked with `ASSUMPTION` comments in
the server code — the manual protocol is what confirms them.

Dock and drone state **does** survive a restart, including a mining job resumed mid-shaft; that was
verified live for 0.3.0. What can still go wrong across a restart is a drone left orphaned with no
dock owning it — `/drone orphans destroy` removes those.

## Server administration

### Stopping every mining drone at once

```
/drone haltmining on     # every mining job on the server stops before its next removal
/drone haltmining off    # jobs resume
```

Admin-only, and server-wide by design — it is not a permission on any one dock. Mining
drones delete terrain automatically and unattended, so a server owner needs one lever that
reaches docks they have no access to, without hunting down their owners.

What it does and does not do:

- A running job stops **before its next block removal**, not mid-pack — nothing is left
  half-removed. The drone finishes travelling, declines to mine, and returns to its dock.
- The halt **survives a restart**. It stays on until someone turns it off.
- The dock's Mining tab reports *"an administrator halted mining"* as the stop reason, so
  the owner can see why their drone came home rather than filing it as a bug.
- Surveying is unaffected. Only mining halts.
- Nothing already mined is refunded or reverted, and hold contents are kept.

Everything else about the drones follows ordinary Eco authorization: access on the
property and on the dock object. This command is the one exception, and it exists because
a server owner's ability to stop automated excavation should not depend on who owns it.

## Development

- Server code: `EcoServerMod/README.md` (projects, version pinning, building).
- Client assets: `docs/guides/2026-07-survey-drone-unity-prefab-guide.md` (keyboard-only
  prefab workflow, name-matching rules).
- Tests: `dotnet test EcoServerMod/AdvancedElectronics.Navigation.Tests` (260 tests over
  the pure navigation/survey/mining/lifecycle core — no Eco dependency, so they run
  anywhere).
- Documented learnings: `docs/solutions/` — solutions to past problems, organized by
  category with YAML frontmatter.
- Shared vocabulary: `CONCEPTS.md`.

### Cutting a release

`scripts/package-release.sh [--version X.Y.Z]` builds the Release DLLs, runs the tests,
and assembles `dist/AdvancedElectronics-<version>-eco<game>.zip` in the layout
[Installing a release](#installing-a-release) describes — ready to upload to mod.io.

Rebuild the asset bundle in Unity **first**. The script does not build it (that needs the
Editor) but it does refuse to package one that is older than anything under `Assets/Art`,
because the bundle carries the prefabs, materials and animator controllers — a
stale bundle ships client assets that silently disagree with the DLLs. If a `git`
operation has rewritten source mtimes and you know the bundle is current, `--force`
overrides.

## Known limitations

- **Drones fly through trees, stockpiles and other placed objects.** They route around whatever
  exists when they plan a trip and pass straight through anything built afterwards. Nothing is
  damaged or moved — a visual fault only.
- **A drone starts travelling before its take-off animation finishes.** The server times these by
  counting animation frames rather than being told when a clip ends, so the two drift. Cosmetic.
- **The Drone Dock is a placeholder cube**, drawn about a metre above the volume it occupies, with
  no ghost outline while you hold it. It places and works normally. Item and skill icons are
  flat-colour placeholders too.
- **Mining is not reversible from inside the mod.** A worked plot is left as an open pit with a 3×3
  mouth, and there is no fence around it. Drones cannot place blocks, so there is no backfilling
  and no safety rim.
- **A drone can be left orphaned across a restart** — alive with no dock owning it. Admins clear
  these with `/drone orphans` and `/drone orphans destroy`.
- The Harvest Drone exists in the source but is not craftable, and the Advanced Electronics
  Assembly is excluded from the build.

## License

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

The full LGPL text is in [`LICENSE`](LICENSE).

### Attribution

The HRVSTR-01 drone -- its mesh, textures, rig and animation clips, under
`Assets/Art/AdvancedElectronics/Sprites/HRVSTR/` -- was created by
[Phlo123](https://github.com/Phlo123) and is licensed under the Creative Commons
Attribution-ShareAlike 4.0 International license. The
full text of that license is in [`LICENSE-ART`](LICENSE-ART).

### Third Party

Eco, the Eco ModKit and their assets remain the property of Strange Loop Games. They are
**not** redistributed here -- they are account-gated downloads for owners of the game, so
mirroring them would route around that gate; see [Setup after cloning](#setup-after-cloning)
for the paths involved and how to supply your own copy. The grant above covers this
project's own work only. It does not extend to anything of theirs, and it does not give you
rights to the game or the ModKit.
