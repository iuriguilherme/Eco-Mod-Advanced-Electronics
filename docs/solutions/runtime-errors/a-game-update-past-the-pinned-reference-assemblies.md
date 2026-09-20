---
title: "A game update past the pinned reference assemblies breaks the mod at runtime, not at build"
date: 2026-09-19
category: runtime-errors
module: EcoServerMod
problem_type: runtime_error
component: build_toolchain
severity: high
symptoms:
  - "Every chat command of the mod fails with System.MissingMethodException: Attempted to access a missing method"
  - "The stack trace names a mod method that has not been touched in weeks, and no Eco method at all"
  - "The mod builds with zero errors and the whole unit suite passes while the server is unusable"
  - "Features that never went near a chat command stop silently, because the same engine call sits under them"
root_cause: config_error
resolution_type: environment_setup
tags: [eco-modding, reference-assemblies, versioning, binary-compatibility, optional-parameter, missingmethodexception, retarget, toolchain]
related_components: [EcoServerMod/AdvancedElectronics, scripts]
---

# A game update past the pinned reference assemblies breaks the mod at runtime, not at build

## Problem

The dedicated server updated itself to Eco 0.14.1.1 while the mod was still compiled against
reference assemblies built from a 0.14.0 commit. Every `/drone` command died with
`MissingMethodException`, although the mod's source was correct, built clean, and passed its
whole test suite.

## Symptoms

The server log carried the same trace for each command, naming a mod method rather than
anything in the engine:

```
System.Reflection.TargetInvocationException: Exception has been thrown by the target of an invocation.
 ---> System.MissingMethodException: Attempted to access a missing method.
   at Eco.Mods.TechTree.DroneCommands.FindNearestAuthorizedDock(User user)
   at Eco.Mods.TechTree.DroneCommands.Farm(User user)
```

`FindNearestAuthorizedDock` is old, shared code: every `/drone` subcommand starts by calling it,
so the whole command surface failed at once. It had not been edited in weeks, and the method it
is blamed for is one line inside it (`EcoServerMod/AdvancedElectronics/DroneCommands.cs:779`).

Nothing else reported a fault. `dotnet build` was clean, `dotnet test` was green, and the
client showed a working mod: its tabs, items and world objects all loaded. The one honest signal
was the version line at the top of the server log, `Eco Server 0.14.1.1 beta`, against a
`<EcoRefSha>` pinned at a 0.14.0 commit.

## What Didn't Work

**Reading the mod's own diff.** The failing method was untouched, so its history says nothing.
Neither does the mod's build output: the compiler resolved the call against the reference
assemblies on disk, which still held the old engine, and had no reason to object.

**Trusting the test suite.** The unit suite covers the Eco-free core project only. That project
cannot reference an Eco type at all, so no amount of green there can speak to an engine call.

**Treating it as a mod defect at all.** The exception names a mod method, which invites a search
through mod code. The missing method belongs to the *engine*, and the frame is simply where the
call sits.

## Solution

The engine's method gained an optional parameter between the pinned commit and the running
server:

```csharp
// pinned 0.14.0 commit (Server/Eco.Gameplay/Objects/WorldObject.cs:950)
public bool IsAuthorized(User user, AccessType requiredAccess)

// v0.14.1.1-beta (Server/Eco.Gameplay/Objects/WorldObject.cs:985)
public bool IsAuthorized(User user, AccessType requiredAccess, WorldObjectComponent componentScope = null)
```

The mod calls it as `dock.IsAuthorized(user, AccessType.FullAccess)`
(`EcoServerMod/AdvancedElectronics/DroneCommands.cs:779`). No source change was needed: that
call compiles against either version. Only the binding had to be redone against the engine the
server actually runs.

Re-pin and rebuild the reference assemblies:

1. Find the tag matching the running server. The server log's first lines name the version
   (`Eco Server 0.14.1.1 beta`); the Eco source checkout has a tag per release
   (`git tag --sort=-creatordate`).
2. Get a tree at that tag without disturbing the checkout, which may be on a branch with local
   edits: `git worktree add --detach <path> v0.14.1.1-beta`.
3. Set `<EcoRefSha>` in `EcoServerMod/AdvancedElectronics/AdvancedElectronics.csproj` to that
   tag's commit. The csproj is the single source of truth for the pin, and
   `scripts/gather-eco-refs.sh` refuses to run against a checkout that does not match it.
4. Run `scripts/gather-eco-refs.sh <worktree path>` and point `EcoRefAssembliesDir` in the
   git-ignored `Local.props` at the directory it prints.
5. Rebuild the mod and redeploy.

Then check the whole-file vanilla overrides under `EcoServerMod/UserCode/`. Each is a copy of an
upstream file frozen at the version it was copied from, so an upstream edit between the two
versions is silently reverted by the override. `git diff <old pin> <new tag> -- <the vanilla
original>` per override answers it; both overrides were unchanged upstream in this case.

Verified live: after the rebuild and a restart, `/drone farm` ran and printed its output. The
work is commit `build: pin the Eco reference assemblies to 0.14.1.1`, local and unpushed on
`feat/tech-tree-icons` at the time of writing.

## Why This Works

**An optional parameter is source-compatible and binary-incompatible.** C# resolves default
arguments at the call site: code calling `IsAuthorized(user, access)` against the new engine
compiles into a call to the three-parameter method with the default filled in. A binary compiled
against the old engine still asks for the two-parameter method, by its full signature. The
runtime has no such method, and the CLR raises `MissingMethodException` at the moment the call
is reached — not at load, and not for the assembly as a whole.

That timing is what makes it look like a mod bug. The failure is per call site, so it arrives
one feature at a time, wearing the name of whichever mod method happened to make the call.

**Nothing in the local toolchain can see it.** The compiler reads the reference assemblies on
disk; the tests exercise the Eco-free project. Both are answering a different question from
"does the method this will bind to exist on the server that will run it".

**The version gap opens by itself.** The dedicated server is a Steam install that updates
without being asked, while `<EcoRefSha>` only moves when someone edits it. So the two drift
apart on the game's release schedule rather than on the repo's, and the mod keeps building
happily against an engine nobody runs any more.

## Prevention

- **After any Eco update, compare two numbers before debugging anything else.** The version at
  the top of the server log, and the release tag `<EcoRefSha>` points at. A mismatch explains a
  `MissingMethodException` on its own, and explains it better than any mod-side theory.
- **Treat `MissingMethodException` naming a mod method as a version symptom, not a mod bug.**
  The frame is the call site; the missing member is the engine's. Look at the pin first.
- **Re-pin from a worktree at the release tag, never by moving the source checkout.** That
  checkout is shared and may carry local work; `git worktree add --detach` gives a clean tree at
  the tag and leaves it alone.
- **Re-check every whole-file `.override` in `EcoServerMod/UserCode/` on every retarget.** Diff
  each one's vanilla original between the old pin and the new tag. An override frozen at an old
  version silently undoes whatever upstream changed in that file, and nothing reports it.
- **Expect a green build and a green suite to say nothing about this class of break.** The only
  proof is the running server: exercise the surfaces that call into the engine after a retarget,
  which for this mod means the chat commands and one dispatch of each drone job.
- **A mod-side workaround is the wrong shape here.** Nothing in the mod's source was wrong, so
  there is nothing to fix in it; re-binding is the whole repair.

## Related

- `docs/solutions/build-errors/building-eco-reference-assemblies-from-source.md` — how the
  reference assemblies are built once the pin has moved, and the traps in that build. This doc
  covers when and why to move the pin; that one covers doing it.
- `docs/solutions/workflow-issues/the-compile-target-decides-what-exists.md` — the design-time
  half of the same rule: what the mod can call is decided by the compile target, not by whatever
  source tree is open. This is what happens when the *runtime* target moves out from under it.
- `docs/solutions/workflow-issues/a-timestamp-says-when-a-file-was-written-not-what-is-in-it.md`
  — why the reference assemblies' file dates cannot confirm which version they hold.
