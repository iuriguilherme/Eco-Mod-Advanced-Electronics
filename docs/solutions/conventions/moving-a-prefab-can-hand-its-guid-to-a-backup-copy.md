---
title: "Moving a prefab can hand its GUID to a backup copy, and the scene keeps pointing at the backup"
date: 2026-08-07
last_updated: 2026-09-22
category: conventions
module: Assets
problem_type: convention
component: asset-pipeline
severity: critical
applies_when:
  - "Reorganising Assets/ into subfolders, or moving a prefab for any reason"
  - "Keeping an Old* or backup copy of a prefab beside the live one"
  - "A fix is committed and verified but does not appear in game after a bundle rebuild"
  - "Auditing what a ModkitPrefabContainer will actually ship"
tags: [unity, guid, prefab, asset-bundle, modkit, silent-failure, name-match]
related_components: [Assets/Art/AdvancedElectronics/Scenes/AdvancedElectronicsScene.unity, Assets/Art/AdvancedElectronics/Prefabs]
---

# Moving a prefab can hand its GUID to a backup copy, and the scene keeps pointing at the backup

## Context

**Status: the misbinding this documents is resolved, and the rule stands.** It was closed by
`aac18e3` (2026-08-08, on `main`) — **the day after this document was written**. That commit's
subject is *"feat(art): re-export the HRVSTR chassis and mask the propeller layer"*, and the
deletion is one clause in its body: *"Drop the superseded `Old*` prefab copies and the dock's
placeholder pad edits."* Nine files went with it. Worth noticing, because a reader searching
history for why the backups vanished would not find that commit by its subject — the fix rode
along with an unrelated art re-export and announced nothing.

Re-checked 2026-09-06 — no `Old*` prefab remains under `Assets/Art/AdvancedElectronics`, the five
live prefabs are all present (`AdvancedElectronicsAssemblyObject`, `DroneDockObject`,
`HarvestDroneObject`, `MiningDroneObject`, `SurveyDroneObject`), and
`scripts/validate-name-match.sh` reports `PASS`. In particular `MiningDroneObject`, which the
table below records as shipping nowhere at all, is present and bound. The incident is kept
because it is what produced the rule and because the GUID-resolution loop under **Guidance** is
how you would catch it again.

Run that loop today and it does not come back clean, which is worth knowing before you read a
dirty result as a fresh misbinding. The container holds ten entries: the five live prefabs
above, each bound correctly, and five that resolve to no asset whatsoever. Those five are the
very GUIDs in the table below — the identities the `Old*` copies had inherited, plus the one no
asset ever owned. Deleting the backups ended the misbinding, because a slot pointing at nothing
ships nothing, but it left the dead slots in the list. They are residue rather than a defect,
and clearing them out of the container is what would let a clean result mean something again.

The gap between those two dates is the reason
`docs/solutions/workflow-issues/a-fixed-defect-in-the-present-tense-passes-every-check.md` exists.
This document described a resolved state as current for thirty days, through a refresh pass
(`3bd0cf1`, *"refresh the logic-errors learnings against the tree"*, 2026-09-05) that edited the
paragraph immediately above the table and left the table itself untouched, because every check
that pass ran asks whether a citation resolves and none asks whether the prose is still true.
That SHA was branch-local when this paragraph was written; `feat/tech-tree-icons` has since
merged in `d35c722` without squashing, so it is reachable from `origin/main` and stable.

The art folder was reorganised into per-kind subfolders (`Prefabs/`, `Icons/`, `Materials/`,
`Models/`, `Animators/` — the layout has shifted again since, and `Icons/` now sits under
`Sprites/`), and backup copies of several prefabs were kept alongside the live ones under
`Old*` names. Everything looked right afterwards: every asset had its `.meta`,
every prefab filename still matched its server class, and `scripts/validate-name-match.sh`
reported `PASS`.

The scene's `ModkitPrefabContainer` — the list that decides what a bundle actually ships —
had quietly been repointed at the backups.

## Guidance

**A `.meta` GUID is the asset's identity, and a move can reassign it.** Unity normally
carries a GUID with its asset through a move. It did not here: three prefabs came out of the
reorganisation with fresh GUIDs, and the GUIDs they vacated ended up on the `Old*` backup
copies that were created in the same operation.

```
DroneDockObject.prefab.meta      8da7e182…  ->  1ce5ff3f…   (new)
OldDroneDockObject.prefab.meta               ->  8da7e182…   (the live prefab's old identity)
```

**Every reference is by GUID, so the references followed the identity, not the name.** The
scene's container list still held the original GUIDs, which now belonged to the backups:

The bindings as they stood during the incident — every `Old*` target below has since been
deleted:

| Container slot | Intended | Resolved to, at the time |
|---|---|---|
| `8da7e182…` | `DroneDockObject` | `OldDroneDockObject` |
| `3adcb668…` | `SurveyDroneObject` | `OldSurveyDroneObject` |
| `ca3dc372…` | `HarvestDroneObject` | `OldHarvestDroneObject` |
| `067689352…` | — | `OldOldSurveyDroneObject` |
| `914037f5…` | — | nothing; no asset owns it |

`MiningDroneObject` appears nowhere in the list, so it would not ship at all.

**This is silent because the stale GUIDs still resolve.** Four of the five point at real,
loadable prefabs — just the wrong ones. Unity has nothing to complain about: no missing
reference, no console error, no failed import. The one dangling GUID is the only entry that
could produce a warning, and a single warning among a working-looking list reads as noise.

**The symptom is a fix that does not appear in game.** Server code lands, tests pass, the
bundle builds clean, and the object in game behaves as if none of it happened — because the
client is running last month's prefab. That failure looks like a server bug, or a caching
problem, and neither trail leads anywhere.

**Check the container's GUIDs after any asset move.** Names are not the binding; comparing
filenames proves nothing. Resolve each entry to the file that owns it:

```bash
# What does each ModkitPrefabContainer entry actually point at?
grep -A 10 '^  Prefabs:' Assets/<YourScene>.unity |
  grep -o 'guid: [a-f0-9]*' | awk '{print $2}' |
  while read g; do
    printf '%s -> %s\n' "$g" "$(grep -rl "$g" Assets --include=*.meta | head -1)"
  done
```

Anything resolving to an `Old*` file, or to nothing, is a slot that will ship the wrong asset
or no asset.

**Prefer a fresh GUID for the backup, not for the live asset.** If a copy must be kept, make
the *copy* the new identity. The live asset keeping its GUID is what preserves every existing
reference for free.

## Why This Matters

The whole point of the `Old*` copies was caution — keep the previous version in case the new
one is wrong. The caution inverted: the backups inherited the identities, so the project
shipped exactly the versions that were meant to be retired, and did it without a single
diagnostic.

It also defeats the checks that exist. `validate-name-match.sh` compares server class names
against client asset *filenames* and passes, because the filenames are all correct. The
binding it verifies and the binding that broke are different bindings — file naming versus
GUID identity — and nothing in the repo checked the second one.

The cost lands at the worst moment: after a branch of real fixes is finished and deployed,
when the natural conclusion from "nothing changed in game" is that the fixes were wrong.

## When to Apply

- After moving or renaming anything under `Assets/`, before rebuilding a bundle.
- Whenever an `Old*`, `Backup*`, or duplicated prefab is created next to a live one.
- When a verified change does not show up in game — check the container before re-reading the
  server code.
- When adding a new world object: confirm it is actually in the container, not merely present
  on disk. A prefab no container references is invisible to the build.

## Related

- `docs/solutions/conventions/eco-custom-worldobject-placement-requirements.md` — the naming
  triad that binds a prefab to its server class. That binding is by *name*; this one is by
  *GUID*. Both must hold, and passing the name check says nothing about the GUID check.
- `docs/solutions/logic-errors/prefab-finisher-writes-to-the-scene-object-name.md` — the other
  way this project has shipped the wrong prefab: the tool wrote to a path derived from the
  scene object's name. Same outcome, different mechanism, and worth reading together as the
  two halves of "the asset you built is not the asset that shipped".
- `docs/solutions/workflow-issues/an-accepted-deviation-is-indistinguishable-from-drift.md` — the
  general form of the five dead slots above. They are accepted residue, and the only place that
  is written down is this document; the scene file itself says nothing, so an auditor resolving
  those GUIDs meets five misses with no marker beside them. That entry is about what it costs
  when the acceptance lives outside the artifact.
