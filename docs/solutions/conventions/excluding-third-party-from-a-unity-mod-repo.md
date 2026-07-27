---
title: "Excluding a redistribution-restricted SDK from a Unity mod repo without breaking a fresh clone"
date: 2026-07-27
category: conventions
module: AdvancedElectronics
problem_type: convention
severity: high
applies_when:
  - "Publishing a Unity project that depends on an SDK you are not allowed to redistribute"
  - "Deciding which Unity directories belong in version control and which are third-party or generated"
  - "A fresh clone of the repo will not open, will not build, or opens with missing components"
tags: [unity, gitignore, third-party, redistribution, guid, repo-hygiene, eco-modding, modkit]
related_components: [EcoServerMod/AdvancedElectronics]
---

# Excluding a redistribution-restricted SDK from a Unity mod repo without breaking a fresh clone

## Context

Making this repository public created a tension with two hard sides. The Eco ModKit and Eco's client
libraries are distributed by Strange Loop Games from their website, behind an account that owns the
game — there is no public download URL, so mirroring them into a public repo would route around an
ownership gate. But a repo nobody else can build is not really published either.

The instinct — "add the SDK folders to `.gitignore` and move on" — is right in direction and wrong in
three specific ways, each of which was live in this repo before the cleanup and none of which
announces itself. The rules had accumulated as prefix globs written at different times, and nobody
had cloned the result to see whether it worked.

## Guidance

**1. Exclude third-party by explicit path, never by prefix glob.**
A rule like `Assets/Eco*` reads as "the SDK folders" but is a substring match. It over-matches
anything the project might later add under that prefix, and it under-matches by accident: in this
repo `Assets/Eco.Client.asmdef` — the client assembly definition that *our own* scripts compile into
— was covered only because its name happened to start with `Eco`. Narrowing to explicit paths
promptly exposed it as an unlisted dependency. List each excluded path, with a comment saying whose
it is and why it is not vendored.

**2. Project configuration is yours, not the SDK's — track it.**
`ProjectSettings/` and `Packages/manifest.json` + `Packages/packages-lock.json` define the Unity
version, render pipeline, tags, layers and package set. A clone cannot reproduce the build without
them. Ignoring them is inert for files already tracked, which is exactly why the mistake survives:
the ignore silently swallows only *new* files, so it looks harmless right up until a new setting
does not travel. Track the configuration; ignore only the SDK's own embedded packages inside
`Packages/`, and per-package editor tool state (which carries machine-local paths).

**3. Know that your tracked assets reference the excluded tree by GUID.**
This is the trap that makes "just download the SDK" insufficient advice. Unity resolves every asset
reference through the GUID in its `.meta` file. Prefabs you track will reference GUIDs owned by
`.meta` files inside the folders you excluded. If a contributor obtains the SDK any way that
regenerates those `.meta` files — copying files loose, re-importing, extracting from a source that
does not preserve them — Unity mints **fresh** GUIDs, and your tracked prefabs silently lose the
components they referenced. No error at import, build, or load; the mod simply does nothing.

So the setup instruction is not "get the SDK" but "get *the official distribution*, which carries
the original `.meta` files", plus a check the contributor can run:

```bash
grep guid Assets/EcoModKit/Scripts/WorldObject.cs.meta
# expected: guid: 22281bf2bb54279449ac8e3fbf199314
```

Pin one known GUID from the excluded tree in the README and let the contributor verify before they
open the editor. It converts a silent failure into a one-line check. (That 32-hex value is a Unity
asset GUID, not a git commit hash — they are indistinguishable by shape, and tooling that scans docs
for commit references will flag it.)

**4. Give contributors a path that needs none of it.**
Here the server half is a separate .NET project resolving Eco through a NuGet reference-assembly
package, so it builds, tests and runs from a bare clone with no Unity and no SDK. Say that first in
the setup docs: most contributions touch only that half, and telling them up front that the whole
restore section is skippable is the difference between a repo that looks approachable and one that
looks gated.

**5. Verify by cloning, not by reasoning.**
The rules are only correct if a clone actually works, and that is cheap to test:

```bash
git clone --branch main <repo> /tmp/clonetest && cd /tmp/clonetest
git ls-files | grep -iE "EcoModKit|EcoLibs|ThirdParty|strangeloopgames|TextMesh|Eco\.Client"  # must be empty
dotnet build <server-project> && dotnet test <test-project>
```

Two assertions, both mechanical: nothing third-party is tracked, and the no-SDK path builds and
passes. Keep the audit grep — it is the regression test for rule 1, and the failure it catches
(accidentally committing someone else's code to a public repo) is one you very much want to catch
before a push rather than after.

## Why This Matters

Each of the three mistakes fails quietly and on someone else's machine. An over-broad ignore hides a
file you needed; a wrongly-ignored `ProjectSettings/` only bites when a new setting fails to travel;
a GUID mismatch produces a mod that builds cleanly and does nothing. None of them is visible from
inside a working checkout, which is the whole problem — the repo *works for you* in every case.

The GUID hazard is the one worth internalising beyond Unity: **when you exclude a dependency that
your tracked artifacts reference by an identity that dependency owns, "install the dependency" is
not sufficient instruction.** The contributor has to obtain it in a way that preserves those
identities, and you have to give them a way to confirm they did.

There is also a licensing dimension that the audit grep makes concrete. "We don't redistribute their
code" is a claim about the repository's contents, and a claim of that kind should be mechanically
checkable rather than believed.

## When to Apply

- Before making a repo public that has ever had an SDK, asset pack, or vendored library inside the
  working tree.
- When writing or reviewing `.gitignore` rules for a Unity project — check every prefix glob for
  what else it catches and what it catches only by luck.
- Whenever setup instructions say "download X and put it here": ask what happens if the contributor
  gets a *different but plausible* copy of X, and whether they would find out.
- After any `.gitignore` change: re-run the clone test. A rule that newly exposes or newly hides a
  file is invisible in the working tree.

## Examples

The shape of an explicit exclusion block — each entry names an owner and a reason, so the next reader
does not have to guess whether a path is third-party or simply stale:

```gitignore
# Third-party content — deliberately NOT redistributed here.
# These ship with the vendor's SDK distribution, not with this repo.
#
# IMPORTANT: our tracked prefabs reference assets in these folders BY GUID, so
# the restored copy must be the official distribution (which carries the
# original .meta files). A hand-copied or re-imported tree gets fresh GUIDs and
# silently strips components off our prefabs.
/Assets/EcoModKit/
/Assets/EcoLibs/
# The client assembly definition. Scripts under Assets/ with no nearer asmdef
# compile into it -- including ours -- so it must be restored too.
/Assets/Eco.Client.asmdef
/Packages/com.strangeloopgames.*/

# NOTE: ProjectSettings/, Packages/manifest.json and Packages/packages-lock.json
# are intentionally TRACKED. They are this project's own configuration and a
# clone cannot reproduce the build without them.
```

Before and after, on this repo:

```
before:  ProjectSettings, Packages, and Assets/Settings ignored (inert for already-tracked
         files, silently swallowing anything new); third-party matched by prefix globs;
         Assets/Eco.Client.asmdef excluded only by accident; never cloned to check.

after:   third-party listed explicitly with reasons; project configuration tracked;
         GUID hazard documented with a verification command; clone test run —
         no third-party tracked, server built with 0 errors, 68/68 tests passing.
```

## Related

- `docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md` — what the excluded
  SDK actually gives you access to on the client side.
- `docs/solutions/logic-errors/prefab-finisher-writes-to-the-scene-object-name.md` — the other way a
  tracked prefab silently stops matching what the game expects, by name rather than by GUID.
