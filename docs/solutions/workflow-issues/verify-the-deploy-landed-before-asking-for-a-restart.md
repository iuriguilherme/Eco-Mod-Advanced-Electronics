---
title: "A deploy to the wrong tree succeeds silently, so verify the artifact landed before asking for a restart"
date: 2026-07-28
category: workflow-issues
module: EcoServerMod
problem_type: workflow_issue
component: tooling
severity: high
applies_when:
  - "Copying built artifacts to a game server or other external runtime by hand"
  - "More than one install of the same application exists on the machine"
  - "A tester restarts and reports that nothing changed"
  - "Taking a deploy path from a plan document, README, or previous session's notes"
tags: [eco-modding, deployment, live-testing, verification, workflow]
related_components: [EcoServerMod/AdvancedElectronics]
---

# A deploy to the wrong tree succeeds silently, so verify the artifact landed before asking for a restart

## Context

Live testing this mod means: build DLLs, copy them into the server's mod folder, ask the user to
restart, read the result. That loop is the only way to learn anything about client rendering, and
each turn of it costs a real person real time.

An entire cycle was burned because the copy went to a **different install of the same game**. The
path came from a stale plan document and was never checked. Every step reported success:

- `dotnet build` — 0 errors
- `cp` to the mod folder — exit 0, no warning
- `ls` afterwards — new timestamp, plausible size

The user restarted and reported: *"I restarted the server and nothing changed."* They had, correctly,
restarted the server they actually run. It was still executing the previous build.

The decoy was convincing: same vendor, same `Eco_Data/Server/Mods/<ModName>/` layout, real DLLs
already sitting in it from an earlier era. Nothing about the copy distinguishes a live install from
an abandoned one — the filesystem cannot tell you which process reads a directory.

## Guidance

**Verify the artifact landed in the tree that runs, and that it contains the new code, before asking
anyone to restart.**

Three checks, cheap enough to run every time:

**1. Confirm the target is the live install.** A server tree whose log directory has no entry from
today is not the one running. That single check would have caught this immediately:

```bash
ls -t "<server>/Logs/"*.log | head -1     # newest log — is it from this session?
```

**2. Confirm the binary contains the change.** A fresh timestamp only proves a copy happened, not
that it copied what you think. Grep the deployed artifact for a symbol that exists only in the new
build:

```bash
strings -el "<server>/Mods/<Mod>/<Mod>.dll" | grep -c "v11: rows now name"   # want 1
```

Use `strings -el` for .NET string literals — they are UTF-16, and plain `strings` silently misses
them. Type and member names live in metadata and are findable with plain `strings`.

**3. Remove the manual step entirely where the toolchain allows it.** This project's `.csproj`
already had a post-build copy target keyed on a property that nobody had ever set:

```xml
<Target Name="CopyModToEco" AfterTargets="Build" Condition="'$(EcoModsDir)' != ''">
```

Setting `EcoModsDir` in a git-ignored `Local.props` makes every build deploy itself, so build output
and deployed artifact cannot diverge. Machine-specific paths belong in that git-ignored file, never
in tracked files or commit messages.

## Why This Matters

The failure is silent in both directions. The deployer sees success; the tester sees no change.
Neither observation points at the copy, so the natural next move is to debug the code — which is
exactly the wrong place, because the code under investigation was never loaded. Cost is at minimum
one full restart cycle, plus however long is spent theorising about behaviour that no running process
ever exhibited.

It also poisons evidence. A "no change" result is indistinguishable from a genuine negative finding,
so it can be recorded as one. In a session already reasoning about which of several changes had an
effect, an un-deployed build is a false negative that survives into the notes.

Duplicate installs are normal, not exotic: a store copy, a standalone build, a dedicated-server
download, an old version kept for compatibility. Any of them will accept a copy without complaint.

## When to Apply

- Before every request for a restart or retest that costs someone else time.
- Whenever the deploy path came from a plan doc, README, or earlier session rather than from
  something checked this session. Paths go stale silently and outlive the machines they describe.
- When a tester reports "nothing changed" — check deployment **before** re-reading the code. It is
  the cheapest hypothesis and it is checkable in seconds.
- When a machine has more than one install of the target application.

## Examples

The check that closed the case, after the user supplied the correct path:

```
$ find "<server>" -iname "AdvancedElectronics*.dll" -printf "%TY-%Tm-%Td %TH:%TM  %p\n"
2026-07-27 22:08  .../Mods/AdvancedElectronics/AdvancedElectronics.dll
```

22:08 was the *previous* build. Two deploys made after it had gone to the other tree entirely, and
the running server had never seen them.

The evidence that identified the live install, from the client log rather than the filesystem — this
setup runs the server inside the client process, so its startup lines appear there:

```
LocalServer -  Loading AdvancedElectronics...
```

## Related

- `docs/solutions/workflow-issues/eco-mod-batched-live-testing.md` — batching probes into one restart;
  this doc protects the value of each restart that batching buys.
- `docs/solutions/workflow-issues/release-scripts-should-refuse-not-warn.md` — the same
  stale-artifact hazard on the packaging side, and the same conclusion: refuse rather than warn.
- `docs/solutions/runtime-errors/duplicate-asset-bundle-under-mods-aborts-startup.md` — a second case
  where the mod folder's contents, not the mod's code, were the fault.
