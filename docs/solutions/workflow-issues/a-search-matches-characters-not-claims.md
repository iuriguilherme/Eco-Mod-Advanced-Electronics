---
title: "A search matches characters, not claims"
date: 2026-09-06
last_updated: 2026-09-15
category: workflow-issues
module: docs
problem_type: workflow_issue
component: development_workflow
severity: high
applies_when:
  - "Auditing a learning's claims by searching the tree for the symbol, attribute, or type it names"
  - "A search returns zero and that absence is about to be reported as a finding"
  - "A pattern anchors on incidental syntax -- an opening bracket, a leading attribute, a filename's spelling"
  - "A hit lands in a comment, a doc, or a commented-out declaration rather than in live code"
  - "A search result is about to become the evidence for editing, deleting, or \"fixing\" a document"
symptoms:
  - "A symbol's only occurrence is a comment saying it is absent, wrong, or deliberately switched off"
  - "A symbol reported dead is documented elsewhere as the misspelling it is"
  - "A commented-out class declaration reads as shipped code, because grep cannot see the comment block"
  - "A required attribute is reported missing on components that all carry it mid-list"
  - "The document being checked already named the exact failure the check then committed"
root_cause: "A textual search cannot distinguish mentioning a symbol from using one, so a hit in prose, a comment, or a commented-out block reads as a use, while a pattern anchored on incidental syntax reads a present symbol as absent."
resolution_type: workflow_improvement
tags:
  - grep
  - verification
  - false-positive
  - false-negative
  - learnings-audit
  - knowledge-store
  - ce-compound
  - eco-modding
related_components:
  - docs/solutions
  - EcoServerMod/AdvancedElectronics
  - scripts
---

# A search matches characters, not claims

## Context

A text search — `grep`, `rg`, an editor's find-in-files, a symbol lookup that is really a text search
underneath — answers one question and one question only: does this sequence of characters occur in
this file. It does not know whether the occurrence is a declaration, a call, a comment, a string
literal, a line inside a `/* … */` block, or a sentence in a design document explaining that the
thing named does not exist. Every one of those is a match, and every one of them is reported
identically.

That gap matters most in a corpus that *talks about* code. In this repository the C# sources carry
long explanatory comments, and `docs/solutions/` is a store of prose whose entire subject is the
code. In such a corpus, the string you are hunting for appears disproportionately in the places
asserting that it is absent, that it is wrong, or that it is switched off — because those are exactly
the sentences that have to name it in order to talk about it. The search finds the mention. The
reader, scanning a hit list, reads the mention as a use.

Four instances of this happened in a single session, all of them while auditing this repository's
learnings store against its own code. Three of the four are **false presence** — a hit that is a
mention rather than a use. One is **false absence** — a real use that a slightly-too-narrow pattern
could not see. All four have one root: a text search cannot distinguish *mentioning* a symbol from
*using* one.

Every instance below was re-verified against the working tree before this entry was written, and the
matched line is quoted as it actually stands.

### Instance one — a comment that negates itself

A search of `EcoServerMod/AdvancedElectronics` for `Serialized` looking for the attribute applied to
a computed property returned `SurveyComponent.cs`. The hit is the line immediately above the
property:

```
EcoServerMod/AdvancedElectronics/SurveyComponent.cs:83
    // NOT [Serialized] -- derived on every read, so there is no member to load a saved value
```

and the line it sits above is:

```
EcoServerMod/AdvancedElectronics/SurveyComponent.cs:85
    public bool Operating => this.Parent is DroneDockObject dock && dock.DroneIsWorking;
```

Read as a match, that reports a latent bug: `[Serialized]` on an expression-bodied property with no
backing field, which is the exact defect the search was looking for. Read as a sentence, it says the
precise opposite — the attribute is deliberately absent, and the comment exists to record the rule
being checked so that nobody adds it later. The search found the strongest possible statement that
the bug is not there and reported it as the bug.

The same file contains two more of these. `SurveyComponent.cs:88` and `:91` are doc-comment lines
reading *"Deserialization assigns `[Serialized]` members by invoking their setters"* and *"Not itself
`[Serialized]`: it must start false on every load"*, both attached to the private `ready` field,
which likewise does not carry the attribute. Three hits in one file, all of them prose about the
attribute, none of them the attribute.

### Instance two — a counter-example named because it is wrong

A symbol search for `HarvesterDroneItem` across the repository reported it dead — one hit, in a
markdown file, and no C# at all. The conclusion drawn was that a type had been deleted and a document
still referenced it.

The type is not dead; the name is wrong. The class is `HarvestDroneItem`, not `HarvesterDroneItem`,
declared at:

```
EcoServerMod/AdvancedElectronics/HarvesterDrone.cs:42
    public class HarvestDroneItem : RepairableItem, IWorldObjectComponentSource, IPersistentData
```

The file is `HarvesterDrone.cs`; the class inside it is `HarvestDroneItem`. The filename and the type
name differ by three characters, and the misspelling is the filename's spelling applied to the type.

At the time, the single repository occurrence of `HarvesterDroneItem` was inside
`docs/solutions/conventions/unregistering-a-crafting-table-does-not-hide-the-recipe.md`, in a passage
that exists for no other reason than to warn about this exact mistake:

```
docs/solutions/conventions/unregistering-a-crafting-table-does-not-hide-the-recipe.md:117-120
    Search alongside a name you know must be present, and confirm the name you are
    searching for is the real one — the class in `HarvesterDrone.cs` is `HarvestDroneItem`, not
    `HarvesterDroneItem`, and searching the filename's spelling returns zero for a type that is very
    much in the DLL.
```

So the search for a misspelled symbol found exactly one hit, and that hit was a document telling the
searcher that this symbol is the misspelling. The document was right, it was the only thing in the
repository that could have caught the error, and it was read as evidence of a dangling reference
rather than as the correction it is.

### Instance three — disabled code that still reads as code

The same conventions document records that for the `v0.2.0` release the mining and harvest drone
recipes were withheld by commenting out their entire `RecipeFamily`-derived classes, rather than
merely commenting out the `CraftingComponent.AddRecipe` call inside them. It also records why a
source `grep` cannot verify that:

```
docs/solutions/conventions/unregistering-a-crafting-table-does-not-hide-the-recipe.md:109-111
    **Verify against the compiled artifact, never against the source.** `grep` has no idea what a
    `/* … */` block means. Source-grepping a commented-out region reports exactly what a live region
    reports, so it cannot distinguish the two states you are trying to tell apart.
```

A `grep` for `class MiningDroneRecipe` matches whether the declaration is live or sealed inside a
comment block. That was the trap when the recipe was withheld. Today the same match means something
different again, and the difference is the whole point: **the recipes have since been restored**, so
the match is now a live declaration.

```
EcoServerMod/AdvancedElectronics/MiningDrone.cs:262-266
    // RESTORED (U11, R32): the mining drone now has mining behaviour, which is the only
    // reason this recipe was withheld.
    /// <summary>Recipe unlocking <see cref="MiningDroneItem"/>.</summary>
    [RequiresSkill(typeof(AdvancedElectronicsSkill), 1)]
    public partial class MiningDroneRecipe : RecipeFamily
```

The restoring commit is `a046d67` (*"feat(mining): drone cleanup and recipe (U11)"*), and it is
reachable from `origin/main` — `git branch -a --contains a046d67` lists `main` and `remotes/origin/main`
— so this restoration is merged and shipped, not branch-local. All four drone-family recipes are live
in the current tree, as the conventions document's own updated body states.

The lesson is therefore not "this string means the code is disabled". It is that the string means
*nothing at all about liveness*, in either direction, at any point in time. The identical `grep`
returned an identical result in three different world-states: recipe live before the withholding,
recipe commented out during `v0.2.0`, recipe live again after `a046d67`. A check whose output is
constant across the states you are trying to distinguish is not a weak check; it is not a check.

### Instance four — the symmetric failure, an over-narrow pattern

The fourth instance runs the other way. A search for `\[HasIcon` — the attribute name anchored to its
opening bracket — over the server mod was used to find `WorldObjectComponent` subclasses missing a
required icon-declaring attribute. Eco requires such a component to declare one of `[HasIcon]` or
`[NoIcon]`; the search anchored on the bracket was meant to find the ones that declare neither.

Against the current tree the account is very slightly off, and the tree wins. There are nine
`WorldObjectComponent` subclasses in `EcoServerMod/AdvancedElectronics`. A bracket-anchored search
over the icon attributes finds three of them — `DroneLifecycle.cs:48`, `DroneMoverComponent.cs:42`,
and `OreSensorComponent.cs:23` — each of which writes `[NoIcon]` alone on its own line, so the
opening bracket does immediately precede the attribute name. The remaining **six** are invisible to
that pattern, and every one of the six carries the attribute anyway, mid-list, where the character
before it is a space and not a bracket:

```
EcoServerMod/AdvancedElectronics/SurveyComponent.cs:56
    [Serialized, CreateComponentTabLoc("Survey", true), HasIcon]
```

The other five are `CropCeilingComponent.cs:40`, `FarmingComponent.cs:35`, `MiningComponent.cs:36`,
and `UIShowcaseComponent.cs:41` — all with the same `[Serialized, CreateComponentTabLoc(…), HasIcon]`
shape — plus `DroneModuleComponent.cs:56`, which is `[Serialized, NoIcon]`. So of the six the pattern
could not see, five declare `HasIcon` and one declares `NoIcon`; all six declare an icon attribute,
and none of them has the defect the search was hunting for. Zero of the six reported gaps were real.

The pattern was not lazy. `\[HasIcon` was written that way deliberately, to avoid matching the many
lines of prose in this repository that discuss `HasIcon` without applying it — `AdvancedElectronics.cs`
alone contains twelve such comment lines, including *"[HasIcon] on an Item subclass looks inert,
because the lookup is INHERITED"* at `AdvancedElectronics.cs:52`. The bracket anchor was an attempt
to defend against false presence, and it bought false absence instead. That is the pairing worth
noticing: the two failures are not independent, and the natural defence against one manufactures the
other.

### Where these landed

Three of the four happened while checking a document's claims against the code. In three of the four,
the document being checked already warned about the exact failure the checker then committed. The
conventions document names `HarvesterDroneItem` as the misspelling and was read as a dangling
reference; it says `grep` cannot see a comment block and was audited with a `grep`; it prescribes a
control search and the control was not run.

That is not bad luck. The corpus most likely to mention a symbol without using it is documentation
about code, and documentation about code is precisely the corpus an audit searches. The activity that
most needs to distinguish a mention from a use is the activity conducted over the material richest in
mentions.

### Three earlier instances, and one thing never done (session history)

This session's four are not the first. Earlier work on other branches produced three more, each a
different shape of the same failure, and one finding that decides how much the guidance below is
worth (session history).

**An empty result from the wrong instrument, trusted six times.** A client-side UI teardown was
hunted through the *server* log across six separate passes. Every pass came back empty and the
emptiness was read as exculpatory, until someone escalated to the client log and found the actual
signature there. The conclusion recorded at the time: *"the server log isn't the quiet instrument —
it's the wrong one."* A zero says nothing at all when the corpus could not have held the answer,
which is a failure one step before the two below — not a wrong pattern, a wrong haystack.

**Six true hits that were still the wrong answer.** Looking for what made certain world objects show
link-toggle checkboxes, a search for callers of two `LinkComponent` methods returned exactly six call
sites. All six were genuine. The cause was none of them: it was `InOutLinkedInventoriesComponent`, a
marker component that calls neither method and is detected by its mere presence. Taken at face value
the six true positives would have pointed at the wrong fix, and only a cross-check against the
object's actual component list caught it. **A true positive is not a correct conclusion**, and a
search can only ever return what its pattern can express.

**A documented search that was itself over-narrow.** `eco-server-only-mod-client-rendering-surfaces.md`
told readers to grep the client log for three literal strings, framed as a checklist rather than as
examples. All three are crash-shaped; a component-churn bug found later throws no exception and
matches none of them. The assessment at the time: *"A reader following this literally greps, finds
nothing, and concludes the client log is clean too."* The doc was Updated rather than Replaced — the
advice to read that log first was still right, only the enumerated strings were incomplete.

**And the finding that matters most for what follows.** A search of that history turned up **no
instance of a zero result ever being validated against a known-present control before it was
trusted**. The technique is written down in this store, and in the sessions examined it has never
once been run. That is the gap this entry exists to close: the control is not a new idea here, it is
an unused one.

## Guidance

**For an absence, prove the pattern before you believe the zero.** A search that returns nothing has
two possible explanations, and they look identical: the tree does not contain the thing, or your
pattern cannot match the thing. Resolve that ambiguity by running the same pattern against a case you
know is present. If the control does not match, your pattern is wrong and the tree is not empty. If
the control does match, the zero is evidence. This costs one extra command and it converts a
guess into a measurement.

**This technique is not new here, and the credit belongs to an existing document.**
`docs/solutions/conventions/unregistering-a-crafting-table-does-not-hide-the-recipe.md` already
carries it as a bolded guidance lead:

> **Give an artifact check a control.** A name-based search over a binary fails the same way whether
> the type is absent or your spelling is wrong, and both look like success when absence is what you
> are hoping for. Search alongside a name you know must be present, and confirm the name you are
> searching for is the real one.

A second document holds the same idea for source rather than binaries.
`docs/solutions/conventions/auditing-content-derived-from-autogen-templates.md:80-85` prescribes
writing the grep so it cannot silently match nothing, and sanity-checking the pattern against a line
you know should match, and builds its residue sweep with `*`
rather than `+` specifically so the pattern cannot silently match nothing. Between them the store
already holds two thirds of the false-absence half; what neither states is that the two directions
have one root, and neither addresses false presence at all.

That first entry states the technique for one specific case: a `strings`-based search over a compiled
`.dll`, where the question is whether a withheld type made it into the release artifact. Its worked
example searches eight names at once precisely so that the four expected-present names act as the
control for the four expected-absent ones. What this entry adds is scope and a second half. The
scope: the control is not a binary-search technique, it is a *search* technique, and it applies
unchanged to `grep` over C# sources and to `grep` over markdown. The second half: the original entry
covers false absence only — it is written for someone hoping to see a zero — and says nothing about
the opposite error, where a search returns hits and every hit is a mention.

**For a presence over a corpus containing prose or comments, read the matched line rather than
counting it.** A count is a claim about occurrences; the finding you want is a claim about uses. In a
mixed corpus those are different numbers, and the only reliable conversion between them is reading.
This sounds like an unaffordable rule and is not: hit counts here are small, the read is one line
plus enough context to see what the line is doing, and the line that negates itself is usually
obvious the moment it is read as a sentence instead of scanned as a token. `SurveyComponent.cs:83`
announces itself with the word `NOT` in the first three characters after the comment marker.

**When the corpus mixes code and prose, separate them before you search, and search them
differently.** Concretely, for this repository:

- Scope the search. `EcoServerMod/**/*.cs` and `docs/**/*.md` answer different questions and should be
  separate commands with separately-read results. A finding about code that comes back from a `.md`
  path is a mention until proven otherwise.
- Expect the documentation corpus to over-report. A store of learnings is written *about* mistakes,
  so misspellings, removed types, wrong API names and disabled constructs all appear in it as
  quoted counter-examples. That is the store working correctly. A hit there is more likely to be a
  warning about the symbol than a use of it.
- Do not defend against prose hits by tightening the pattern. That is what produced instance four.
  Defend against them by narrowing the *corpus* — which is visible and reviewable — rather than the
  *pattern*, whose over-narrowing is silent. This is the same asymmetry
  `docs/solutions/workflow-issues/a-gate-that-discovers-nothing-passes-everything.md` establishes:
  over-broadening is loud and self-correcting, over-narrowing is quiet and permanent.
- Where the question is genuinely about what the built code contains rather than what the sources
  say, do not search text at all. Build and search the artifact, as the conventions entry requires.

**One tooling connection is real and worth stating.** `scripts/validate-name-match.sh` discovers C#
types by grepping the sources, which is the same instrument these four instances misused, and the
script's own commit history is a series of pattern-narrowing bugs: `9946097`
(*"fix(scripts): name-match gate discovered almost nothing and passed"*), `8c70b57`
(*"fix(scripts): plugin modules are holdable items and need icons too"*), `0ca6b2c`
(*"fix(scripts): both drone items had dropped out of the name-match gate"*), and later `95ef162`
(*"feat(scripts): discover Skill subclasses in the name-match gate"*, branch-local on
`feat/tech-tree-icons` and so not a durable reference, unlike the three before it). The first of those is
character-for-character instance four at a larger scale: an end-of-line anchor, added for a correct
reason, made almost every real declaration invisible. The script's own comment records it, at
`scripts/validate-name-match.sh:50-57`, ending *"a green gate that verified almost nothing."*

The script has since been hardened against exactly the failures described here, and its hardening is
worth copying by hand. It strips `//` comments before discovering anything — `sed -E 's://.*$::'` at
`scripts/validate-name-match.sh:60`, with the comment above it saying *"so commented-out template code
is never discovered as a real type"*, which is the false-presence defence. It normalizes the source
into one whitespace-collapsed string so a multi-line base list is still one declaration, which is the
false-absence defence. And it prints its discovered denominator on every run. Note that the strip
handles `//` only: a `/* … */` block of the kind instance three describes survives it, so the script
would still discover a block-commented type. The gap is narrow but it is there.

## Why This Matters

**The two failure directions cost differently, and the expensive one is the quiet one.**

A false absence is loud once you look. It sends you to write code that already exists, or to file a
gap that is not a gap, and the first thing you do is open the file — at which point the attribute is
sitting there in plain sight and the error collapses in seconds. Instance four cost the time to open
six files. It is embarrassing rather than expensive, and it is self-limiting because the next step
after "this is missing" is almost always "let me add it", which requires opening the file.

A false presence is worse, because it sends you to fix a bug that does not exist. The next step after
"this is broken" is a change. In instance one that change would have been to *remove* a
`[Serialized]` attribute that was never there — which, given how the surrounding comment is worded,
most plausibly means editing the comment to match the imagined state, thereby destroying the record of
why the attribute is absent and setting up its future re-addition. In instance two it would have been
to delete or "fix" a documented counter-example, removing the only warning in the repository about
that misspelling. In both cases the fix damages the thing that was right.

Note the shape: a false presence in a documentation corpus does not merely waste effort, it tends to
propose deleting the correction. The mention that triggered the false hit is usually a warning, and
the natural response to "this reference is stale" is to remove the reference.

**And then the recursive part.** The corpus an audit searches is the corpus most full of mentions. An
audit of a learnings store against the code is, definitionally, a search over prose whose whole
subject is the code. That prose names dead symbols, wrong spellings, superseded APIs and disabled
blocks on purpose, because naming them is how it warns. So the activity with the highest need to
distinguish a mention from a use is conducted over the material where the ratio of mentions to uses
is worst — and where a false finding, if acted on, deletes a warning that was placed there by someone
who had already paid for the lesson.

`docs/solutions/workflow-issues/a-knowledge-store-corroborates-its-own-errors.md` describes the store
propagating a wrong claim between its own entries. This is a different way for the store to be
damaged by the act of maintaining it: not a wrong claim spreading, but a correct warning being read as
the error it warns about, and removed for being correct.

## When to Apply

- Whenever a search result is about to become a finding. The step between "grep returned this" and "I
  will now change something" is where the matched line gets read, and it is the only place the read
  can happen.
- When auditing documentation, plans, or a learnings store against code. This is the highest-risk
  case, because the corpus is prose about code and its mention-to-use ratio is at its worst.
- When a search returns zero and you are about to report an absence — run the control before
  reporting.
- When you are about to narrow a pattern to suppress prose hits. Narrow the corpus instead.
- When the question is what a shipped build contains rather than what the sources say. Then the
  answer is not in the sources at all and no amount of careful grepping will reach it; build and
  search the artifact, per the conventions entry.
- When a symbol search comes back with exactly one hit, in a markdown file. That is the signature of
  instance two: a name that exists only where something is explaining that it is wrong.

**Where this does not apply.** A compiler and a language server resolve *uses*, not mentions. C#
"Find All References", a rename refactor, or the build itself will never report a comment, a string
literal, or a paragraph of markdown as a reference to a symbol, because they operate on a parsed
program rather than on characters. Where such a tool is available and the question is about the code,
it is strictly better than a search and this entry does not apply to it. The whole failure mode
described here is a property of tools that do not parse.

**And this is not an argument against grep.** In the same session, `grep` found every real finding
there was. It located the recipe restoration, the nine component classes and their attributes, the
one true declaration of `HarvestDroneItem`, and the single stale-looking reference that turned out to
be a deliberate counter-example. Text search over this repository is fast, exhaustive, and needs no
build. The correction is not to search less. It is to spend one extra command proving a pattern before
believing a zero, and one extra glance reading a line before counting it.

## Examples

The four instances, each as the command run, the match returned, and what the match actually was.

**One — the comment that negates itself.**

```
$ grep -rn 'Serialized' EcoServerMod/AdvancedElectronics/SurveyComponent.cs
```

Among the matches:

```
EcoServerMod/AdvancedElectronics/SurveyComponent.cs:83:    // NOT [Serialized] -- derived on every read, so there is no member to load a saved value
```

Counted, that is `[Serialized]` on a computed property, which is the bug being hunted. Read, it is a
comment stating that the attribute is deliberately absent from
`public bool Operating => this.Parent is DroneDockObject dock && dock.DroneIsWorking;` on the line
below, and explaining why. The word `NOT` is the first token of the comment.

**Two — the counter-example named because it is wrong.**

```
$ grep -rn 'HarvesterDroneItem' .
docs/solutions/conventions/unregistering-a-crafting-table-does-not-hide-the-recipe.md:119:`HarvesterDroneItem`, and searching the filename's spelling returns zero for a type that is very
```

One hit, in prose, no C#. Read as a match: a dead symbol still referenced by a document. Read as a
sentence, with the line above it at `:118` — *"confirm the name you are searching for is the real one
— the class in `HarvesterDrone.cs` is `HarvestDroneItem`, not"* — it is a warning that the searched
name is a misspelling. The real class is at `EcoServerMod/AdvancedElectronics/HarvesterDrone.cs:42`,
and the correct spelling has fifteen occurrences across the server sources, the Unity scene, the
editor tooling and four documents.

**Three — disabled code that still reads as code.**

```
$ grep -rn 'class MiningDroneRecipe' EcoServerMod/
EcoServerMod/AdvancedElectronics/MiningDrone.cs:266:    public partial class MiningDroneRecipe : RecipeFamily
```

This exact command returned a matching line when the class was live before `v0.2.0`, returned a
matching line when the entire class was sealed inside a `/* … */` block for `v0.2.0`, and returns a
matching line now that `a046d67` has restored it. Today the match is a live declaration — the
comment two lines above it at `:262` reads *"RESTORED (U11, R32): the mining drone now has mining
behaviour, which is the only reason this recipe was withheld."* — and `a046d67` is reachable from
`origin/main`, so this state is merged. What the match never was, in any of the three states, is
evidence about which state the tree is in.

**Four — the over-narrow pattern.**

```
$ grep -rn '\[HasIcon' EcoServerMod/AdvancedElectronics/
```

Returns item, skill and upgrade declarations such as `AdvancedElectronics.cs:69` and
`AdvancedElectronicsUpgrade.cs:146`, plus a dozen comment lines discussing the attribute, and not a
single `WorldObjectComponent` subclass. Broadened to include `NoIcon`, the bracket anchor still finds
only the three components that put the attribute on a line of its own. The six it cannot see all
carry it after a comma:

```
EcoServerMod/AdvancedElectronics/SurveyComponent.cs:56:    [Serialized, CreateComponentTabLoc("Survey", true), HasIcon]
```

The control that would have caught it in one command: run the pattern against a component you know
declares the attribute. `SurveyComponent` was the file already open at the time. Had `\[HasIcon` been
run against it and returned nothing, the pattern would have been indicted rather than the tree.

The dropped anchor, as the fix:

```
$ grep -rn 'HasIcon\]\|NoIcon\]' EcoServerMod/AdvancedElectronics/*Component.cs
```

which returns all six, alongside the three the anchored version already found.

## Related

- `docs/solutions/conventions/unregistering-a-crafting-table-does-not-hide-the-recipe.md` — the
  source of the control technique, under **"Give an artifact check a control."** That entry states it
  for a binary artifact search where the hoped-for answer is zero; this one extends it to source and
  prose searches and adds the false-presence half it does not cover. It is also, itself, three of the
  four instances above: it names the misspelling, it warns that `grep` cannot read a comment block,
  and it was the document being audited.
- `docs/solutions/workflow-issues/a-gate-that-discovers-nothing-passes-everything.md` — the same
  over-narrowing failure as instance four, automated and left running. Its asymmetry rule
  ("over-narrowing a pattern is silent; over-broadening is loud") is the reason this entry says to
  narrow the corpus rather than the pattern.
- `docs/solutions/workflow-issues/a-knowledge-store-corroborates-its-own-errors.md` — the other way
  auditing a learnings store goes wrong. There a wrong claim propagates between entries; here a
  correct warning is mistaken for the error it warns about and proposed for deletion.
- `docs/solutions/workflow-issues/a-cross-reference-makes-two-claims-and-only-the-path-is-checked.md`
  — the adjacent verification gap in the same corpus: confirming a cited path exists is not
  confirming it says what the citation claims.
- `docs/solutions/workflow-issues/validate-the-instrument-before-the-hypothesis.md` — the general
  form of "run the control first", stated for measurement rather than for search.
- `docs/solutions/workflow-issues/a-fixed-defect-in-the-present-tense-passes-every-check.md` — the
  time axis of instance three: text that described a real state, describes a different state now, and
  reads identically in both.
