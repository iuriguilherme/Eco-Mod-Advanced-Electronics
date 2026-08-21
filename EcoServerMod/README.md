# EcoServerMod — server-side mod code

Server half of the Advanced Electronics mod. The Unity project in this repo builds the
client assets; projects under this folder build against Eco's server reference
assemblies and deploy to an Eco dedicated server.

## Projects

- `AdvancedElectronics/` — the real mod (server half of the drone feature). Registers the craftable
  `DroneDock` WorldObject plus the `SurveyDroneItem` and `MiningDroneItem`, all three crafted at the
  Robotic Assembly Line. Slotting a drone item into the dock spawns a WorldObject that self-navigates
  to an area the player drew on the map, and the dock grows a **Survey** or **Mining** tab to match.
  Survey drones report ore density and depth; mining drones dig the surveyed plots and unload into
  storage linked through the dock's Storage tab. A clean sibling of the spike
  (KTD1: the spike is a reference, not a base class), reusing its proven csproj shape,
  version pin, and registration pattern by imitation, not inheritance. References
  `AdvancedElectronics.Navigation` — deploying the mod means deploying both DLLs.
- `AdvancedElectronics.Navigation/` — pure-C# navigation core (grid A* pathfinder,
  survey-grid accumulation, drone lifecycle state machine). Zero Eco dependency by
  design (KTD2) so it is unit-testable without a game server.
- `AdvancedElectronics.Navigation.Tests/` — xUnit suite over the navigation core
  (`dotnet test EcoServerMod/AdvancedElectronics.Navigation.Tests`).
- `AdvancedElectronics.Spike/` — feasibility spike for the survey drone
  (`docs/plans/2026-07-11-002-feat-drone-feasibility-spike-plan.md`). Three admin chat
  commands probe the questions blocking the survey-drone plan; results are recorded in
  `docs/spikes/2026-07-survey-drone-spike.md`. Kept as reference/documentation — not
  part of the shipped mod, deploy it only to re-run the probes.
- `UserCode/` — whole-file `.override` copies of vanilla server files, not a build project. The
  Robotic Assembly Line only accepts plugin modules its own `[AllowPluginModules]` names, and a mod
  assembly cannot add to that attribute, so Eco's escape hatch is a same-path override under
  `Mods/UserCode/`. Installed and refreshed by `scripts/deploy-usercode-overrides.sh`, which must be
  re-run after a game update.

## Version matching

The reference assemblies **must match the target server's game build**.

- The mod targets Eco **0.14**. There is no `Eco.ReferenceAssemblies` package for it, and the
  shipped dedicated server is a single-file bundle with its managed assemblies embedded, so
  neither is usable as a reference source. The assemblies are built from an Eco source checkout
  instead, pinned by `EcoRefSha` in `AdvancedElectronics/AdvancedElectronics.csproj`:

  ```bash
  scripts/gather-eco-refs.sh <path-to-eco-checkout>
  ```

  Then set `EcoRefAssembliesDir` in `AdvancedElectronics/Local.props` (git-ignored) to the
  directory it prints. The script refuses to run against a checkout that is not on the pinned
  commit — `staging` moves daily, and a mod tracking a moving branch has no reproducible build.

- The spike at `AdvancedElectronics.Spike/` still pins the `0.13.0.4-beta-release-1024` NuGet
  package. It is reference-only and is not built against 0.14.

- To re-pin: move the checkout to the commit you want, update `EcoRefSha`, and re-run the
  gather script.

## Building

Eco 0.14 reference assemblies target **net10.0**. If `dotnet --list-sdks` shows no 10.x
SDK, install one user-locally (no admin) with the official script:

```powershell
# Windows
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1
./dotnet-install.ps1 -Channel 10.0 -InstallDir "$env:USERPROFILE/.dotnet"
$env:PATH = "$env:USERPROFILE/.dotnet;$env:PATH"
```

Then:

```bash
dotnet build EcoServerMod/AdvancedElectronics          # the mod (also builds Navigation)
dotnet test  EcoServerMod/AdvancedElectronics.Navigation.Tests   # 260-test suite, no Eco dependency
dotnet build EcoServerMod/AdvancedElectronics.Spike    # optional -- reference probes only
```

Note: Unity regenerates the root `.sln`/`.csproj` files for its own scripts and they are
git-ignored; these server projects are deliberately outside that solution. Build them
with `dotnet` directly (or open the folder in your IDE), not via Unity's generated
solution.

## Deploying

Copy **both** DLLs from `AdvancedElectronics/bin/Debug/net10.0/` into the server's
`Mods/AdvancedElectronics/` directory (compiled-DLL mods live directly under `Mods/`;
`Mods/UserCode/` is for source-code mods Eco compiles at runtime):

- `AdvancedElectronics.dll`
- `AdvancedElectronics.Navigation.dll` (project dependency — the mod fails to load without it)

The target server must be running **Eco 0.14**. A 0.13 server will not load this build.

`AdvancedElectronics/Local.props` (git-ignored) holds both machine-local paths: where the
reference assemblies were gathered, and the server to auto-copy the build output into.

```xml
<Project>
  <PropertyGroup>
    <EcoRefAssembliesDir>/path/to/eco-checkout/Build/EcoModkit/ReferenceAssemblies/</EcoRefAssembliesDir>
    <EcoModsDir>/path/to/EcoServer/Mods/AdvancedElectronics/</EcoModsDir>
  </PropertyGroup>
</Project>
```

Point `EcoModsDir` at the tree the server actually runs from, and re-check it whenever you
change game versions. Two installs of different versions have identical folder layouts and
both accept a copy without complaint, so deploying to the wrong one fails silently — see
`docs/solutions/conventions/document-the-path-you-actually-deploy-to.md`.

The client asset bundle (`AssetBundles/AdvancedElectronics.unity3d`, built from the
Unity project) deploys separately — see the root `README.md` for the full
server-testing walkthrough including the bundle and the in-game smoke test.

To re-run the spike probes instead, deploy `AdvancedElectronics.Spike.dll` the same way
(it is self-contained). Its `/spike` chat commands require **admin** authorization on
the server (`/admin add <you>` or server config); the real mod's `/drone` command only
requires normal user auth.

## Object-UI picker findings (spike Q3, UI half) — historical

> **Superseded as a design decision, kept as API evidence.** This section records what was and was
> not available for object UI in **0.13.0.4**, and its conclusion — that district assignment had to
> be a chat command — no longer describes the mod. Areas are drawn on the map through
> `SurveyAreaPicker` and assigned from the dock's own tab; districts are not used at all. The
> negative findings below are still useful if you go looking for a dropdown picker, so they stay.

Recorded during implementation against the 0.13.0.4 reference assemblies:

- Districts are first-class civic objects: `Eco.Gameplay.LegislationSystem.District`,
  managed by `Eco.Gameplay.Civics.Districts.DistrictMap` (a `SimpleProposable` with
  `Districts` dictionary, `GetDistrictAtWorldPos(WorldPosition2i)`,
  `GetDistrictAtPlotPos(PlotPos)`, and `DistrictsUpdatedEvent`). Static helpers live in
  `Eco.Gameplay.Civics.Districts.DistrictUtils` (`BelongsToDistrict(Vector3i, District)`).
  Reading district data from a mod is therefore straightforward server-side.
- The 0.11-era `ClientCanSelectAndAdd` attribute researched during planning was **not
  found** in the 0.13 assemblies under that name. The live-picker half of Q3 therefore
  remains at "partial (assembly-evidence only)" until a picker is demonstrated on a
  WorldObject UI — see the spike report's Q3 section. Candidate mechanisms to evaluate
  when building the real dock: the auto-generated object UI property attributes used by
  vanilla components (grep decompiled vanilla components for dropdown-backed properties),
  or a chat-command fallback for district assignment (works today, no UI risk).

### U6 follow-up probe (dock readout unit, bounded effort)

Re-ran the picker search while building the dock readout (R14/R15), against the same
`0.13.0.4-beta-release-1024` reference assemblies, using `System.Reflection.MetadataLoadContext`
against the raw DLLs (`Eco.Gameplay.dll`, `Eco.Mods.dll`) rather than a decompiler.

- **Correction to the finding above:** `Eco.Gameplay.Utils.ClientCanSelectAndAddAttribute`
  DOES exist as a type in 0.13.0.4 — the earlier "not found under that name" note was
  wrong (likely a search-string/namespace mismatch during the spike). Its real shape:
  `.ctor(string title, string entryTitle, string entryTitlePlural, bool showCategories)`,
  properties `Title`/`EntryTitle`/`EntryTitlePlural`/`ShowCategories`.
- **But it doesn't solve our problem.** Every usage found across `Eco.Gameplay.dll` (7
  hits) targets an `Eco.Core.Utils.ControllerAliasSet` or `ControllerHashSet<T>`
  collection-typed member — chat channel membership (`Channel.Managers`/`Channel.Users`),
  deed accessors (`Deed.Accessors`), permission manager/user sets
  (`DualPermissions.ManagerSet`/`UserSet`). It is an "add/remove members to a roster" UI,
  not a general single-value dropdown picker. Zero usages anywhere in `Eco.Mods.dll` (no
  vanilla WorldObject or TechTree object uses it at all). It's also the wrong shape for
  `DroneDock.AssignedDistrictName`, which is a single string, not a set.
- Also inspected `Eco.Gameplay.Civics.GameValues.GamePickerList` (civics "game values"
  pick-list machinery used inside Laws) as a second candidate: it derives from
  `Eco.Core.Systems.UnserializedEntry` (registrar-managed civics infrastructure), not
  anything attachable to a plain WorldObject's own right-click UI. Wiring it would mean
  building law/civics machinery, well outside this unit's bounded-effort scope.
- **Net finding:** no cheap, WorldObject-attachable, auto-generated single-selection picker was
  found in 0.13.0.4. This was a bounded look, not an open-ended one, per the plan's own framing for
  this probe.
- **What shipped instead.** The chat command this probe concluded with was a stopgap and is gone.
  Area selection is now a map picker (`SurveyAreaPicker`) reached from a **Manage Areas on Map**
  button on the dock, with a numeric selector plus **Assign Selected Area** for choosing among the
  ten areas a dock can hold. Districts play no part. The general lesson — that the client draws only
  what a server declaration names, from a fixed template vocabulary — is written up in
  `docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md`.
