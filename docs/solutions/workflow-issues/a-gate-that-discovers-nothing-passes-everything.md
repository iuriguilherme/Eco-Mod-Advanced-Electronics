---
title: "A gate that discovers nothing passes everything"
date: 2026-07-30
category: workflow-issues
module: AdvancedElectronics
problem_type: workflow_issue
component: tooling
severity: high
applies_when:
  - "A check discovers its own inputs before validating them (grep, glob, reflection, directory scan)"
  - "A validation script has passed for a long time without anyone reading its output"
  - "A plan cites an existing gate as evidence that new work is correct"
  - "Narrowing a pattern to exclude false positives, where over-narrowing is silent"
tags: [validation, verification, false-confidence, grep, regex, tooling, eco-modding, name-match]
related_components: [scripts, EcoServerMod/AdvancedElectronics]
---

# A gate that discovers nothing passes everything

## Context

`scripts/validate-name-match.sh` is this mod's guard against its worst silent failure: a client asset
whose name does not exactly equal its server class name renders nothing, logs nothing, and looks like
an art problem rather than a naming one.

The script works in two halves. First it **discovers** the server types that need client assets, by
grepping the C# sources for class declarations. Then it **matches** each discovered type against the
assets on disk and fails on any type with no counterpart.

The matching half was correct and had always been correct. The discovery half found two types. The
mod had eleven.

The gate reported `PASS: every server WorldObject/Item type has a matching-named client asset` on
every run, and it was telling the truth — every type it knew about did have an asset. It simply knew
about almost none of them.

## Guidance

**Assert the denominator, not just the failures.** A check that iterates a collection and fails on
bad members passes trivially when the collection is empty. The interesting number is not how many
items failed, it is how many were examined. If a gate cannot state how many things it checked, it
cannot distinguish "all clear" from "found nothing to look at."

**Over-narrowing a pattern is silent; over-broadening is loud.** The two failure directions are not
symmetric, and the quiet one is the dangerous one:

- Too broad: the gate reports types that need no asset. Cost: a visible false failure someone
  immediately investigates and fixes.
- Too narrow: the gate reports nothing. Cost: a green check for years, and no signal at all.

This is the same asymmetry argument as
`docs/solutions/workflow-issues/release-scripts-should-refuse-not-warn.md`, applied one step earlier.
That doc is about what a gate does once it has detected a bad condition. This one is about a gate that
never detects anything to act on — the matching logic there was never reached.

**Well-reasoned narrowing is the kind that survives review.** The anchors here were deliberate,
commented, and correct about the problem they were solving. A reviewer reading the script would have
agreed with the comment. What no one checked was the *effect* of the anchor on the real corpus, which
is a different question from whether the anchor's stated rationale is sound.

**Printing the blind spot is not the same as noticing it.** This script already echoed its discovered
type list on every single run, in the first line of output. The blind spot was on screen, every time,
for weeks. Output that is always present becomes chrome; only a value that *changes state* — an exit
code, a failed assertion, a diff — reliably gets read.

**Verify a gate by breaking it.** The cheapest possible test of a discovery step is to delete an
asset you know is required and confirm the gate goes red. If it stays green, discovery is broken, and
that takes seconds to learn. Nobody had ever made this gate fail on purpose.

**Treat "this gate passes" in a plan as a claim to verify, not a fact to build on.** A plan for new
work cited this script as an acceptance criterion — "the name-match gate passes with the new types
present." Because discovery could not see any of the new types, that criterion would have been
satisfied by doing nothing at all. An acceptance criterion that rests on an unexamined gate inherits
all of that gate's blind spots while looking like independent evidence.

## Why This Matters

The direct cost here was a real user-facing defect that had been shipping unnoticed: `DroneDockItem`
had no icon at all. It is not new, not subtle, and would have been caught the first time the gate ran
correctly. It was invisible because the only automated thing that could have seen it had been looking
past it since it was written.

The compounding cost is worse than the missed defect. A green gate is not neutral — it actively
suppresses investigation. Every run added confidence that naming was under control, which is exactly
the belief that stops anyone from checking naming by hand. A gate that does nothing is worse than no
gate, because no gate at least leaves the doubt intact.

This generalises past grep. Any check whose first step is discovery has the same structure and the
same failure: a test suite that globs for `*_test.rb` and finds none passes; a linter with a
`files:` pattern matching nothing reports clean; a reflection-based registry check that filters on a
base type nobody inherits from any more validates an empty set. In all of them the reported result is
*correct* and *useless*, which is why it survives.

## When to Apply

- When writing any check that discovers its own inputs — before trusting its first green run, make it
  red on purpose.
- When narrowing a pattern to suppress false positives. The narrowing is the moment the risk is
  introduced, and it is the moment the corpus is in front of you and cheap to count against.
- When a gate has passed for a long time without anyone reading its output. Longevity is not evidence
  of correctness; it is equally consistent with the gate having been inert the whole time.
- When a plan, PR description, or acceptance criterion cites an existing automated check as evidence.
  Ask what that check actually inspects before treating it as independent confirmation.
- When a check's output includes a list, a count, or a "found N items" line that nobody has looked at
  in months.

## Examples

The discovery step as originally written. The `*$` anchors exclude `WorldObjectItem<T>` and
`WorldObjectComponent`, which is a real requirement and the reason the anchors exist:

```bash
grep -rhoE 'class [A-Za-z0-9_]+ : WorldObject *$' "$SERVER_DIR"
grep -rhoE 'class [A-Za-z0-9_]+ : Item *$' "$SERVER_DIR"
```

End-of-line is doing two jobs at once, though, and only one was intended. It also excludes every
declaration carrying a trailing interface, a trailing brace, or a generic base:

```csharp
// trailing interface
public partial class AdvancedElectronicsAssemblyObject : WorldObject, IRepresentsItem

// trailing brace
public partial class EngineeringResearchPaperPostModernItem : Item    {

// generic base
public partial class AdvancedElectronicsSkillBook : SkillBook<AdvancedElectronicsSkill, AdvancedElectronicsSkillScroll> {}

// base list spanning several lines
public partial class BatteryItem :

    BlockItem<BatteryBlock>
    {
```

That is nearly every declaration in the mod, including ones written years apart by different
conventions.

What the gate printed on every run — the first line is the blind spot, in plain sight:

```
Server WorldObject types (need a name-matching prefab): SurveyDroneObject
Server Item types (need a name-matching icon asset):    SurveyDroneItem

PASS: every server WorldObject/Item type has a matching-named client asset.
```

The fix keeps the exclusion but stops using end-of-line to express it. Discovery now runs against a
normalized view — comments stripped, lines joined, whitespace collapsed — so a multi-line base list is
one string, and the `WorldObjectItem`/`WorldObjectComponent` exclusion is carried by requiring a word
boundary after the base name instead:

```bash
normalize_sources() {
  find "$SERVER_DIR" -name '*.cs' -exec cat {} + \
    | sed -E 's://.*$::' \
    | tr '\n' ' ' \
    | sed -E 's/[[:space:]]+/ /g'
}

# " ,{" after the base name is what keeps WorldObjectItem<T> and
# WorldObjectComponent out, without also excluding trailing interfaces.
grep -oE 'class [A-Za-z0-9_]+ : WorldObject[ ,{]'
```

The same run, after the fix — and the first genuine failure the gate had ever produced:

```
Server WorldObject types: AdvancedElectronicsAssemblyObject DroneDockObject SurveyDroneObject
Server Item types:        AdvancedElectronicsAssemblyItem AdvancedElectronicsSkillBook
                          AdvancedElectronicsSkillScroll BatteryItem DroneDockItem
                          EngineeringResearchPaperPostModernItem SurveyDroneItem

MISMATCH: Item 'DroneDockItem' has no matching-named icon asset ...
```

`DroneDockItem` is the payoff. It is not one of the new types the run was about — it had been
shipping without an icon, and the gate that existed to catch precisely that had never once looked at
it.

A later addition in the same session repeated the lesson at smaller scale. `AdvancedElectronicsUpgradeItem`
derives from `EfficiencyModule`, which was not in the list of item base classes, so a newly written
item sailed through a gate that had just been fixed. Discovery lists are not written once; every new
base class is a new chance for the same silence.

## Related

- `docs/solutions/workflow-issues/release-scripts-should-refuse-not-warn.md` — the adjacent failure at
  the next step: that doc is about a gate that detects a bad condition and declines to fail on it;
  this one is about a gate whose detection stage was empty, so its failure logic never ran.
- `docs/solutions/workflow-issues/verify-the-deploy-landed-before-asking-for-a-restart.md` — the same
  shape in the deploy path: a step that reports success without having confirmed the thing it claims.
- `docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md` — why name match is
  the invariant worth gating on in this project at all.
