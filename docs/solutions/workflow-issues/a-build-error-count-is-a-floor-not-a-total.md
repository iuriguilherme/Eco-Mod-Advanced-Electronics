---
title: "A build's error count is a floor, not a total"
date: 2026-08-06
category: workflow-issues
module: EcoServerMod
problem_type: workflow_issue
component: development_workflow
severity: medium
applies_when:
  - "Estimating how much work remains from a failing build's error count"
  - "Reporting build progress before the count has actually reached zero"
  - "A file reports one error and is assumed to be otherwise sound"
  - "Triaging a batch of newly authored or newly copied files for the first time"
tags: [build-errors, compiler, csharp, roslyn, estimation, eco-modding, triage]
related_components: [EcoServerMod/AdvancedElectronics]
---

# A build's error count is a floor, not a total

## Context

A batch of new mod content was compiled for the first time. `dotnet build` reported **7 errors**
across three files, arising from three kinds of defect: a missing `using` (in two files, 2 errors
each), a class declaring partial methods without being `partial` (2 errors), and an attribute naming a
type that had never been written (1 error). All of them were fixed.

The rebuild reported **14 errors**.

Nothing regressed. The tree was not touched beyond those fixes, and every one was correct. The second
build simply reported problems the first build had declined to mention — including eight in a file the
first build had flagged only at a single line.

## Guidance

**A failing build's error count tells you what the compiler got far enough to notice, not how much is
wrong.** Compilation resolves declarations before it binds method bodies. While an unrecovered
declaration-level or syntax error stands anywhere in the compilation, method-body binding does not
happen, so every error inside every method body is invisible. Fix the declarations and the bodies get
their first inspection. This is not a diagnostic cap or a truncated list; the later errors were never
computed.

**The masking is compilation-wide, not per-file.** This is the part that inverts the intuition. A file
with no declaration problems of its own, in no way related to the broken one, still has all of its
body-level diagnostics suppressed — the unit is the compilation (the whole project build), not the
file. Reproduced directly: an unresolved attribute in `Class1.cs` suppresses a body error in
`Class2.cs`, in either ordering, and a plain missing semicolon does it too. So the trigger is not
specifically attributes or base types; it is any declaration-level or syntax error anywhere in the
build.

**Therefore: a file reporting no errors at all may be arbitrarily broken.** Not merely a file
reporting few. As long as one unrecovered declaration error exists anywhere in the project, silence
about a given file carries no information about that file's method bodies. "Nothing in my file" is not
evidence until the build is otherwise clean.

**The count is wrong in both directions at once, which is why it feels trustworthy.** It *under*counts
by hiding unbound bodies, and it *over*counts because an unresolved attribute is reported twice — C#
resolves `[Foo]` by trying both `Foo` and `FooAttribute`, and both failures are reported. Two of the
seven errors in this run were one missing `using`, counted twice. So the number is simultaneously
inflated relative to causes and deflated relative to problems, and the two errors partly cancel,
producing a figure that looks plausible and means very little.

**Never report remaining work from a build you have not re-run.** "Seven errors, four causes, all
fixed" is a true statement about a past build and a claim about nothing. The only defensible progress
signal is the count from the most recent build — and the only defensible finish signal is zero. This
matters most in exactly the situation where it is most tempting: reporting to someone who is waiting.

**Triage newly authored files by iterating the build to a fixed point, not by reading its first
output.** For a batch of new or copied files, expect several rounds where the count *rises*. Rising is
the normal shape of that work, not evidence of a mistake. Budget for the shape.

## Why This Matters

The direct cost is a wrong estimate delivered with confidence. Reporting "seven errors, four causes,
fixed" invites the reader to believe the work is finished, and the correction — fourteen — arrives
after they have already updated their picture. That is worse than reporting nothing, because the first
number was specific enough to be trusted.

The subtler cost is triage order. A file showing few errors — or none — gets deprioritised behind a
file showing six, when it may be the more broken of the two. Any prioritisation that ranks files by
reported error count is ranking them partly by how early the compilation failed, which is unrelated to
how much is wrong.

There is a compounding version too, and the compilation-wide scope is what makes it bite. Template-
derived files carry residue at both levels — copied attributes and base classes in the declarations,
copied type references inside method bodies. It takes only *one* file in the batch to carry a
declaration-level item for the body-level residue in *every* file to go unreported. That is what turns
a modest first number into a much larger second one. The first build of a batch is the least
informative build you will run, and it is the one whose number gets quoted.

## When to Apply

- Before stating how much work remains on a failing build, or how close it is to green. Re-run first.
- Whenever the build contains any unresolved declaration-level or syntax error. Until it is gone,
  treat every method body in the project as unexamined — including in files that reported nothing.
- When triaging a batch of new or copied files, particularly ones derived from templates, where
  declaration-level failures cluster.
- When an error count rises after a correct fix — recognise it as the expected shape and keep going,
  rather than suspecting the fix.
- When the build is being used as a completion gate by someone else. Only zero means anything.

## Examples

The mechanism, reduced to one file and reproducible from scratch. An unresolved attribute on the type
plus two unresolved types inside a method body:

```csharp
using System;

[TotallyMissingAttribute]            // unresolved ATTRIBUTE on the type
public class Widget
{
    public void Run()
    {
        MissingTypeInBody x = null;  // unresolved type in a METHOD BODY
        AlsoMissing y = null;        // second unresolved type in a method body
        Console.WriteLine(x);
        Console.WriteLine(y);
    }
}
```

First build. Only the attribute is reported — and reported twice, once per name the compiler tries.
Lines 7 and 8 are not mentioned:

```console
Program.cs(3,2): error CS0246: The type or namespace name 'TotallyMissingAttribute' could not be found
Program.cs(3,2): error CS0246: The type or namespace name 'TotallyMissingAttributeAttribute' could not be found
```

Delete the attribute line, changing nothing else, and rebuild. The body errors appear for the first
time:

```console
Program.cs(7,9): error CS0246: The type or namespace name 'MissingTypeInBody' could not be found
Program.cs(8,9): error CS0246: The type or namespace name 'AlsoMissing' could not be found
```

Two errors became two different errors, and at no point did any single build show all three problems.
(Use a class library rather than a console project to reproduce — a console project with no `Main`
adds an unrelated CS5001 that muddies the output.)

The scope is wider than one file. Put the unresolved attribute in `Class1.cs` and an unrelated body
error in `Class2.cs`, with nothing connecting them:

```csharp
// Class1.cs
[TotallyMissingAttribute]
public class Widget { }

// Class2.cs — no attribute, no relation to Widget
public class OtherFileClass
{
    public void Run() { SomeOtherMissingType x = null; }
}
```

The build reports only the two attribute errors from `Class1.cs`. `Class2.cs` is not mentioned at all,
in either file ordering, and the same happens if the trigger is a plain missing semicolon rather than
an unresolved type. Remove the declaration error and `Class2.cs`'s body error appears immediately.
A file that reports nothing has not been cleared; it may simply not have been reached.

The same shape at batch scale, from the run that prompted this. `SurveyDrone.cs` in the first build:

```console
SurveyDrone.cs(173,6): error CS0246: ... 'RequiresSkillAttribute' could not be found
SurveyDrone.cs(173,6): error CS0246: ... 'RequiresSkill' could not be found
```

One line, one missing `using`, counted twice. After adding that `using`, the same file:

```console
SurveyDrone.cs(154,18): error CS1061: ... does not contain a definition for 'ModsPreInitialize'
SurveyDrone.cs(156,31): error CS0246: ... 'FuelSupplyComponent' could not be found
SurveyDrone.cs(158,18): error CS1061: ... does not contain a definition for 'ModsPostInitialize'
SurveyDrone.cs(160,87): error CS0246: ... 'PartInfo' could not be found
SurveyDrone.cs(189,50): error CS0246: ... 'InsulatedCopperWiringItem' could not be found
SurveyDrone.cs(209,33): error CS7036: no argument given for required parameter 'skillType'
SurveyDrone.cs(210,18): error CS1061: ... does not contain a definition for 'ModsPreInitialize'
SurveyDrone.cs(212,18): error CS1061: ... does not contain a definition for 'ModsPostInitialize'
```

Every one of those lines existed, unchanged, during the first build.

## Related

- `docs/solutions/conventions/auditing-content-derived-from-autogen-templates.md` — the same batch of
  files and the same incident, catalogued by what the residue *is* rather than by when the compiler
  reports it. Note that its residue spans both levels: some is declaration-level (attributes, base
  classes) and some sits in method bodies, so it documents the material this doc's masking acts on
  without itself establishing the ordering.
- `docs/solutions/workflow-issues/a-gate-that-discovers-nothing-passes-everything.md` — the same
  epistemics on a different tool: a clean-looking result that reflects what the check managed to
  examine rather than what is true. Adjacent rather than overlapping — no shared mechanism.
- `docs/solutions/workflow-issues/validate-the-instrument-before-the-hypothesis.md` — the third member
  of that family, and the closest existing sentence to this doc's core claim: "a run that did not
  reach the code under test is not a negative result." A build that never reached a file's method
  bodies is not a clean result for that file.
