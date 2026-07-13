# EcoServerMod — server-side mod code

Server half of the Advanced Electronics mod. The Unity project in this repo builds the
client assets; projects under this folder build against Eco's server reference
assemblies and deploy to an Eco dedicated server.

## Projects

- `AdvancedElectronics/` — the real mod (server half of the survey-drone feature).
  Registers the craftable `DroneDock` WorldObject and craftable `SurveyDroneItem`;
  inserting the drone item into the dock pairs them. A clean sibling of the spike
  (KTD1: the spike is a reference, not a base class), reusing its proven csproj shape,
  version pin, and registration pattern by imitation, not inheritance.
- `AdvancedElectronics.Spike/` — feasibility spike for the survey drone
  (`docs/plans/2026-07-11-002-feat-drone-feasibility-spike-plan.md`). Three admin chat
  commands probe the questions blocking the survey-drone plan; results are recorded in
  `docs/spikes/2026-07-survey-drone-spike.md`.

## Version matching

The `Eco.ReferenceAssemblies` NuGet version **must match the target server's game build**.

- This repo's ModKit DLLs identify as Eco **0.13.0.4** (`Eco.Shared.dll` version string),
  so the spike pins `0.13.0.4-beta-release-1024`. (The planning research assumed 0.11.x;
  the repo evidence superseded it.)
- To re-pin: find your server's build number (server console banner or
  `EcoServer.dll` version), list versions with
  `https://www.nuget.org/packages/Eco.ReferenceAssemblies`, and set `EcoRefVersion`
  in the `.csproj`.

## Building

Eco 0.13 reference assemblies target **net10.0**. If `dotnet --list-sdks` shows no 10.x
SDK, install one user-locally (no admin) with the official script:

```powershell
# Windows
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1
./dotnet-install.ps1 -Channel 10.0 -InstallDir "$env:USERPROFILE/.dotnet"
$env:PATH = "$env:USERPROFILE/.dotnet;$env:PATH"
```

Then:

```bash
dotnet build EcoServerMod/AdvancedElectronics.Spike
```

Note: Unity regenerates the root `.sln`/`.csproj` files for its own scripts and they are
git-ignored; this server project is deliberately outside that solution. Build it with
`dotnet` directly (or open the folder in your IDE), not via Unity's generated solution.

## Deploying

Copy `bin/Debug/net10.0/AdvancedElectronics.Spike.dll` into the server's
`Mods/UserCode/` directory, or create `AdvancedElectronics.Spike/Local.props`
(git-ignored) to auto-copy on every build:

```xml
<Project>
  <PropertyGroup>
    <EcoModsDir>C:\path\to\EcoServer\Mods\UserCode</EcoModsDir>
  </PropertyGroup>
</Project>
```

The spike's chat commands require **admin** authorization on the server
(`/admin add <you>` or server config).

## Object-UI picker findings (spike Q3, UI half)

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
- **Net finding (unchanged conclusion, corrected reasoning):** still no cheap,
  WorldObject-attachable, auto-generated single-selection district picker found in
  0.13.0.4. U4's `/drone district <name>` chat command remains the shipped mechanism
  (KTD4) — this was a bounded look, not an open-ended one, per the plan's own framing
  for this probe.
