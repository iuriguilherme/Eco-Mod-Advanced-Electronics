---
title: "A remembered capability and a cited file are claims, and nothing checks either"
date: 2026-08-10
category: workflow-issues
module: EcoServerMod
problem_type: workflow_issue
component: development_workflow
severity: medium
applies_when:
  - "A request says 'IIRC there's a tool/template/wiki page for this'"
  - "A task cites a document, script, or menu command by name"
  - "About to tell the maintainer that some vanilla asset or API exists"
  - "A claim about the toolchain is about to become an input to a decision"
  - "Reading in-repo documentation that predates the version being targeted"
tags: [research-grounding, verification, false-premise, toolchain, documentation-drift, stale-tasks, eco-modding, methodology]
related_components: [EcoServerMod/AdvancedElectronics, EcoModKit, docs]
---

# A remembered capability and a cited file are claims, and nothing checks either

Paths beginning `Server/` or `Content/` below are Eco's own source checkout, not files in this
repository. Everything else is repo-relative.

## Context

A brainstorm on 10 August set out to give this mod's four tech-tree entries — the skill, its skill
book, its skill scroll, and `EngineeringResearchPaperPostModernItem` — their in-game icons. It was a
small, well-bounded task. Three separate assertions about the toolchain turned out to be wrong before
it was scoped, and all three came from different places:

- the **maintainer's memory** of a capability the wiki supposedly documents,
- a **repo task** citing a file by name,
- and the **agent's own claim** about what vanilla art exists on disk.

None of the three was checked by anything. A memory has no validator. A task's cited resource is
never resolved by the tool that stores the task. And an agent's mid-conversation assertion is
believed the moment it is typed, because the person reading it is the person who cannot check it.

The task's whole subject matter made the problem worse rather than better: a missing icon in Eco
degrades quietly. The engine falls back to a placeholder sprite rather than raising, and Eco's own
icon tooling ships a "Show Missing Icons only" mode — so an unassigned icon is an expected authoring
state, not an error. Nothing fails loudly enough to correct a wrong belief about how icons work.

## Guidance

**Treat "IIRC there is a tool for this" as a lead, not a fact.** The request here was roughly *"IIRC
the wiki has something about using templating to make those, it should be in your tasks to research
it."* The templating is real. The template behind skills, books and scrolls
(`Server/Eco.TechTree/TechTemplate.tt`) generates C# class declarations from a spreadsheet and emits
no icon metadata — the vanilla Electronics classes it produced
(`Server/Mods/__core__/AutoGen/Tech/Electronics.cs:33-100`) carry no icon-related attribute of any
kind. But its sibling templates *do* emit icon attributes, off a `RequireIcon` spreadsheet column:
`Server/Eco.TechTree/ItemTemplate.tt:62` emits `[NoIcon]`, `WorldObjectTemplate.tt:221` emits
`[IconGroup("World Object Minimap")]`. Which is precisely why "the templating handles icons" felt
true. The memory named a real thing that really does touch icons — just not the one that produces
the four classes in question.

**A presence check is not enough when the claim is about what a tool does.** The neighbouring
discipline in `docs/solutions/workflow-issues/the-compile-target-decides-what-exists.md` is "does
this exist in the artifact I build against" — and here it would have *passed*. The templating exists.
It is in the tree, it runs, it produces the very classes in question. What was false was not its
existence but its output.

**Grep the surface that would own the capability, not the first file with a plausible name.** The
check this session actually ran was one grep for `icon|sprite|atlas` against
`Assets/EcoModKit/Scripts/Editor/ModKitTools.cs`, returning 0 across its 160 lines — read as proof
that the ModKit exposes no icon path to mods. It proves nothing of the sort. That file is the
asset-bundle export window; its members are `ModKit Tools…`, `Build Current Bundle`, and a curve
toggle. It would score zero even if the ModKit supported icon authoring completely — which it does.
`Assets/EcoModKit/Prefabs/ItemTemplate.prefab` and `IconTemplate.prefab` are image templates, backed
by `Assets/EcoLibs/Utils/IconUnityTools/IconTemplate.cs:7`, and `Assets/EcoModKit/Docs/README.md:30-34`
documents the workflow in as many words: drag `ItemTemplate` onto `Items`, edit the background and
foreground sprites.

What is genuinely first-party is the **bake** step that turns authored icons into an atlas — it needs
an internal Unity scene, a server-GUI export, and a *Bake icons* press. "No bake pipeline for mods"
is true. "No icon support for mods" is false. One grep against one file cannot tell those apart, and
the difference is the whole question.

That failure is this doc's own thesis biting the doc. An inference from a single absence is a claim,
not a fact, and this particular one survived being reasoned out, written down, and used — until an
independent validator read the file the grep had actually searched. A check you invented is exactly
as unverified as the belief it was meant to test.

**The memory is still worth mining, because it usually names something real and adjacent.** The
templating the maintainer remembered sits directly upstream of the same four classes — it produces
the C# rather than the art. Discarding the lead outright would have thrown away a true fact about the
content pipeline. Downgrade the claim from *capability* to *pointer*, then find out what the pointer
actually points at.

**Resolve a task's cited resource before doing the task.** A task pending since 30 July read
"Replace placeholder item icons using `Icons.md` guidance". `Assets/EcoModKit/Docs/` contains exactly
two files, `README.md` and its `.meta`; a search of the tracked tree for the cited name returns
nothing:

```bash
find . -iname "icons.md" -not -path "./Library/*"    # no output
```

**Search the disk, not the index.** `git ls-files | grep -i "icons\.md"` also returns nothing here,
and it is the wrong check: `.gitignore:147` is `/Assets/EcoModKit/`, so `git ls-files Assets/EcoModKit`
returns zero files and a tracked-tree search is structurally blind to the exact directory the task
pointed at. It gives the right answer for the wrong reason, which is worse than a wrong answer —
it would go on giving that answer after someone added the file.

The mechanism is already named in this store:
`docs/solutions/conventions/a-fix-does-not-reach-the-copies-already-taken.md:58` — *"An untracked
file is invisible to every tool that would have shown the divergence."* And the shape it produces —
an emptiness that reads as an answer — is
`docs/solutions/workflow-issues/a-gate-that-discovers-nothing-passes-everything.md`. Note the
converse, so the rule does not overreach: `git ls-files` is the *correct* instrument when
trackedness is the question being asked, as in
`docs/solutions/conventions/excluding-third-party-from-a-unity-mod-repo.md`, where a vendored tree
showing up in the index is the failure being tested for. Index for "is it tracked", disk for
"does it exist".

The real `Icons.md` does exist, 453 lines of it, but only in a separate vendor wiki checkout outside
this repository. The task had carried a false premise for ten days, and nothing flagged it, because
nothing validates that a task's cited resource resolves. That check is one command and belongs at the
*start* of the task, not after someone has gone looking through `Assets/EcoModKit/Docs/` by hand.

**Send an independent verifier before a claim reaches the maintainer's decision, not after.**
Mid-brainstorm the agent asserted that loose vanilla per-class icon files existed at
`Content/Art/UI/Icons/IndividualIcons/<ClassName>_Icon.png` in the Eco source checkout and could be
borrowed as placeholders. A verification subagent dispatched later refuted it: that directory holds
130 PNGs and not one is a skill, skill book, scroll, or research paper. The vanilla art for
those classes exists only as rects inside a baked sprite atlas
(`Content/Art/UI/Icons/UI_Icons_Baked_0.png.meta`). The refutation is recorded in the resulting plan
at `docs/plans/2026-08-10-001-feat-tech-tree-icons-plan.md:162`. The dispatch was the right move; its
*timing* was the failure, because by then the claim had already been used.

**Mark the confidence in the sentence itself.** "Vanilla ships loose per-class icon files you can
copy" and "vanilla probably has borrowable art — let me confirm where it lives before you decide" are
the same research state described two ways. Only the second lets the maintainer wait.

**In-repo documentation is a claim too, and it carries a version.** `Assets/EcoModKit/Docs/README.md`
is the one document this repo does have on the subject, and line 8 reads:

> Note: As of 0.9.6 Eco is in the process of moving to Addressables for asset loading. Currently the
> modkit does not support this yet…

This mod targets 0.14. The documentation that *is* present describes a different version of the
engine — so "the repo documents it" is not the same as "the repo documents the thing you are
building against."

## Why This Matters

The cheap failure is wasted research: an hour spent looking for an icon templating feature that was
never in the ModKit. Annoying, self-correcting, paid in agent time.

The expensive failure is instance 3. By the time the verifier refuted the loose-files claim, the
maintainer had already made a decision on the strength of it — *borrow vanilla art locally, never
commit it* — which is now recorded as a governing key decision, KD2 in
`docs/plans/2026-08-10-001-feat-tech-tree-icons-plan.md:39`. The decision itself survived, but its
cost did not: what had been "copy a file" became "crop a rect out of a baked atlas by its
coordinates", which is a different amount of work and still sits in the plan's Outstanding Questions
at `:147`. The choice had to be re-presented.

That is the sharp point. An unverified claim that reaches a user's decision does not just waste the
agent's time — it makes the maintainer **choose under a false premise**, and then spends their
attention twice: once on the decision, once on the correction. In a project where the maintainer is
the only instrument that can see the running game (see
`docs/solutions/workflow-issues/a-user-report-carries-evidence-and-a-request.md`), their attention is
the scarcest resource in the loop, and burning it on a retraction is the worst possible use of it.

There is a trust cost on top of the attention cost. Every one of these three assertions arrived
wearing the shape of a fact — a remembered feature, a written task, a specific path with a filename
pattern. Specificity reads as verification, and it is not. A path with a plausible naming convention
in it is exactly as unchecked as a vague hunch, and considerably more convincing.

Two of the three checks that would have caught this are single shell commands, and the third is one
subagent dispatched a few minutes earlier than it actually was.

## When to Apply

- Any request containing "IIRC", "I think there's a", "the wiki has", or "it should be in your tasks
  to research" — before starting the research it authorises.
- Before starting any task whose text names a file, document, script, or menu command: resolve the
  name against the tracked tree first.
- Before writing a sentence that asserts a file, directory, asset, API, or menu item exists, when you
  have not opened it this session.
- Before a claim about the toolchain becomes an input to a maintainer's decision — dispatch the
  verifier *upstream* of the question, not after the answer.
- When citing in-repo documentation whose version stamp differs from the version being targeted.
- When a capability "should obviously exist" and you cannot find it — before concluding it is absent,
  establish that the file you searched is the surface that would own it. A zero from the wrong file
  is not evidence.
- When a search of the tracked tree comes back empty — check whether the directory in question is
  git-ignored before reading the emptiness as an answer.

## Examples

The three checks, and what each returned in this session:

```bash
# 1. Does the task's cited resource exist here at all?
#    Disk search, not `git ls-files` -- the vendored SDK is git-ignored.
find . -iname "icons.md" -not -path "./Library/*"
#   (no output)  -> the task had cited a file outside this repository for ten days

# 2. Does the surface that would OWN the capability carry its vocabulary?
#    WRONG surface -- an asset-bundle exporter, which scores 0 either way:
grep -ciE "icon|sprite|atlas" Assets/EcoModKit/Scripts/Editor/ModKitTools.cs
#   0            -> proves nothing about icon support
#    RIGHT surface -- where icon authoring would actually live:
ls Assets/EcoModKit/Prefabs/ | grep -i template
#   IconTemplate.prefab, ItemTemplate.prefab  -> authoring IS exposed to mods

# 3. Independent verification of an asset-existence claim
#    (subagent) "list Content/Art/UI/Icons/IndividualIcons/ in the Eco checkout;
#                report any skill, skill book, skill scroll or research paper file"
#   -> 130 files, none of them any of those four
```

How the third claim was phrased, and how it should have been:

```text
WRONG -- asserted, then used as a decision input, then retracted
  "Vanilla ships loose per-class icons at
   Content/Art/UI/Icons/IndividualIcons/<ClassName>_Icon.png -- we can borrow those
   as placeholders."
  -> maintainer decides "borrow locally, never commit"
  -> verifier: 130 files, not one is a skill/book/scroll/paper; it's all atlas rects
  -> decision re-presented with a different cost (crop from an atlas, not copy a file)

RIGHT -- confidence marked, verification upstream of the question
  "Vanilla almost certainly has art for these four, but I haven't confirmed the form
   it's in -- loose files vs. atlas rects changes the effort a lot. Checking before
   I ask you to choose."
```

What survived the checks is the useful part: the ModKit exposes icon authoring by hand — drag a
template into the `Items` scene root, set two sprites — and this mod automated that into a menu
command, because doing it by hand for every entry was the real cost.
`Assets/Art/AdvancedElectronics/Editor/AdvancedElectronicsBuildTools.cs:64` holds a
class-name-to-fill-colour table, and `:166` exposes a
`[MenuItem("Eco Tools/Advanced Electronics/Finish All Item Icons")]` command over it. The nine
placeholder PNGs under `Assets/Art/AdvancedElectronics/Sprites/Icons/` are each about 200 bytes —
flat generated squares — and there is no row for `AdvancedElectronicsSkill` in that table, which is
the actual gap the brainstorm was after. None of that was discoverable from the remembered
capability, the cited file, or the asserted vanilla path.

## Related

- `docs/solutions/workflow-issues/the-compile-target-decides-what-exists.md` — the same discipline for
  source trees: a checkout beside the repo is not the thing you build against. That one asks whether
  a thing exists in the artifact you compile against; this one covers the case where it exists and
  still does not do what it was remembered to do, and extends the question to documentation and
  tooling capabilities, including the ModKit's own `README.md:8` still describing Eco 0.9.6.
- `docs/solutions/workflow-issues/validate-the-instrument-before-the-hypothesis.md` — instruments do
  not look like claims, they look like results. A remembered capability and a task's citation are the
  same category: statements that arrive pre-trusted.
- `docs/solutions/workflow-issues/a-decision-about-state-you-own-is-not-the-users-to-make.md` — the
  other half of instance 3. There the problem was handing the maintainer a decision that was not
  theirs; here it is handing them a decision built on a premise that was not true.
- `docs/solutions/workflow-issues/a-user-report-carries-evidence-and-a-request.md` — why the
  maintainer's attention is the scarcest resource, and therefore why a retraction is expensive.
- `docs/solutions/conventions/an-attribute-that-only-feeds-a-tooltip.md` — the inverse case: source
  read correctly, conclusion still wrong, because what the code does is not what it is meant to do.
