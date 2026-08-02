---
title: "Building Eco reference assemblies from source: four traps between a checkout and a usable DLL"
date: 2026-08-01
category: build-errors
module: EcoServerMod
problem_type: build_error
component: build_toolchain
severity: high
symptoms:
  - "MSB3246 Resolved file has a bad image, no metadata, or is otherwise inaccessible"
  - "CS0103 The name 'EcoVersion' does not exist in the current context"
  - "Hundreds of CS0246 on generated type names such as WallFormType and WindowFormType"
  - "A collected assembly is silently the wrong target framework"
root_cause: "Building the Eco server outside its solution skips prebuild steps that generate sources and resolve paths, and a source checkout ships LFS-backed binaries as pointer files."
resolution_type: workaround
applies_when:
  - "No Eco.ReferenceAssemblies package exists for the version the mod targets"
  - "Retargeting a mod to a new Eco release"
  - "Re-deriving reference assemblies after moving the Eco checkout to a different commit"
tags: [eco-modding, reference-assemblies, msbuild, git-lfs, code-generation, solutiondir, retarget, toolchain]
related_components: [EcoServerMod/AdvancedElectronics, scripts]
---

# Building Eco reference assemblies from source: four traps between a checkout and a usable DLL

## Problem

Retargeting the mod to Eco 0.14 needed reference assemblies that do not exist as a package, and
cannot be extracted from the shipped server because it is a single-file bundle with its managed
assemblies embedded. They have to be built from a source checkout. Four separate failures sit
between `dotnet build` and a usable set, none of which name their real cause.

## Symptoms

Each trap produces a different, misleading error:

```text
MSB3246: Resolved file has a bad image, no metadata, or is otherwise inaccessible.
         Unknown file format.  [Tools/Eco.VersionStampGit/Eco.VersionStampGit.csproj]

EcoVersionUtils.cs(16,78): error CS0103: The name 'EcoVersion' does not exist in the current context

Mods/__core__/Vehicles/Crane.cs(79,44): error CS0246: The type or namespace name
        'WallFormType' could not be found        (×426, all generated type names)
```

The fourth produces no error at all — a collected assembly is quietly built against the wrong
target framework.

## What Didn't Work

Reading each error at face value:

- MSB3246 reads as a corrupt or architecture-mismatched DLL, which sends you looking at the
  reference itself rather than at whether the file is real.
- CS0103 on `EcoVersion` reads as a missing file in the checkout, which it is — but it is missing
  because nothing has generated it *yet*, not because it was never committed.
- The CS0246 wave names types that exist nowhere in the tree, so grepping for `WallFormType` finds
  only its use site and suggests the checkout is incomplete.

Every one of these is a symptom of the build being run outside the conditions the Eco solution sets
up for it.

## Solution

Four fixes, in the order they bite. `scripts/gather-eco-refs.sh` encodes all of them.

**1. Fetch LFS objects.** A source checkout ships LFS-backed binaries as 131-byte pointer files
whose first line reads `version https://git-lfs.github.com/spec/v1`. MSBuild reports that as a bad
image.

```bash
git -C <eco-checkout> lfs pull
```

**2. Build twice.** Two prebuild steps generate C# during `AfterBuild`, after the compile item
globs have already been evaluated — so a cold checkout cannot succeed in one pass, by construction.
`Server/Eco.Prebuild.VersionStamp/Eco.Prebuild.VersionStamp.csproj:14` declares
`<Target Name="CustomBuild" AfterTargets="AfterBuild">`, and it is that target that writes the
`EcoVersion.cs` the first pass failed to find. The second pass sees it on disk.

**3. Pass `SolutionDir` explicitly.** The TechTree generator resolves every path from it
(`Server/Eco.PreBuild.TechTreeGenerator/Eco.PreBuild.TechTreeGenerator.csproj:11`):

```xml
<DestinationFolder>$(SolutionDir)Mods/__core__/AutoGen</DestinationFolder>
```

Building a `.csproj` directly leaves `SolutionDir` unset, so the generator writes its ~2,300 files
nowhere and the compile fails on every type they would have declared:

```bash
dotnet build "<eco>/Server/Eco.Mods/Eco.Mods.csproj" -c Release -p:SolutionDir="<eco>/Server/"
```

**4. Collect only the target framework you want.** Several projects multi-target, so the same
assembly name is produced more than once. Matching loosely lets the filesystem decide which copy
wins:

```bash
# Right: only net10.0*. Eco.Shared also builds net6.0; Eco.Networking.ENet also builds netstandard2.1.
find "$ECO_ROOT/Server" -path "*/bin/Release/net10.0*/ref/*.dll" -exec cp {} "$OUT/" \;
```

Reference assemblies land under a per-project TFM that varies — `Eco.Gameplay` under
`net10.0-windows`, `Eco.Core` under `net10.0` — so find them rather than assuming one path.

## Why This Works

The Eco server's build is a solution-level pipeline, not a set of independent projects. Two of its
steps are code generation wired to `AfterBuild`, and one of those resolves its output path from a
solution-scoped property. Building a project file directly satisfies neither precondition: MSBuild
runs the target, the generator runs, and it writes to an unrooted path — no error, because the
generator's `Exec` sets `IgnoreExitCode="true"`.

The two-pass requirement is not a workaround for a bug; it is inherent. Compile items are globbed
during evaluation, generation happens during execution, and no ordering within a single invocation
can make a file that does not yet exist appear in a list already computed.

`Server/Eco.ReferenceAssemblies/Eco.ReferenceAssemblies.csproj` shows what the finished set is meant
to look like — it packages `Build/EcoModkit/ReferenceAssemblies/*.dll`, populated by
`Scripts/Build_GatherReferenceAssemblies.ps1`. That script's `$scopes` list, which searches eight
candidate subpaths per project, is upstream's own acknowledgement that the output TFM varies.

## Prevention

**Encode the sequence in a script, not in a runbook.** All four steps are invisible from the error
messages and none are guessable. `scripts/gather-eco-refs.sh` fetches, double-builds, passes
`SolutionDir`, filters by TFM, and refuses to write a set of fewer than ten assemblies.

**Verify the output before trusting it.** Collecting the wrong TFM fails silently, so check the
count and the names:

```bash
find "$OUT" -name "*.dll" | wc -l          # fewer than expected means a project did not build
find "$OUT" -name "*.dll" -exec basename {} \; | sort | uniq -d   # duplicates mean the TFM filter is wrong
```

**Pin the commit and check it before building.** A checkout tracking a moving branch produces
irreproducible assemblies. The gather script reads the pinned SHA out of the mod's csproj — the
tracked single source of truth — and refuses a checkout that is not on it.

**Expect this to recur on every Eco update.** These are properties of upstream's build, not of one
version. Budget the four traps rather than rediscovering them.

## Related

- `docs/solutions/workflow-issues/the-compile-target-decides-what-exists.md` — the question that
  precedes this one: *which* version to build against. This doc is *how* to produce it once that is
  settled.
- `docs/solutions/conventions/document-the-path-you-actually-deploy-to.md` — the other half of a
  retarget, where the freshly built output has to end up.
