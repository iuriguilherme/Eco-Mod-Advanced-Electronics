---
title: "Document the install path you actually deploy to, not the one you assume"
date: 2026-07-31
category: conventions
module: EcoServerMod
problem_type: convention
component: development_workflow
severity: medium
applies_when:
  - "Writing or reviewing install, deploy, or update instructions for a distributed artifact"
  - "A build target auto-deploys somewhere, and the docs describe a manual path separately"
  - "Preparing a release whose install instructions have never been followed end to end"
  - "The same path appears in more than one tracked file"
tags: [documentation, release, deploy-path, eco-modding, untested-instructions, single-source-of-truth]
related_components: [EcoServerMod/AdvancedElectronics, scripts]
---

# Document the install path you actually deploy to, not the one you assume

## Context

This mod's install instructions told server owners to extract the release into
`Mods/UserCode/`, relative to the Eco dedicated server install (paths in this section live in
that install, never in this repo). Every actual deployment went to `Mods/AdvancedElectronics/`
— that is what the build's auto-deploy target writes to, what the live test server has been
running against for weeks, and the only layout the mod has ever been verified in.

Both statements had been true-looking for a long time. The documented path was wrong from the
first release and nothing surfaced it, because **nobody ever followed the documented path.**
Developers deploy through the build target; only a stranger installing from the zip would ever
execute the written instructions, and by then the mistake is theirs to hit.

Four tracked files carried the wrong path: the packaged `README.txt` (install, uninstall, and a
header comment), `EcoServerMod/README.md` (deploy section and its config example), and the
`CopyModToEco` comment in `EcoServerMod/AdvancedElectronics/AdvancedElectronics.csproj`.

For the record on the specific claim, since it is the sort of thing that reads as arbitrary:
`Mods/UserCode/` is where Eco expects **source-code** mods it compiles at runtime. A mod
shipping compiled DLLs plus an asset bundle belongs in its own folder directly under `Mods/`.

## Guidance

**Treat the auto-deploy target as the single source of truth for the install path.** If a build
target copies artifacts somewhere on every build, that destination is the tested path by
definition. Documentation is a second, untested copy of the same fact, and the two drift silently
because only one of them is ever executed.

**Derive the documented path from the deploy configuration, or check it against it.** In this
project the build's copy target reads a machine-local, git-ignored property for the destination.
That value cannot be committed — it is an absolute path on one machine — but the *shape* under
the server root can and should agree with what every document says. When it doesn't, the document
is wrong; the thing that runs is right.

**A path repeated across files is a path that will drift.** Four files said `UserCode`; fixing one
would have left three. When the same fact appears in more than one tracked file, either
consolidate it or expect to grep for it:

```bash
# every tracked mention of the install path, before a release
grep -rn "Mods/UserCode\|Mods\\\\UserCode" --include="*.md" --include="*.sh" \
  --include="*.csproj" --include="*.props" .
```

**Read the packaged artifact, not the source that generates it.** The release README is written by
a heredoc inside the packaging script. Reviewing the script is not the same as reading what a
downloader receives — extract the zip, or read the file out of it:

```bash
python3 - <<'PY'
import zipfile
z = zipfile.ZipFile('dist/AdvancedElectronics-0.0.2-eco0.13.0.4.zip')
print(z.read('AdvancedElectronics/README.txt').decode('utf-8'))
PY
```

**Instructions nobody has executed are not documentation, they are a hypothesis.** The strongest
version of this convention is to install from the release zip onto a clean server once per release,
following the written steps literally. Short of that, at minimum reconcile every documented path
against the deployment that is actually being tested.

## Why This Matters

The failure mode is asymmetric and delayed. The people who could catch the error never run the
instructions, and the people who run the instructions have no way to know they are wrong — they
get a mod that does not load, with no error naming the cause. For an alpha mod whose README already
warns about save corruption, an install path that quietly does nothing is the difference between
"this is rough" and "this is broken."

It also belongs to a family this project keeps meeting: **a check or claim that cannot fail because
nothing exercises it.** A validation gate whose discovery step matched almost nothing reported PASS
on a dirty tree. A boot verification passed repeatedly against a world containing none of the
objects whose initialization was broken. Untested install instructions are the same shape — an
assertion with no execution path behind it, which therefore stays green until a stranger disproves
it.

The cost of prevention is a single grep and one clean-server install; the cost of the failure is
paid by a user who cannot debug it.

## When to Apply

- Before any release whose install instructions changed, or whose artifact layout changed
- When adding a second place that names a deploy path, a port, a folder, or a filename
- When a build target and a document both describe where something goes
- During review of a packaging script — read the output it produces, not only its source
- When onboarding docs describe a setup the team no longer performs by hand

## Examples

The corrected instruction, and why the old one looked plausible:

```text
# before -- never executed by anyone on the project
2. Extract this zip and put the AdvancedElectronics folder into
   Eco_Data/Server/Mods/UserCode/ , so you end up with
   Mods/UserCode/AdvancedElectronics/ containing:

# after -- the layout the build deploys to and the server runs
2. Extract this zip and put the AdvancedElectronics folder into
   Eco_Data/Server/Mods/ , so you end up with
   Mods/AdvancedElectronics/ containing:
```

The reconciliation check, comparing what the build deploys against what the docs claim:

```bash
# where the build actually copies to (machine-local value, git-ignored)
grep -n "EcoModsDir" EcoServerMod/AdvancedElectronics/Local.props

# what every tracked document tells a user to do
grep -rn "Mods/" --include="*.md" --include="*.sh" --include="*.csproj" . \
  | grep -v "^./.references/"
```

If the tail of the first path and the folder named in the second disagree, the documents are wrong.

## Related

- `docs/solutions/workflow-issues/verify-the-deploy-landed-before-asking-for-a-restart.md` — the
  runtime half of the same instinct: confirm the artifact under test is the one running, rather
  than assuming the copy happened.
- `docs/solutions/workflow-issues/release-scripts-should-refuse-not-warn.md` — the same packaging
  script's other rule, and why its staleness guard fails closed.
- `docs/solutions/workflow-issues/a-gate-that-discovers-nothing-passes-everything.md` — the general
  form: a check that cannot fail reports success. Untested instructions are that shape without any
  check at all.
