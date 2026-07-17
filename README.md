# Advanced Electronics — Eco mod

A mod for [Eco](https://play.eco) (Strange Loop Games) adding a **survey drone**: a
craftable ground rover that a player pairs to a **Drone Dock**, assigns to a map
district, and dispatches to survey the district for ore density. Results appear on the
dock's readout (drone status, densest cell per ore type, coverage gauge).

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

1. Draw a district on the map (the same interface used for law districts); note its name.
2. Craft a **Drone Dock** and a **Survey Drone** (both at the Electric Machinist Table)
   and place the dock.
3. Stand near your dock and run `/drone district <name>` in chat.
4. Insert the Survey Drone item into the dock's slot — a drone spawns beside the dock
   and heads for the district.
5. Watch the dock readout: status (`EnRoute` → `Surveying`), then per-ore
   `"<ore>: densest at <cell>, ~<pct>%"` lines as it samples. `/drone district` with no
   name recalls the drone.

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
- Tests: `dotnet test EcoServerMod/AdvancedElectronics.Navigation.Tests` (31 tests over
  the pure navigation/survey/lifecycle core).
- Documented learnings: `docs/solutions/` — solutions to past problems, organized by
  category with YAML frontmatter.
