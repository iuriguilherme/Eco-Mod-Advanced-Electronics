---
title: "A second copy of a mod's .unity3d anywhere under Mods/ aborts Eco server startup"
date: 2026-07-27
category: runtime-errors
module: AdvancedElectronics
problem_type: runtime_error
component: tooling
severity: high
symptoms:
  - "Eco server fails to start with System.ArgumentException: An item with the same key has already been added. Key: <ModName>.unity3d"
  - "The named bundle path in the error is the CORRECT install, so the error points at the innocent copy"
  - "Appears immediately after updating a mod, or after moving an old build into a backup folder inside Mods/"
root_cause: config_error
resolution_type: config_change
applies_when:
  - "Updating a deployed Eco mod that ships a client asset bundle"
  - "Keeping a backup or disabled copy of a mod anywhere beneath the server's Mods/ directory"
  - "Designing the folder layout of a release archive for an Eco mod"
tags: [eco-modding, deployment, asset-bundle, server-startup, packaging, release]
related_components: [EcoServerMod/AdvancedElectronics]
---

# A second copy of a mod's .unity3d anywhere under Mods/ aborts Eco server startup

> **Path convention.** Paths beginning `Mods/` refer to the **Eco server's**
> `Eco_Data/Server/Mods/` directory, not to anything in this repository. Paths beginning
> `scripts/`, `docs/` or `EcoServerMod/` are in-repo.

## Problem

Eco registers mod asset bundles in a dictionary keyed by **filename**, scanning `Mods/` and every
subdirectory beneath it. Two files named `AdvancedElectronics.unity3d` anywhere under that tree —
regardless of which folders they sit in — collide on insert and take the whole server down at
startup.

## Symptoms

From the server log (`Logs/log_*.log`), at startup:

```
[Error] [Eco] Failed to start the server. Exception was Exception: ArgumentException
System.ArgumentException: An item with the same key has already been added. Key: AdvancedElectronics.unity3d
Outer Exceptions:
One or more errors occurred. (An error occurred adding a mod asset bundle (.unity3d) file to the map:
AdvancedElectronics.unity3d. Located at
C:\...\Eco_Data\Server\Mods\UserCode\AdvancedElectronics\AdvancedElectronics.unity3d.)
```

The server does not start at all — this is an abort, not a degraded load.

**The most misleading part: the path in the message is the copy you just installed correctly.**
It names the file being added when the dictionary rejected it, not the pre-existing entry that
occupies the key. Investigating the named path finds nothing wrong with it, because nothing is.

## What Didn't Work

- **Moving the old build into a folder named `Ignore/`.** This was the actual cause in the observed
  case: a previous deploy had been parked in `Mods/Ignore/`, on the reasonable assumption that the
  name would be honoured. Eco has no such convention — `Ignore` is scanned exactly like `UserCode`
  or any other subdirectory. Names such as `old`, `backup` or `disabled` fail the same way.
- **Reading the path in the error message as the problem.** It is the victim, not the culprit.
- Eco's own `Mods/README.md` states the scanning rule plainly — *"You can put them in this directory
  directly or in any sub-directories"* — but it says it about **pre-compiled `.dll` mods**. It is
  easy to read that as a permission for DLLs and miss that asset bundles are swept by the same
  recursive walk, with the stricter consequence of a unique-filename constraint.

## Solution

**Find every copy and remove all but one from the tree entirely:**

```bash
find "<server>/Eco_Data/Server/Mods" -name "<ModName>.unity3d"
```

Anything that is not the live install must move **out of `Mods/`** — not into a subfolder of it.
Renaming the *folder* changes nothing; only the file's presence under `Mods/` matters. (Renaming the
*file* would technically clear the key collision, but leaves a stale bundle being loaded, so it is
not a fix.)

**Then remove the shape that invites the duplicate.** The release archive originally carried a full
`Mods/UserCode/<ModName>/` prefix so it could be extracted over the server root. That prefix invites
extracting *inside* `Mods/UserCode/`, which silently produces
`Mods/UserCode/Mods/UserCode/<ModName>/` — a second copy, and therefore a startup abort. The archive
now contains a single `<ModName>/` folder that the admin drops into `Mods/UserCode/` (see
`scripts/package-release.sh`):

```bash
# staging: one folder, no server-path prefix
MODDIR="$STAGE/AdvancedElectronics"
mkdir -p "$MODDIR"
```

## Why This Works

The collision is on a **global filename key**, not on a path. Eco flattens every `.unity3d` it finds
beneath `Mods/` into one map so it can serve bundles to connecting clients by name — which is also
why a mod's bundle filename must be unique server-wide, across mods, not merely within its own
folder. Once the key is unique again, startup proceeds.

Removing the archive's path prefix attacks the same failure a step earlier. A zip that encodes an
absolute-ish destination has exactly one correct extraction point and several plausible wrong ones,
and every wrong one produces a nested duplicate. A zip containing a single mod folder has no wrong
extraction point that yields a second bundle: the admin either puts the folder in the right place or
the mod does not load at all — a loud, obvious failure instead of a cryptic startup abort.

## Prevention

- **Ship release archives without a server-path prefix.** One folder, dropped where the admin knows
  mods live. The prefix buys a marginally shorter instruction and costs a whole class of duplicate.
- **State the rule in the release README, with the exact exception text** so an admin who hits it can
  search for it. Both this repo's `README.md` and the generated `README.txt` in the release archive
  now carry it, plus an explicit "delete the old copy first" step in the update path.
- **Keep backups outside `Mods/`.** Any folder inside it is live, whatever it is named. This is worth
  saying out loud in install docs, because parking an old build in a "disabled" folder is the natural
  instinct and it is wrong here.
- **When a startup error names a file path, check for other copies of that filename before
  investigating the path.** For any "same key has already been added" error, the message identifies
  the *duplicate*, never the *incumbent*; the incumbent is what you actually need to find.
- Note that the same recursive sweep loads stray **DLLs** too. A retired `<ModName>.Spike.dll` left
  in a backup folder under `Mods/` is loaded and registers its chat commands — a quieter version of
  the same mistake, and one that does not announce itself with a crash.

## Related

- `docs/solutions/workflow-issues/eco-mod-batched-live-testing.md` — this failure is only visible on
  a real server start, which is the class of problem that discipline exists for.
- `docs/solutions/runtime-errors/worldobject-zero-size-blocks-placement.md` — the sibling
  "deployment looks correct, the game disagrees" runtime error, on the client side rather than at
  server startup.
