---
title: "A document stored in its own generator has no past tense"
date: 2026-08-08
category: conventions
module: EcoServerMod
problem_type: convention
component: development_workflow
severity: high
applies_when:
  - "Release notes, changelogs, or user-facing warnings live inline in a build or packaging script"
  - "Preparing a release whose notes were edited during the cycle that follows the last one"
  - "A shipped document repeats a destructive instruction (delete, re-craft, wipe, start fresh)"
  - "Asserting that an update is safe for objects placed by an earlier version"
tags: [release-process, packaging, documentation, warnings, versioning, eco-modding, save-compatibility]
related_components: [scripts/package-release.sh, EcoServerMod/AdvancedElectronics]
---

# A document stored in its own generator has no past tense

## Context

`scripts/package-release.sh` builds the README that ships inside the release zip. It does not read
it from anywhere — it writes it inline, from a heredoc that opens at
`scripts/package-release.sh:124` and closes at `scripts/package-release.sh:313`. Nearly two hundred
lines of player-facing prose: the game-version requirement, the ALPHA save warning, "WHAT IS NEW",
INSTALL, UPDATING, KNOWN ISSUES, CREDITS, LICENSE. The version it claims to be comes from
`VERSION="0.2.0"` at `scripts/package-release.sh:47`.

That arrangement has one property nobody decides on: the file always describes the *next* release.
There is no copy of last release's notes in the tree, because the heredoc was overwritten. Edit a
line and you have edited what the next zip will say, whatever heading that line happens to sit
under. The only surviving record of what a past release told players is the artifact — `README.txt`
inside `dist/AdvancedElectronics-0.1.0-eco0.14.0.0.zip`.

Preparing 0.2.0 produced two failures from that single cause.

**The notes were labelled with the wrong version.** After the `release: 0.1.0` commit (tag
`v0.1.0`), work on the Battery, the switch from liquid fuel to Electric Fuel, and the Sulfuric
Battery talent all landed, and the commit `docs(release): describe the Battery, the fuel switch,
and the talent` wrote them into the heredoc. At that commit the script still read `VERSION="0.1.0"`
(line 41) and the heading still read `WHAT IS NEW IN 0.1.0` (line 151), with the three new bullets
sitting under it at lines 169, 174 and 178. Packaging at that point would have shipped 0.2.0's
headline features as things players already had. The commit was not careless — its message
explicitly says it "adds the three player-facing changes to WHAT IS NEW" — it simply had no reason
to distrust the heading it was writing under. Meanwhile the real 0.1.0 notes were already gone from
the script, overwritten in place.

The mislabelling was caught by opening the shipped zip rather than by reading the script. The
`README.txt` inside `dist/AdvancedElectronics-0.1.0-eco0.14.0.0.zip` contains a `WHAT IS NEW IN
0.1.0` section about the drone becoming a module, fuel living on the dock, and crafting moving to
the Robotic Assembly Line. The word "Battery" does not appear anywhere in that file. The heading in
the script and the heading in the artifact had the same text and different contents.

**A destructive warning was one packaging run away from being repeated.** 0.1.0's shipped notes say,
in a boxed capitals block:

> \*\*\* THIS VERSION SPECIFICALLY: placed Drone Docks WILL NOT LOAD, and every \*\*\*
> \*\*\* Drone Dock and Survey Drone must be re-crafted.                        \*\*\*

followed by "The safe update path is a fresh world, or removing every Drone Dock and Survey Drone
with admin tools BEFORE installing the new version." That was true and important for 0.1.0: that
release moved the drone's fuel onto the dock and changed the dock's component set, and an Eco
object's component set is fixed when the object is created.

It is false for 0.2.0. The block had survived every edit of the cycle untouched — the Battery
commit's own message notes that it left "the backup and save-migration warnings alone", because
they "cover a different concern this work did not examine." That is the correct instinct applied to
the wrong artifact: not examining a warning leaves it *asserted*, not dormant. Shipping unchanged
would have told every 0.1.0 player to destroy their placed docks and drones — losing survey areas
and accumulated findings — for a release that did not require it.

What ships in 0.2.0 instead (`scripts/package-release.sh:145-153`) keeps the general risk, replaces
the specific instruction, and is explicit about how much it knows:

> THIS VERSION, SPECIFICALLY: unlike 0.1.0, this release does not change the component set of the
> Drone Dock or the Survey Drone, so docks and drones placed by 0.1.0 are expected to load. That is
> a statement about what changed in the code, not a guarantee about your world -- it has not been
> tested against every 0.1.0 save, and no migration exists to rescue one that does fail.
>
> BACK UP YOUR SAVE BEFORE UPDATING. If you are coming from a release older than 0.1.0, the 0.1.0
> warning still applies in full: remove every Drone Dock and Survey Drone with admin tools first, or
> start a fresh world.

That claim is checkable, and it checks out. Between `v0.1.0` and the current head, no
`[Serialized]` or `[RequireComponent]` line was added or removed in either
`EcoServerMod/AdvancedElectronics/DroneDock.cs` (whose `[RequireComponent]` block sits at lines
59-98, above `class DroneDockObject` at line 100) or
`EcoServerMod/AdvancedElectronics/SurveyDrone.cs` (lines 193-206). Widening the same search to all
of `EcoServerMod` shows every added `[Serialized]` living in a file that is new in this release —
`Battery.cs`, `MiningDrone.cs`, `HarvesterDrone.cs`,
`EcoServerMod/UserCode/AutoGen/WorldObject/ElectronicsAssembly.override.cs` — and the only removals coming from
`Battery.cs.deferred`, the placeholder that `Battery.cs` replaced. Nothing was subtracted from the
stored shape of an object a player could already have placed.

The two changes that do touch those classes are configuration and behaviour, not stored state. The
fuel switch is one string: `private static readonly string[] fuelTagList` in `SurveyDrone.cs` went
from `{ "Liquid Fuel" }` to `{ "Electric Fuel" }`, and `FuelSupplyComponent.Initialize` rebuilds its
tag restriction from that list on every install. `SurveyDroneObject` gained the interfaces
`IDroneOwnable, IDroneToolbearer` and an expression-bodied `DroneTool Tool => DroneTool.Harvest` —
no backing field, no attribute, nothing written to a save.

**A third instance, found after 0.2.0 had already shipped.** The same check applied once more —
read the artifact, not the script — turned up a third drift in the same heredoc, this time in the
published zip. `AdvancedElectronics.csproj:54-56` removes `AdvancedElectronicsAssembly.cs` from
compilation, so that item, its world object and its recipe are absent from the shipped
`AdvancedElectronics.dll`; confirmed by reading the DLL out of
`dist/AdvancedElectronics-0.2.0-eco0.14.0.0.zip` rather than by grepping the source. The shipped
`README.txt` nonetheless tells players to build the Advanced Electronics Assembly and craft at it,
and lists it under known issues as an object that still places normally. The exclusion's own comment
in the `.csproj` points readers at "the release notes' known-issues block"; that block has never
mentioned it. Two files each defer to the other for an explanation neither contains. The impact is
bounded — every recipe players actually need registers against a vanilla table — but the
instructions name a bench that was never shipped. Three drifts, one heredoc, one cause.

## Guidance

**Treat a version heading inside a generator as a claim, not a label.** `WHAT IS NEW IN X` written
in a file that is edited continuously is only accurate at the instant it is packaged, and only if
someone checks. Every commit between two releases lands under whatever heading is already there.
Re-read the heading against `VERSION=` at release time, and re-read the bullets under it against
what actually shipped last time.

**The released artifact is the archive.** When the notes live in the generator there is no history
of them — `git log` on the script shows edits, not versions, and the previous release's text was
overwritten rather than superseded. Verify against the zip: read `README.txt` out of
`dist/AdvancedElectronics-<previous>-eco<game>.zip` and diff it against what you are about to ship.
"Does this bullet appear in the last shipped README?" is a question with a definite answer, and it
is the only reliable way to tell a new feature from one you already announced.

**Every destructive instruction must be re-earned each release.** A sentence that tells a player to
delete placed objects, wipe a world, or start fresh does not persist because someone decided it
should — it persists because nobody deleted it, which costs nothing and looks like caution. Before
packaging, take each such sentence and name the specific change in *this* release that makes it
true. If you cannot name one, it does not ship. Not examining a warning is not the same as leaving
it alone; an unexamined warning is re-asserted in full, with the same capital letters and the same
authority.

**Scope an expired warning rather than deleting it.** 0.1.0's instruction is still correct for
anyone updating from before 0.1.0. It was kept, addressed to exactly those players, and the boxed
version aimed at everyone was replaced. Deleting outright would have stranded the people it still
applies to; keeping it unchanged would have hit the people it does not.

**Phrase an untestable claim as what it is.** "Docks placed by 0.1.0 are expected to load" backed by
"that is a statement about what changed in the code, not a guarantee about your world" is honest and
still useful. "Your saves are safe" would have been neither — it asserts a test that was never run.
Where a claim rests on a code diff rather than an observation, say so, and keep the mitigation
("back up your save") that costs the reader nothing.

**Re-derive the claim, do not remember it.** The statement "no component-set change hit the dock or
the drone" is worth exactly the diff behind it. It takes one command (see Examples) and it is the
difference between a safety assertion and a recollection.

## Why This Matters

The two failures share a cause and do not share a cost, and the asymmetry is the whole point.

A stale feature list is embarrassing. Players read that the Battery shipped in 0.1.0, go looking for
it in a version that does not have it, and conclude the notes are unreliable. The damage is
reputational and recoverable — a corrected upload fixes it.

A stale destructive instruction costs somebody their world. The 0.1.0 text does not describe a risk;
it issues an order, in capitals, with admin-tool steps: remove every Drone Dock and Survey Drone
before installing. Players who follow release notes carefully are exactly the ones who comply, and
compliance is irreversible — a demolished dock takes its survey areas and its findings with it, and
there is nothing to restore from. The players harmed most are the ones who trusted the document
most. That inverts the usual calculus about documentation risk: prose that instructs is far more
dangerous when stale than prose that describes, because the reader's correct behaviour is what
executes the damage.

Copy-forward is the default and correctness is the exception. Rewriting a warning requires a
decision; leaving it requires nothing at all. So the notes drift toward maximum accumulated caution
— every scary sentence any release ever needed, all still shouting — which is not merely noisy but
actively wrong, and trains players to skip the warnings that are real. This mod is alpha and ships
no save migrations; the general risk paragraph at `scripts/package-release.sh:140-143` earns its
place every time. The version-specific block below it does not, and has to prove itself each
release.

There is a second-order effect worth naming. Because the shipped README is the only record, an
uncorrected mislabelling is not just wrong going forward — it destroys the ability to answer "what
did we tell players in 0.1.0?" later. Anyone reconstructing the history from the script alone would
have concluded the Battery shipped in 0.1.0, and every subsequent note built on that would inherit
the error.

## When to Apply

- Before packaging any release whose notes live inline in the build script — the version heading and
  the version-specific warnings both need re-reading against `VERSION=`, every time.
- Whenever a commit adds a feature bullet to release notes mid-cycle. The heading it lands under was
  written for the previous release and is wrong by default until the release commit fixes it.
- Whenever a release's notes contain a sentence that instructs the reader to destroy, delete, wipe,
  re-craft, or start fresh. Re-derive the change that justifies it or cut it.
- When claiming an update is compatible with objects placed by an earlier version — in this repo
  that means checking `[Serialized]` and `[RequireComponent]` on the affected `WorldObject` classes,
  because a component set is fixed at object creation (see
  `docs/solutions/conventions/requirecomponent-is-re-enforced-on-every-server-load.md`).
- When any user-facing text is generated rather than stored: MOTDs, in-game tooltips built from
  version strings, install instructions embedded in tooling.

## Examples

Reading the previously shipped notes — the only archive that exists. `zip` is absent from a stock
Git-for-Windows shell, which is why the packaging script itself falls back to Python's `zipfile`:

```bash
python3 - <<'PY'
import zipfile
z = zipfile.ZipFile('dist/AdvancedElectronics-0.1.0-eco0.14.0.0.zip')
print(z.read('AdvancedElectronics/README.txt').decode('utf-8'))
PY
```

The check that caught the mislabelling, reduced to its essence — does the feature you are about to
announce already appear in what you shipped last time?

```text
shipped 0.1.0 README.txt   ->  "WHAT IS NEW IN 0.1.0", no occurrence of "Battery"
script heredoc, mid-cycle  ->  "WHAT IS NEW IN 0.1.0", three Battery bullets
                               (VERSION="0.1.0" at line 41, heading at 151,
                                bullets at 169, 174, 178)
```

Re-deriving the compatibility claim rather than trusting it. An empty result is the claim:

```bash
git diff v0.1.0..HEAD -- \
    EcoServerMod/AdvancedElectronics/DroneDock.cs \
    EcoServerMod/AdvancedElectronics/SurveyDrone.cs \
  | grep -E "^[-+].*(\[Serialized\]|RequireComponent)"

# and the wider version, to catch a serialized member disappearing anywhere:
git diff v0.1.0..HEAD -- EcoServerMod | grep -E "^-.*(\[Serialized\]|RequireComponent)"
```

The shape of a warning that has been re-earned rather than repeated — general risk kept, specific
instruction replaced, epistemic status stated, old warning scoped to who it still applies to:

```text
  <general risk, true every release>       "ships NO SAVE MIGRATIONS ... that risk is
                                            inherent to updating this mod"
  <this release, re-derived>               "does not change the component set ... expected to load"
  <what the claim is worth>                "a statement about what changed in the code,
                                            not a guarantee about your world"
  <mitigation that costs the reader nothing> "BACK UP YOUR SAVE BEFORE UPDATING"
  <old instruction, scoped>                "if you are coming from a release older than 0.1.0,
                                            the 0.1.0 warning still applies in full"
```

### Prevention, and what each option actually costs

None of these exist in the repo today; the 0.2.0 fix was manual, in the `release: 0.2.0` commit.

*Per-version notes files.* Replace the "WHAT IS NEW" portion of the heredoc with a file the script
reads — `docs/release-notes/${VERSION}.txt` — leaving the stable sections (INSTALL, UPDATING,
LICENSE) inline. Bumping `VERSION` to a version with no notes file then fails the packaging run
instead of silently shipping the previous release's text, and past notes stay in the tree where
`git log` and `diff` can see them. The cost is real: the heredoc interpolates `${VERSION}` and
`${GAME_VERSION}` at lines 126, 132 and 136, so a plain `cat` either loses that substitution or
needs an expansion step, and the split means two places to look when writing notes.

*Diff the previous shipped README against the staged one.* The staged copy exists at
`$STAGE/AdvancedElectronics/README.txt` before the zip step at `scripts/package-release.sh:315`, and
the previous zip is in `dist/`, so a short Python block could extract and `diff` them and require
confirmation before zipping. This catches both failures — an unchanged "WHAT IS NEW" heading and an
unchanged warning block both show up as suspiciously small diffs. The catch is that `dist/` is
git-ignored, so on a fresh clone there is nothing to compare against; the step has to degrade to a
warning rather than a hard failure, which weakens it exactly where a new contributor needs it most.

*Interpolate the heading.* Write `WHAT IS NEW IN ${VERSION}` instead of a literal at
`scripts/package-release.sh:155`. Near-zero cost, and it makes a heading that disagrees with the
packaged version impossible. It does nothing about stale *content* under the heading and nothing at
all about the warning, so it is a partial measure worth taking alongside one of the others rather
than instead of them.

## Related

- `docs/solutions/conventions/requirecomponent-is-re-enforced-on-every-server-load.md` — why a
  component-set change breaks placed objects, and therefore why "did the component set change?" is
  the right question to ask before writing a compatibility claim.
- `docs/solutions/conventions/a-defensive-rule-outlives-the-danger-it-answered.md` — the same failure
  in code comments: a well-argued justification protects a constraint whose premise has expired.
  Here it is a warning to players instead of a rule for readers, and the reader acts on it.
- `docs/solutions/conventions/document-the-path-you-actually-deploy-to.md` — user-facing instructions
  drifting from what the project actually does.
- `docs/solutions/conventions/commit-bodies-list-changes-not-lessons.md` — the counterpart division
  of labour: commits record what changed, and the durable reasoning lives here.
