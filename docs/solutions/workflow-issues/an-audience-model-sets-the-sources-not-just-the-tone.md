---
title: "The audience model of a document silently chooses its sources, not only its tone"
date: 2026-09-05
category: workflow-issues
module: docs
problem_type: workflow_issue
component: documentation
severity: high
applies_when:
  - "Asked to write a brief, request, or handoff addressed to a named human outside the project"
  - "Drafting an outbound document without having been told who reads it and what they already know"
  - "Assembling a list of items from a session handoff or summary rather than from the code that declares them"
  - "A correction about audience or scope arrives after a document has already been written or published"
  - "About to explain the reader's own domain back to them"
symptoms:
  - "A rewrite for the real reader deletes roughly half the document as things that reader already knows"
  - "The rewrite, enumerated from the code that declares the items, surfaces entries the first draft never listed at all"
  - "The first draft dropped items its own source document had named, because they were not persuasive"
  - "A partial list carries a \"listed for completeness\" assurance, so it reads as exhaustive"
root_cause: "An unstated assumption about who the reader is silently set the document's job, and the job set its sources. A stranger-audience model licensed a representative list, so items were selected for persuasive value from a summary instead of enumerated from the constructs that declare them -- and the selection dropped two items the summary itself had named."
resolution_type: documentation_update
tags:
  - documentation
  - audience
  - collaboration
  - art-assets
  - completeness
  - source-of-truth
  - methodology
  - eco-modding
related_components:
  - docs/briefs
  - docs/protocols
  - EcoServerMod/AdvancedElectronics
---

# The audience model of a document silently chooses its sources, not only its tone

## Context

The repository owner asked for "the request document to ask the artist for more art" — a document
listing the art this Eco mod still needs, to be sent to a human artist. No audience was named in the
request.

The first version, committed as `c3c5cd8` (`docs(briefs): add the artist-facing icon art request`),
was written for a **stranger**: a competent artist who had never heard of the project and had to be
persuaded to spend unpaid time on it. That assumption is visible in every section of the file at
that commit. It opened with an eyebrow reading "Art request · Eco mod" and a headline promising "Six
icons and one model for a mod that flies drones". Section B, titled "Why this mod, and not another
one", explained what Eco is ("a multiplayer civilization sim where an entire server shares one
ecology, one economy, and one set of laws"), what mods normally add, and why this one is unusual.
Section C showed the three shipped drone icons as a quality reference and named the register the
work had to match. Section D, "The format, in full", was an eight-row table of technical constants:
a 128 × 128 PNG canvas with alpha, two PNGs per icon (one carrying the background plate and one
`_FG` without it), the supplied plate, the `Name_icon.png` / `Name_icon_FG.png` naming rule, the
isometric three-quarter flat-shaded style, legibility at roughly 40 px, and a colour-separation
requirement. Section F, "Licence, credit, and what I will not ask of you", carried the CC BY-SA 4.0
terms, the named-credit offer, and three reassurances — "No deadline", "No obligation to finish", "I
will not ask for revisions past what you offer". The asset list itself held five entries: the
PostModern research paper, the battery, the drone dock model, one tag icon marked "May not be
needed", and the mining and survey components lumped into a single sixth entry with the same
marking. The page was built for sharing and published; `docs/briefs/build-art-brief.py:18-25` and `:40-49`
is the step that swaps the tracked `__ICON_*__` placeholders for base64 data URIs so the page can
travel as a single self-contained file.

The owner then supplied the audience, and it was not a stranger:

> "I should have explained what the target audience is. It's the artist who already made the drone
> model. He is already aware of the mod and it's sold in the idea. He did a huge part of the art for
> the Eco game itself over the years. What I need is the list of outstanding art that is needed
> (including things that have placeholders) and make a list of them, explaining the concept of each
> one, and the scope (the drone dock model is a different work than the icon for the research
> paper). Technical things about the game are not relevant - he knows more than me and you. Any
> information about the mod unrelated to each one of the things needing art is also not relevant."

The rewrite, committed as `579a69a` (`docs(briefs): rewrite the art request for the mod's own
artist`), removed roughly half the document: the persuasion, the mod overview, the whole format
table, and the terms-and-reassurance section. The file went from 707 lines to 617 while gaining six
list entries, which is the shape of the change in one number. Both commits sit on the long-lived
branch `feat/tech-tree-icons`. Neither is merged into `main` (which is at `chore: drop the untracked
template scene from Unity's build list`) and there is no pull request for them, so this learning
describes work in an unmerged branch state rather than shipped history.

## Guidance

**Establish the audience before the outline, and treat "for an artist" or "for a reviewer" as an
unanswered question rather than an answer.** The useful thing to establish is not a role but a
knowledge boundary, and it has three parts worth naming separately. First, what the reader already
knows about the *domain* — here, Eco's icon pipeline, its visual register, and how research-paper
tiers are drawn. Second, what the reader already knows about *this project* — here, that the mod
exists, what it does, and that it is worth helping. Third, what the reader has already *decided* —
here, that they are willing to work on it at all. Everything the reader already holds in any of those
three categories is a candidate for deletion, and everything outside all three is a candidate for
inclusion.

**Ask the audience question when the request does not answer it.** "The request document to ask the
artist for more art" specifies a genre and a recipient class but not a reader. One sentence of
clarification ahead of drafting would have replaced a full rewrite, and it is the cheapest question
in the sequence because the answer is a fact the requester already holds and cannot be derived from
the repository.

**Derive the document's sources from the audience model, not only its tone.** This is the part that
does not announce itself. A document for a stranger needs a *representative* ask, because its job is
to convert interest into a first piece of work; three well-specified items serve that job better than
eleven. A document for a committed expert needs a *complete* ask, because its job is to let the
reader choose their own next piece with full information. Those two jobs pull on different sources: a
representative list can be assembled from any decent summary, while a complete list has to be
enumerated from the artefact that actually declares the things being listed.

**Enumerate the complete list mechanically, from the declaring construct in the source, and not from
prose about the source.** In this repository the declaring construct for a category is the `[Tag("…")]`
attribute on a C# type under `EcoServerMod/AdvancedElectronics/`, and for a borrowed icon it is the
triple of `[HasIcon("…")]`, `[HasStaticIcon(…)]` and an `IconName` override naming a vanilla asset.
Grepping for those constructs is repeatable, and it produces items no summary mentioned.

**Give every entry its concept and its scope, in full prose, and resist compressing them into a
table.** The owner asked for exactly these two things per item — what the thing is for, and how big
a piece of work it is — because "the drone dock model is a different work than the icon for the
research paper" and a reader choosing between them needs to see that difference stated. The rewritten
brief gives each entry two or three explanatory paragraphs plus an explicit `Scope` line; the
first draft gave its three real entries a definition list of measured constants instead. The scope
line is what makes a list of eleven items usable rather than intimidating (auto memory [claude]:
"Specs forbid compression" — a persisted document gets full explicit prose rather than the terse
register that suits chat).

**With an expert reader, demote your own design decisions to proposals.** The first draft presented
the research-paper design as settled fact, under a "Fully specified" pill, with a four-row definition
list of emblem, rails, badge and band derived from measuring the game's own atlas. The rewrite says
the same thing as "My proposal, entirely open to being overruled: keep the Modern star's silhouette
but change the metal to platinum, and step the header band one shade darker"
(`docs/briefs/2026-08-31-icon-art-brief.html:465`). The measurement did not become less true; the
reader became someone whose judgement outranks it, and stating a proposal as a specification asks
them to accept a decision they were never invited to make (auto memory [claude]: "Never bundle my
proposal into their decision").

**When the completeness pass is done, check the enumeration against the source a second time before
believing it.** The rewrite's own summary over-claims — see the Examples below. A pass that improves
completeness is not the same as a pass that achieves it, and the second check is cheap because the
grep is already written.

## Why This Matters

The obvious cost of the wrong audience model is tone, and tone is the recoverable half. A document
that over-explains reads as condescending and wastes the reader's attention, but every sentence of it
is visible to anyone reviewing the draft, including the person who commissioned it. Explaining Eco's
economy to someone who has drawn its art for years is an error that announces itself on first
reading, and deleting it costs one editing pass.

The unrecoverable half is that the audience model silently chose the document's **sources**. Because
the first draft was addressed to a stranger, a representative list was sufficient for its job, so it
was assembled from a session handoff document
(`docs/protocols/2026-08-30-tech-tree-icons-handoff.md`) and from what the drafting session happened
to hold. Because the rewrite was addressed to a reader who would pick his own work, the list had to
be complete, so it was assembled by searching the mod's own source for the declarations — and that
search returned assets the first draft did not contain at all.

That asymmetry is the whole argument. A reader can see that a document is too long, too flattering,
or pitched at the wrong level, and can say so. **Nobody reviewing a document can see what is not in
it.** The missing asset does not read as an error; it reads as a shorter list. Had the first version
gone to the intended reader, the likely outcome is not an objection but silence on five pieces of
work, and the mod's skill emblem would have gone on borrowing vanilla's Electronics emblem
(`EcoServerMod/AdvancedElectronics/AdvancedElectronics.cs:69-75` and `:88`) indefinitely, because nobody in
the loop would have known it was ever on a list.

There is a second-order effect worth naming. The first draft's asset list was not merely short; it
was short in a way that *looked* deliberate. Two of its five entries carried a "May not be needed"
pill and a sentence saying they were listed "for completeness", which reads as an assurance that the
list is exhaustive. A partial list that advertises its own completeness is worse than an obviously
partial one, because it suppresses the reader's instinct to ask what else there is.

## When to Apply

- Whenever a document is being written for a **specific named human** rather than for a repository,
  and the request does not say who that human is. Ask.
- Whenever the reader is credibly **more expert in the document's domain** than the author. Sections
  that explain the domain, justify the project, or specify the reader's own craft are the ones to
  cut, and the ones whose absence the reader will not notice.
- Whenever a document's job is to let the reader **choose** among items. Choosing requires a complete
  set; persuading does not.
- Whenever a list is being assembled from a summary, a handoff, or a previous session's notes. Those
  sources are complete only with respect to the purpose they were written for.

Where it does **not** apply, and where over-applying it does damage:

- **A document for an unknown or mixed audience is a different problem.** A public README, a release
  note, a repository-hosted brief that may be read by anyone — these have to carry the context the
  least-informed plausible reader needs, and cutting it for the expert's benefit breaks them for
  everyone else. The rewritten brief is safe to trim precisely because it is addressed to one person
  whose knowledge is known.
- **Terseness is the symmetric failure.** Deleting explanation is correct only for material the
  reader already holds. Cutting the *concept* of each asset — what the drone dock is for, why the
  battery is handled constantly — because "he is an expert" would have removed exactly the
  information the owner asked for, since the reader's expertise is in art and not in this mod's
  fiction. The rewrite is shorter overall while being *longer* on every individual asset, and that is
  the correct direction.
- **Do not cut a constraint the reader cannot infer.** The CC BY-SA 4.0 licensing of the art survives
  in the rewritten footer, and the handoff records the reason it must survive any future rewrite:
  a piece has to be drawn fresh in vanilla's visual language rather than painted over vanilla's file,
  because the repository is public
  (`docs/protocols/2026-08-30-tech-tree-icons-handoff.md:91-92`). Expertise in the craft does not
  convey knowledge of this project's licence position.

## Examples

**What the two versions listed.** The five entries of the first draft against the eleven of the
rewrite, with what the source says about each addition:

```text
FIRST DRAFT (c3c5cd8, addressed to a stranger)     REWRITE (579a69a, addressed to the mod's artist)
  01 PostModern research paper (fully specified)     01 Drone Dock model
  02 Battery (open brief)                            02 Drone role variants          <- new entry
  03 Drone dock model (open brief)                   03 Battery
  04 Electric Fuel tag ("may not be needed")         04 PostModern research paper
  05 Mining AND survey components (one entry)        05 Upgrade module icon          <- new entry
                                                     06 Skill emblem                 <- new entry
                                                     07 Electric Fuel tag            <- promoted
                                                     08 Post Modern Research tag     <- new entry
                                                     09 AdvancedElectronicsUpgrade tag <- new entry
                                                     10 Survey Component (undecided) <- split
                                                     11 Mining Component (undecided) <- split
```

Grounding for the additions, at the current tree:

- The **upgrade module** borrows vanilla's art through three separate fields, all naming
  `ElectronicsUpgradeItem`: `EcoServerMod/AdvancedElectronics/AdvancedElectronicsUpgrade.cs:146`
  (`[HasIcon("ElectronicsUpgradeItem")]`), `:153` (`StaticIconName`), and `:166`
  (`public override string IconName => "ElectronicsUpgradeItem";`).
- The **skill emblem** borrows `ElectronicsSkill` the same way at
  `EcoServerMod/AdvancedElectronics/AdvancedElectronics.cs:69`, `:75` and `:88`.
- **`Electric Fuel`** is declared as a tag on the battery item at
  `EcoServerMod/AdvancedElectronics/Battery.cs:234`, immediately below `[Fuel(270000)][Tag("Fuel")]`
  at `:233`. There is no `ElectricFuelItem` anywhere in the tree; the only occurrences of the string
  outside documentation are that attribute and the three drones' `fuelTagList` arrays
  (`SurveyDrone.cs:91`, `MiningDrone.cs:80`, `HarvesterDrone.cs:83`). `CONCEPTS.md:474-477` states
  the same thing in prose: "the mod defines the tag and the Battery is its sole holder".
- **`AdvancedElectronicsUpgrade`** is declared at
  `EcoServerMod/AdvancedElectronics/AdvancedElectronicsUpgrade.cs:110`, and the comment above it
  (`:104-109`) explains why the mod had to invent its own tag rather than reuse vanilla's
  `SpecialtyModule`.

**Where the tree disagrees with this session's account, and the tree wins.**

*The "Electric Fuel named as both an item and a tag" correction is not visible in either document.*
The first draft's entry 04 was already headed "Electric Fuel — category icon" and opened "A tag, not
an item — the label a group of items share." Whatever confusion existed lived in the request or the
conversation, not in the file. What the diff actually shows is a change of *status*: the first draft
marked it "May not be needed" and said "I have not yet established whether that icon is mine to
supply or something the base game should already provide", while the rewrite states it as a definite
ask and grounds it in behaviour — "the tag is what the dock actually filters its fuel slot on"
(`docs/briefs/2026-08-31-icon-art-brief.html:528`), which matches the drones' `fuelTagList` cited
above.

*The handoff was not the limiting source for two of the additions.* The handoff's icon table already
named both `AdvancedElectronicsSkill` and `AdvancedElectronicsUpgradeItem` as "borrowed sibling —
**replace**" (`docs/protocols/2026-08-30-tech-tree-icons-handoff.md:38-39`). The first draft dropped
two assets its own source document had listed. The handoff was genuinely missing only the two tag
names, `Post Modern Research` and `AdvancedElectronicsUpgrade`; its unassessed list at `:47-50` names
`Electric Fuel`, `MiningComponent`, `SurveyComponent`, a talent group and a recipe, and nothing else.
This makes the lesson slightly worse than "the wrong source was consulted": the audience model caused
a *selection* to be made from an adequate source, and selection under a persuasion goal drops
whatever is not persuasive.

*The corrected version still over-claims.* Both the rewrite's commit message and the handoff it
updated say the brief covers "three tag icons (`Electric Fuel`, `Post Modern Research`,
`AdvancedElectronicsUpgrade` — all three declared by the mod)"
(`docs/protocols/2026-08-30-tech-tree-icons-handoff.md:87-89`). At the current tree only two are
declared. `Post Modern Research` is commented out:

```csharp
// EcoServerMod/AdvancedElectronics/EngineeringResearchPaperPostModern.cs:86-89
[Ecopedia("Items", "Research Papers", createAsSubPage: true)]
// TODO: add this tag so we can use it
//[Tag("Post Modern Research")]
[Tag("Research")]
```

*And the "undecided" list is itself a selection.* The rewrite lists two components whose icons are
undecided, Survey and Mining. Five component classes in the shipped project carry a bare `[HasIcon]`
— `SurveyComponent.cs:56`, `MiningComponent.cs:36`, `FarmingComponent.cs:35`,
`CropCeilingComponent.cs:40` and `UIShowcaseComponent.cs:41` — and the project excludes only
`AdvancedElectronicsAssembly.cs` from compilation
(`EcoServerMod/AdvancedElectronics/AdvancedElectronics.csproj:55`), so all five compile. Per this
session's reading of the handoff, only `MiningComponent` and `SurveyComponent` were reported missing
by the client (`docs/protocols/2026-08-30-tech-tree-icons-handoff.md:47-50`); whether the other three
surface an icon at all was not verified here, so this is flagged as an enumeration gap to check
rather than as three more missing assets.

**The sentence that would have prevented the rewrite**, asked before drafting rather than after
publishing:

```text
Before I draft this -- who is reading it? Specifically: does this person already
know Eco's art pipeline, and do they already know and want to help this mod?
If yes to both, the document is a complete list of outstanding work with a
concept and a scope per item, and carries no pitch and no format spec. If no,
it is a short, persuasive, fully specified ask for two or three pieces.
```

## Related

- `docs/solutions/workflow-issues/a-review-scoped-to-the-document-cannot-see-what-the-code-settled.md`
  — the review-side half of the same rule. There a reviewer given only the document reports gaps the
  code had already closed; here an author working from a summary omits items the code declares.
  Neither doc covers the other's half: that one has no audience model, this one has no guidance on
  dispatching reviewers.
- `docs/solutions/workflow-issues/a-user-report-carries-evidence-and-a-request.md` — the same
  collaboration read from the other side. That one is about not discarding what the owner's sentence
  contains; this one is about what happens when a sentence the owner never said (the audience) is
  filled in by assumption instead of by asking.
- `docs/solutions/workflow-issues/a-closed-option-set-caps-the-answer-at-what-you-thought-of.md` —
  the same failure mode one layer out. There, an invented option set caps the answer at the asker's
  model; here, an invented audience model caps the document's asset list at what the drafting session
  found persuasive. Both are invisible from the asker's side, and for the same reason: the reader
  cannot object to an absence.
- `docs/protocols/2026-08-30-tech-tree-icons-handoff.md` — the handoff the first draft drew from,
  including the per-asset audit trail at `:96-104`, the format facts at `:106-114`, and the
  licence constraint at `:91-92` that must
  survive any future rewrite of the brief.
