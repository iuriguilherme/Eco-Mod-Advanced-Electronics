---
title: "A knowledge store corroborates its own errors, and the copies outlive the correction"
date: 2026-08-14
category: workflow-issues
module: docs
problem_type: workflow_issue
component: documentation
severity: high
applies_when:
  - "Correcting a claim that is recorded in docs/solutions/, CONCEPTS.md, README.md, or a plan"
  - "Two in-repo artifacts agree on a technical claim and neither cites a run, a log, or a source file"
  - "Auditing or refreshing the learnings store, or reading it to orient on the codebase"
  - "About to cite an in-repo document as the authority for a statement"
  - "A learning is being summarised into README.md, CONCEPTS.md, or a plan's rationale"
symptoms:
  - "A wrong claim appears in a learning doc and again in CONCEPTS.md, README.md, or a plan"
  - "A doc names a sibling doc as its authority instead of a source file, a log, or a live test"
  - "README.md contradicts itself, carrying the correct statement and the wrong one in the same file"
  - "Grepping docs/solutions/ finds one copy while the rest sit in other artifact classes"
  - "A claim reads as confirmed because it was met twice, not because it was verified once"
root_cause: inadequate_documentation
resolution_type: documentation_update
tags: [knowledge-store, documentation-drift, false-corroboration, audit, cross-artifact, concepts-md, readme, eco-modding]
related_components: [docs, CONCEPTS.md, README.md]
---

# A knowledge store corroborates its own errors, and the copies outlive the correction

Paths beginning `Server/` below are Eco's own source checkout, not files in this repository.
Everything else is repo-relative.

## Context

Over 13 and 14 August this repository corrected four claims it had written down as settled. Each one
was wrong, each had been believed for weeks, and — the part worth documenting — **not one of them
existed in only one place.** By the time anybody checked, every one had been quoted, summarised or
cited into at least one other artifact, and in three cases into an artifact of a different *kind*:
a learning doc, a glossary entry, a README, a plan's assumptions list, an ideation survivor.

The commit that fixed the first batch, *"docs: refresh conventions, replace the RequireComponent
learning"* (`644fbe8`), touched twenty files. The follow-up, *"docs(solutions): record how far the
client-code constraint propagated"* (`186e648`), exists only because a fifth artifact was found
still repeating one of them. Its commit body states the mechanism in one line:

> before it was understood, the MonoBehaviour path was written down as a proven client surface and
> reached the README from there, so the store corroborated its own error.

That is the whole failure. A wrong claim recorded once does not stay one claim. It gets cited by the
next document that needs it, summarised into the glossary, restated as a constraint in a plan, and
repeated in the README for contributors. A reader who meets the second copy reads it as independent
confirmation of the first — and it is not independent, it is the first copy wearing different
clothes. Two documents agreeing is normally evidence. Here it is evidence of nothing except that one
of them was written after the other.

The four:

**1. `[RequireComponent]` binds at creation.** The deleted
`docs/solutions/conventions/requirecomponent-binds-at-creation-not-retroactively.md` opened with
*"`[RequireComponent(typeof(T))]` on a `WorldObject` class is a **construction** rule, not a shape
rule … Editing the attribute changes what future objects get and leaves every existing object
exactly as it was."* The engine does the opposite. `ValidateComponents` is called from
`DoInitializationSteps` (`Server/Eco.Gameplay/Objects/WorldObject.cs:457,537,541`), whose own doc
comment says *"called after OnCreate, and every server start"*, and
`Server/Eco.Gameplay/Objects/WorldObjectManager.cs:161` runs it over every persisted object in a
`Parallel.ForEach` at load. The claim's copies: `CONCEPTS.md`'s **World Object** entry, and
`docs/plans/2026-08-01-001-feat-dock-owns-drone-components-plan.md`, which cited the doc.

**2. `[AllowPluginModules]` is presentation, not admission.** Recorded in
`docs/solutions/conventions/an-attribute-that-only-feeds-a-tooltip.md` on 2 August, correctly, as a
description of a *bug* SLG intended to fix. The fix landed:
`Server/Eco.Gameplay/Components/PluginModulesComponent.cs:407-409` reads the table's allow-list and
applies it as a real `StackableRestriction`. The copy:
`docs/solutions/conventions/usercode-cannot-name-a-mod-dll-type.md`, which had repeated the premise
in its own Context *and* named the first doc in its Related section as the authority for it.

**3. The server half builds from a bare clone via a NuGet package.**
`docs/solutions/conventions/excluding-third-party-from-a-unity-mod-repo.md` gave this as rule 4 —
*"the server half is a separate .NET project resolving Eco through a NuGet reference-assembly
package, so it builds, tests and runs from a bare clone"* — and `README.md` told contributors the
same in its **Setup after cloning** section. There is no `Eco.ReferenceAssemblies` package for 0.14.

**4. A bundle-shipped MonoBehaviour renders custom client UI.**
`docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md` listed it as an
**escape hatch** inside a document whose entire stated method is *"a whitelist of proven surfaces,
not an open contract"*. The Eco client is an IL2CPP build and cannot load mod code at all. The copy:
`README.md`, describing the shipped bundle as carrying the `DockReadoutDisplay` MonoBehaviour.

## Guidance

**A claim that is cited by a sibling document has already spread — that citation is the cheap
tell.** You do not need a survey to know whether a correction will be a one-file edit. Grep the
store for the doc's own filename. `usercode-cannot-name-a-mod-dll-type.md` did not merely happen to
agree with `an-attribute-that-only-feeds-a-tooltip.md`; its Related section said so out loud:

> `docs/solutions/conventions/an-attribute-that-only-feeds-a-tooltip.md` — corrects this doc's
> original premise. `[AllowPluginModules]` is presentation, not admission; a table admits a module
> by matching the module's own slot tag.

That sentence is a load-bearing dependency written in prose. Every doc that contains one has
inherited the cited claim and will not be fixed by fixing the citee. In this repo the
RequireComponent doc was named by eight other files at the moment its claim was overturned; each of
those names was a place the old summary might be sitting.

**Repointing a link is not correcting a sentence.** This is the failure that survived the correction
commit, and it took writing *this* document to find it. `644fbe8` updated the plan's reference from
the deleted filename to the new one — and left the words beside it untouched:

```
docs/plans/2026-08-01-001-feat-dock-owns-drone-components-plan.md:517
- `docs/solutions/conventions/requirecomponent-is-re-enforced-on-every-server-load.md`
  — why component changes do not retrofit.
```

The link now points at a document that exists to say component changes *do* retrofit, and the gloss
still says they do not. Worse, the same plan states it as a governing assumption at `:418` —
*"**Component changes do not retrofit.** Docks and drones already in a world keep whatever
components they were created with, which is why R17 exists"* — where it is not a citation at all but
the justification for a requirement. A rename-driven pass finds the first line and cannot see the
second. Fix the *proposition*, then the pointer.

**Correct by artifact class, not by search hit, because no single search reaches them all.** The
five classes this store actually uses each fail a different query:

| Class | Why a search misses it |
|---|---|
| `docs/solutions/*` learning doc | Found by everything. This is the only class the tooling audits. |
| `CONCEPTS.md` glossary | Paraphrases; carries neither the doc's filename nor its vocabulary. |
| `README.md` | Written for a different audience in different words — "builds straight from a clone", not "reference assemblies". |
| `docs/plans/*` | States the claim as an *assumption* or an acceptance criterion, often with no citation. |
| `docs/ideation/*` | Cites the doc, but nothing audits ideation; a proposal is not read as a fact store. |

`ce-compound-refresh` is explicit about its own scope — *"Audit the learnings under
`<root>/solutions/`"* — so four of those five classes are outside the only automated maintenance
this store has. That is not a defect in the tool; it is a reason the checklist has to be manual.

**Grep for the claim's vocabulary, not the doc's title.** The RequireComponent claim survives in
`docs/plans/` as the word "retrofit", which appears in neither the old filename nor the new one. The
NuGet claim appears in `README.md` as "needs nothing extra" and "builds straight from a clone". The
MonoBehaviour claim appears in `docs/ideation/2026-07-26-survey-system-improvements.md` as
"bundle-shipped Unity MonoBehaviour". Pick two or three nouns from the *substance* of the wrong
claim and search those.

**A false positive in a whitelist is a different animal from a false negative, and only one of them
compounds.** The render-surfaces doc had accumulated several overturned false negatives — surfaces
believed impossible that turned out to work — and each cost only the capability it withheld until
someone retried it. The MonoBehaviour entry was that document's first false *positive*, and the
correction says what that cost:

> It is this doc's only recorded **false positive**, and unlike the false negatives below it cost
> real work: prefab authoring, two orphaned MonoBehaviours needing a strip pass, and a stale claim
> in the repo README.

A false negative sits still. A false positive gets *built on*, and every artifact built on it is
another copy.

**When something is recorded as proven, record what proved it.** The retracted entry's own provenance
line was *"Verified by reading the client and the SLG wiki"* — which is a real check, and not the one
that could have caught this. The follow-up commit states the rule:

> "Verified by reading the client and the wiki" is a different claim from "verified by watching it
> run", and only the second one survives a constraint like this.

Provenance is what lets a later reader rank two disagreeing copies instead of averaging them.

**Retract in place; do not quietly delete.** Both corrections here kept the wrong version visible.
`eco-server-only-mod-client-rendering-surfaces.md:213` begins **"RETRACTED — there is no
custom-MonoBehaviour escape hatch"** and then says what was believed and why it was wrong.
`an-attribute-that-only-feeds-a-tooltip.md` keeps its entire original analysis under a **"Resolved
2026-08-10: the gate came back"** header, on the grounds that *"The account below is the state that
produced the decision."* This matters for exactly the reason this document exists: the copies are
still out there, and a reader arriving from one of them needs to land on a page that recognises what
they were told, not on a page that has silently changed the subject.

**Say when a claim is a moving target rather than a fact.** The corrected third-party doc added the
sentence that generalises its own instance: *"this was a single NuGet reference and became a local
build step on a version bump, so *which tier does this contributor actually need* is a question to
re-answer per release, not a fact to state once."* A claim marked as version-dependent invites
re-checking. One stated flat does not.

## Why This Matters

The direct cost is the correction itself: `644fbe8` touched twenty files to fix four claims, and it
still was not finished. `186e648` followed. And when this document was written a day later, **five
further copies were still live** — two in a conventions doc, one inside an exemplar commit message,
and two in a plan and an ideation doc. The ideation one described the impossible MonoBehaviour panel
as *"The 'real' fix for #4 … the only path to a true My-Deeds-style UI"* and cited the
render-surfaces doc as its source, so had it fed a brainstorm the store would have handed back the
error it had just spent two commits removing. All five are corrected in the same commit as this
document — which is the finding, not a footnote to it: three passes were needed, and the third only
happened because someone set out to describe the pattern rather than to fix an instance of it.

The compounding cost is that a store like this exists to be trusted without re-derivation. That is
its entire value proposition, and it is exactly what makes a wrong entry expensive: the doc is
consulted *instead of* the source. Claim 1 was in the glossary a fresh session reads to orient. Claim
3 was in the README a new contributor reads first — and that file managed to contradict itself, with
`README.md:9-12` correctly stating there is no `Eco.ReferenceAssemblies` package for 0.14 while the
setup section sixty-odd lines below promised the server half *"builds straight from a clone — its
Eco dependency comes from the `Eco.ReferenceAssemblies` NuGet package."* Both sentences had readers.
A contributor who followed the second one hit a hard csproj error and had no way to know which half
of the file to believe.

Claim 4 is the one that cost real work rather than confusion. `DockReadoutDisplay` was authored,
shipped in the bundle, and documented in the README as a thing the release carries — and it never
ran once. It rendered placeholder text forever because nothing could update it. The release script
even grew a rule referencing it (*"a stale bundle ships client behaviour that silently disagrees with
the DLLs"*), which `644fbe8` had to rewrite. Every one of those artifacts made the belief look
better-established than it was.

And the mechanism is self-reinforcing in the worst way: **nothing in the store contradicted any of
these, because the store was the thing repeating them.** Consistency across a knowledge base feels
like corroboration and is structurally incapable of being it, because the later copies were derived
from the earlier ones. The only real check is the source — the engine tree, the running game, an
actual clone — and the whole point of the store is that people stop going there.

This repo's own store demonstrated all four. It is not a hypothetical risk; it is what happened here
in a single week, and the fifth copy has outlived both correction commits.

## When to Apply

- **Before writing the correction**, whenever a doc in `docs/solutions/` turns out to be wrong.
  Budget for the copies first; a correction scoped to one file is almost certainly incomplete.
- When a doc's Related section says another doc "corrects", "supersedes", "establishes" or
  "explains why" — you are looking at an inherited claim in both directions.
- When a plan cites a learning doc, especially in its **Assumptions**, **Risks** or **Key
  decisions** sections. Those restate rather than link, so a filename search will not find them.
- When a claim is about the *build*, the *toolchain*, or *what a contributor needs* — those reach
  `README.md` and `EcoServerMod/README.md`, which no learnings audit covers.
- When a claim is about domain behaviour — how a World Object, area, drone or component behaves —
  those reach `CONCEPTS.md`, paraphrased.
- When adding something to a whitelist of *proven* capabilities. Write down what proved it, in the
  same sentence, and distinguish "read the source" from "watched it run".
- When a doc is deleted and replaced. Every file naming the old path needs its sentence read, not
  just its link rewritten.
- Before a vanilla-version bump. Claims 2 and 3 were both true when written and were falsified by
  upstream changing, not by anyone making a mistake.

## Examples

**The correction checklist, one pass per artifact class.** Run all five; each one has caught
something in this repo.

```bash
# 0. The claim's own vocabulary -- pick 2-3 nouns from the SUBSTANCE, not the title.
#    The RequireComponent claim lives in docs/plans/ as the word "retrofit", which
#    appears in neither the old filename nor the new one.
grep -rn "retrofit\|fixed at creation\|binds at creation" docs/ CONCEPTS.md README.md

# 1. Who names the doc? Every hit is a doc that INHERITED the claim.
grep -rn "requirecomponent-binds-at-creation" docs/ CONCEPTS.md README.md
#   -> 8 files named the doc at the time it was overturned

# 2. The glossary -- paraphrases, so the filename search above cannot find it.
grep -n "component set" CONCEPTS.md

# 3. The READMEs -- different audience, different words. Check BOTH, and check
#    each file against itself: README.md:9 and README.md:76 disagreed for weeks.
grep -n "ReferenceAssemblies\|straight from a clone\|nothing extra" README.md EcoServerMod/README.md

# 4. Plans -- the claim appears as an assumption or acceptance criterion, uncited.
grep -rn "<claim vocabulary>" docs/plans/

# 5. Ideation -- outside every audit tool's scope; it is where the last copy hides.
grep -rn "<claim vocabulary>" docs/ideation/
```

**Step 0 vs step 1 is the whole point.** Step 1 found the plan's Related line and `644fbe8`
repointed it. Step 0 would have found `:418` as well, and it is the one that mattered:

```text
docs/plans/2026-08-01-001-...-plan.md:418   (as found -- the load-bearing copy)
  - **Component changes do not retrofit.** Docks and drones already in a world keep
    whatever components they were created with, which is why R17 exists.

docs/plans/2026-08-01-001-...-plan.md:517   (as found -- link corrected, sentence not)
  - `docs/solutions/conventions/requirecomponent-is-re-enforced-on-every-server-load.md`
    -- why component changes do not retrofit.
```

The second line is the sharper one. A citation was rewritten to point at the document that
*refutes* the sentence it sits beside, so the false claim came out of that pass wearing the
authority of its own refutation. A mechanical rename made it worse than leaving it alone would
have. Both are corrected now; `:418` carries a dated retraction rather than a silent edit, because
R17 was justified by the wrong version and whoever implements it needs to know that.

**What a retraction that keeps the reader oriented looks like.** From
`eco-server-only-mod-client-rendering-surfaces.md:213` — it names the wrong claim, says it was
recorded here, gives the mechanism, points at the doc that supersedes it, and states what became of
the artifact built on it:

```text
- **RETRACTED — there is no custom-MonoBehaviour escape hatch.** This doc originally recorded
  that a mod could ship its own MonoBehaviour on the WorldObject prefab (this project's
  `DockReadoutDisplay`) to render arbitrary Unity UI from server-synced states. That is
  impossible. The Eco client is an IL2CPP build and **cannot load mod code at all** ...
  `DockReadoutDisplay` never ran; it rendered placeholder text forever because nothing could
  update it, and it has been deleted.
```

Compare the shape of the deleted RequireComponent doc's opening, which is what a confident wrong
claim reads like — no hedge, no provenance, no version stamp:

```text
`[RequireComponent(typeof(T))]` on a `WorldObject` class is a **construction** rule, not a shape
rule. It governs what components an instance receives when it is created; the resulting component
list is then serialized on that instance. Editing the attribute changes what future objects get
and leaves every existing object exactly as it was.
```

Every observation *supporting* that paragraph was real — a probe that produced nothing on existing
drones, a tab that stayed on docks after the attribute was removed. The inference from them was
wrong, and nothing in the sentence tells a later reader which part was observed and which part was
concluded. The replacement leads with engine source and line numbers instead
(`docs/solutions/conventions/requirecomponent-is-re-enforced-on-every-server-load.md`), quoting
`WorldObject.cs:536-541` and `WorldObjectManager.cs:161` directly.

**The self-contradicting README.** Worth keeping as a reminder that "one artifact" is not the same
as "one copy":

```text
README.md:9      There is no `Eco.ReferenceAssemblies` package for 0.14 ... the reference
                 assemblies are built from a pinned source checkout instead

README.md:76     **The server half needs nothing extra.** `EcoServerMod/` builds straight from
   (pre-fix)     a clone — its Eco dependency comes from the `Eco.ReferenceAssemblies` NuGet
                 package.
```

The fix at `README.md:77-86` replaced the second with three named build tiers, and — the useful
detail — made the dependency between the two statements explicit: *"As noted at the top of this
file, there is no `Eco.ReferenceAssemblies` package for 0.14."* A cross-reference inside one file is
how you stop it drifting against itself.

## Related

- `docs/solutions/conventions/a-fix-does-not-reach-the-copies-already-taken.md` — the same mechanism
  in source rather than prose: `HarvestDrone.cs` was copied from `SurveyDrone.cs` and kept the
  pre-fix fuel tag along with the once-true comment justifying it. That one is about copies of code;
  this one is about copies of a claim, which are harder because they are paraphrases rather than
  duplicates and no diff will line them up.
- `docs/solutions/workflow-issues/a-remembered-capability-and-a-cited-file-are-claims.md` — the
  upstream half. That doc is about a claim entering the store unverified; this one is about what the
  store does with it afterwards. Read together they are the full lifecycle: nothing validates a
  claim on the way in, and once in, it multiplies.
- `docs/solutions/conventions/an-attribute-that-only-feeds-a-tooltip.md` — instance 2, and the
  model retraction: the original analysis is kept intact under a "Resolved" header because it is the
  state that produced a decision that is still live.
- `docs/solutions/architecture-patterns/client-animation-is-driven-by-name-not-by-mod-code.md` — its
  closing section, *"How far the wrong version travelled"*, is instance 4 traced copy by copy, and
  is where the false-positive-versus-false-negative asymmetry is argued.
- `docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md` — the whitelist that
  carried the false positive, and the retraction wording quoted above.
- `docs/solutions/workflow-issues/a-gate-that-discovers-nothing-passes-everything.md` — the
  automated analogue: a check whose corpus cannot contain the answer reports clean. Here the
  "corpus" is the audit scope of `ce-compound-refresh`, which is `docs/solutions/` and nothing else,
  so a wrong claim living in a plan or an ideation doc is structurally invisible to it.
- `docs/solutions/conventions/a-defensive-rule-outlives-the-danger-it-answered.md` — the adjacent
  time problem: a doc that was correct when written and is now residue. Claims 2 and 3 were both
  that shape before they were this one.
