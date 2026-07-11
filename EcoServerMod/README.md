# EcoServerMod — server-side mod code

Server half of the Advanced Electronics mod. The Unity project in this repo builds the
client assets; projects under this folder build against Eco's server reference
assemblies and deploy to an Eco dedicated server.

## Projects

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
