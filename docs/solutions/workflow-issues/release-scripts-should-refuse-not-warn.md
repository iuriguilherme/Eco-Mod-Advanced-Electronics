---
title: "A release script should refuse to package a stale artifact, not warn about it"
date: 2026-07-27
last_updated: 2026-09-05
category: workflow-issues
module: AdvancedElectronics
problem_type: workflow_issue
component: development_workflow
severity: medium
applies_when:
  - "Assembling a release from two toolchains where only one is scriptable"
  - "A build artifact must be produced by a GUI step the release script cannot run"
  - "Shipping an artifact whose staleness produces no error, only wrong behaviour"
tags: [release, packaging, build-tooling, staleness, fail-closed, eco-modding, unity]
related_components: [EcoServerMod/AdvancedElectronics]
---

# A release script should refuse to package a stale artifact, not warn about it

## Context

This mod ships two artifacts from two toolchains: server DLLs from `dotnet build`, and a client asset
bundle built inside the Unity Editor. Only the first is scriptable. When the first release was being
assembled, the bundle on disk was eight days old and predated a behavioural change to a script it
carries — it would have shipped client behaviour that silently disagreed with the DLLs.

Nothing about that was visible. The bundle file existed, was the right name and a plausible size, and
the DLLs beside it had just been rebuilt from current source. The half of the release that *is*
scripted is always fresh, which makes "I just built this" feel true of the whole thing.

## Guidance

**Encode the trap into the release tool the moment you hit it.** The knowledge that "the bundle can
be older than its sources" is worth little as prose and a lot as a check that runs every time. The
release script is the right home because it is the last thing that touches the artifact before it
leaves.

**Refuse, do not warn.** A script that prints a warning and then exits 0 has taught the operator to
ignore warnings. If the condition means the output is wrong, make it a non-zero exit and produce no
archive. The distinction that matters is not severity of wording — it is whether a bad artifact can
still reach the upload.

**Fail closed when the signal is imperfect, and say why.** The staleness check here compares file
mtimes, which git rewrites on checkout — so it can raise a false positive on a bundle that is
genuinely current. That argues for an override flag, not for downgrading to a warning, because the
two errors are not symmetric:

- False positive: the operator is told to rebuild something already fresh. Cost: mild annoyance, and
  they can pass `--force`.
- False negative: a stale artifact ships and misbehaves in the field with no error message. Cost: a
  bad release and a confusing bug report.

When one direction is loud and cheap and the other is silent and expensive, the default belongs on
the loud side. Put the reasoning in a comment next to the check so nobody "fixes" the annoyance later.

**Check the premise before reusing that asymmetry, because it is a condition and not a slogan.** It
holds here because a false positive is rare and costs one rebuild. Where false positives are instead
the *majority* of a check's output, the same reasoning inverts: a hard gate stops being a safety net
and becomes pressure to remove whatever trips it. The documented case in this repo is the citation
validator run over `docs/solutions/`, where the correct-on-purpose flags outnumber the real defects
by an order of magnitude and the right response is per-flag adjudication rather than a gate — see
`docs/solutions/workflow-issues/a-crashed-check-and-a-flagged-check-are-opposite-problems.md`. Refuse
rather than warn where a failure is rare and each one is cheap to clear; adjudicate rather than gate
where failures dominate and clearing one means editing the artifact the check is meant to protect.

**A better signal does not always exist, and where it does it answers a narrower question.** The
bundle's contents are now readable without Unity — `scripts/read-mod-bundle.py` reports the names
inside a built `.unity3d`, which is how a separate class of claim gets settled exactly rather than
guessed (`docs/solutions/workflow-issues/a-timestamp-says-when-a-file-was-written-not-what-is-in-it.md`).
That is worth adding **alongside** this gate for name-presence questions: did a retired object leave
the bundle, did a new one reach it. It is not a replacement for the mtime comparison, because most
of what makes a bundle stale changes no name at all — an edited material, a moved mesh, a changed
script inside a prefab. The imperfect signal still covers the wider question, so `--force` stays.

**Gate on negative conditions too.** A release check is not only "is everything present" but "is
anything present that must not ship". This script fails if the feasibility-spike DLL appears in the
build output — an artifact that is useful in development, is deployed on the dev server, and would
quietly register its own commands if it escaped into a release.

**Separate "cannot build it" from "will not ship it stale".** The script does not try to drive the
Unity Editor; automating a GUI build is a different and larger problem. It simply declines to package
without fresh output from it. That keeps the tool honest about its own scope while still closing the
hole.

**Run the real gates, not proxies for them.** Building and testing inside the packaging script means
the artifact and the evidence come from one invocation. A test suite that passed in some earlier
terminal is not evidence about the thing being zipped.

## Why This Matters

Staleness is the failure mode that looks exactly like success. A stale artifact is present,
well-formed, correctly named, and loads without complaint — every check a human performs by eye
passes. It is detectable only by comparing it against something else, which is precisely the kind of
bookkeeping people are bad at and scripts are good at.

The mixed-toolchain seam is where it hides. When one half of a release is scripted and the other is
manual, the scripted half is always current, and its freshness gets attributed to the whole. The more
reliable the automated half becomes, the more confidently the stale manual half ships.

There is a second-order benefit: the checks become the documentation. A reader of the script learns
that the bundle carries scripts, that it can be older than its sources, and that the spike DLL must
never ship — facts that would otherwise live in someone's memory or in a README nobody rereads.

## When to Apply

- Any release that combines a scripted build with a manual or GUI-produced artifact.
- Any artifact whose staleness has no runtime symptom — no version mismatch error, no checksum, no
  load failure.
- Right after any near-miss. The moment you catch a bad artifact by luck is the moment the check is
  cheapest to write and easiest to justify.
- When reviewing an existing release script: look for `echo "WARNING..."` followed by continued
  execution, and decide whether that condition should actually be fatal.

## Examples

The staleness gate, with the asymmetry recorded next to it
(`scripts/package-release.sh`):

```bash
# Anything under Assets/Art newer than the bundle means the bundle predates a client
# change. Fails closed: a git checkout rewrites mtimes and can trigger a false
# positive, which is why --force exists -- but the default must be to refuse.
NEWER="$(find Assets/Art -type f \
            \( -name '*.cs' -o -name '*.prefab' -o -name '*.mat' -o -name '*.png' -o -name '*.unity' \) \
            -newer "$BUNDLE" 2>/dev/null | head -5)"

if [ -n "$NEWER" ]; then
    echo "Client sources are newer than the asset bundle:" >&2
    echo "$NEWER" | sed 's/^/    /' >&2
    echo "    (bundle built: $(date -r "$BUNDLE" '+%Y-%m-%d %H:%M'))" >&2
    if [ "$FORCE" -eq 0 ]; then
        fail "bundle is stale. Rebuild it in Unity, or pass --force if you just did."
    fi
    echo "WARNING: packaging a possibly stale bundle because --force was given" >&2
fi
```

`*.unity` joined that list later, and the comment in the script now records why: an item's icon
binding lives in the scene, so a scene edited after the last bundle build changes what the client
renders while every other file type here stays untouched.

The negative gate, which is easy to forget to write:

```bash
# The spike project is a reference artifact, never shipped.
if [ -f "$RELEASE_DIR/AdvancedElectronics.Spike.dll" ]; then
    fail "AdvancedElectronics.Spike.dll is in the Release output; it must not ship"
fi
```

What it looked like when it fired on the real problem — note that it names the offending files rather
than just asserting staleness, so the operator can judge whether it is a true hit:

```
==> Running navigation tests
Passed!  - Failed: 0, Passed: 68
Client sources are newer than the asset bundle:
    Assets/Art/AdvancedElectronics/DockReadoutDisplay.cs
    Assets/Art/AdvancedElectronics/DroneDockObject.prefab
    (bundle built: 2026-07-19 11:20)
ERROR: bundle is stale. Rebuild it in Unity, or pass --force if you just did.
```

## Related

- `docs/solutions/runtime-errors/duplicate-asset-bundle-under-mods-aborts-startup.md` — the other
  half of the same release: this doc keeps a stale bundle out of the archive, that one keeps a
  duplicate out of the install.
- `docs/solutions/workflow-issues/eco-mod-batched-live-testing.md` — the same instinct applied to
  test cadence rather than to packaging.
- `docs/solutions/workflow-issues/a-crashed-check-and-a-flagged-check-are-opposite-problems.md` — the
  case where this doc's asymmetry does not hold, and the reason the two are not in conflict. There
  the false positives are structural and dominant, so the check hands back questions to adjudicate
  instead of refusing; here they are rare and cheap, so it refuses.
