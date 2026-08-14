---
title: "The compile target decides what exists, not the source tree next to it"
date: 2026-08-01
last_updated: 2026-08-10
category: workflow-issues
module: EcoServerMod
problem_type: workflow_issue
component: development_workflow
severity: high
applies_when:
  - "Designing against an engine type the mod does not already use"
  - "Reading a vendored or reference source tree to learn how an API works"
  - "The reference source and the shipped package can be at different versions"
  - "A search against one tree returns nothing and the search moves to another tree"
tags: [research-grounding, reference-assemblies, versioning, methodology, eco-modding, planning]
related_components: [EcoServerMod/AdvancedElectronics]
---

# The compile target decides what exists, not the source tree next to it

## Context

This mod is developed with a full Eco source checkout beside it, because the shipped reference
assemblies have their method bodies stripped — reading real implementations is only possible in that
tree. Every grounded claim in this repo's docs about engine behaviour comes from there, and that is
correct practice.

The trap is that the checkout and the compile target are **separate artifacts with separate
versions**. The checkout sits on whatever branch it was last pulled to; the mod compiles against
reference assemblies built from that checkout at the commit pinned by `EcoRefSha`. The pin is a
guard, not a guarantee — moving the checkout after gathering leaves you reading a tree the resolved
assemblies no longer come from, and the divergence is invisible while reading. When this incident
happened the two were not connected at all: the mod pinned an `Eco.ReferenceAssemblies` NuGet
package with nothing tying it to the checkout.

A planning session designed an entire architecture — a module system in which a slotted item declares
components the host installs — on APIs read out of that checkout. The checkout was on `staging`, 860
commits past the tag the mod builds against. Four of the load-bearing types did not exist in the
compile target at all. The design was coherent, cited real source with line numbers, survived a
document review, and could not have compiled.

It was caught by the maintainer mentioning in passing that the feature was a v14 thing.

## Guidance

**The compile target is the authority on what exists. Check it before designing on anything new.**
Not the source tree, not the deployed server, not another mod. The question "does this type exist"
has exactly one correct place to ask it: the reference assemblies the project actually resolves.

**PRESENT answers existence, not behaviour.** The check settles whether you can name a type, not
whether it does what you remember it doing — the two present types in the Examples below compiled
and were still the wrong foundation. When the claim is about what a tool or an API *produces*,
presence is the first question rather than the last, and a PRESENT that is read as a yes to the
larger question is how this discipline fails while appearing to pass.

**The check is seconds long.** Managed assembly metadata stores type names as plain strings, so a
binary grep answers it without any tooling:

```bash
D="$(grep -oP '(?<=<EcoRefAssembliesDir>)[^<]+' EcoServerMod/AdvancedElectronics/Local.props)Eco.Gameplay.dll"
grep -qa "IWorldObjectComponentSource" "$D" && echo PRESENT || echo ABSENT
```

Do **not** probe `~/.nuget/packages/eco.referenceassemblies/`. That package is still on disk and
still resolves — but only for the reference-only spike project, which is pinned to the old version.
It answers, and it answers about the wrong artifact.

Run it per type, in one loop, before the design work rather than after. There is no reason to skip a
check this cheap on a decision this expensive.

**Scope it — this is not a tax on every lookup.** The check earns its keep only for engine types the
mod does not already use. Anything the codebase already compiles against is proven present by the
build. The rule bites exactly when it matters: reaching for a capability for the first time.

**A search that comes back empty is a finding, not an obstacle.** The tell was there and got walked
past. A grep for the API against the deployed server's shipped sources returned zero hits; that was
noted, and the search moved to the source checkout, which had them. The two results together were the
whole answer — one tree has it, another doesn't, so which one do we build against? Instead the empty
result was treated as "wrong place to look" and the non-empty one as confirmation.

When two sources disagree about whether something exists, the disagreement *is* the result. Stop and
reconcile it before continuing.

**Reading ahead is fine; assuming availability is not.** Nothing was wrong with reading the newer
tree — it is the only place the implementation is legible, and the research it produced stayed valid
and got reused once the version question was settled. The error was one unexamined step: treating
"I can read it" as "I can call it."

## Why This Matters

A design built on absent APIs does not look broken. It cites real files and real line numbers, its
mechanisms genuinely work — somewhere — and every internal consistency check passes, because the
design *is* internally consistent. Structural review cannot catch it: a reviewer checking whether the
plan contradicts itself finds nothing wrong, and a reviewer checking whether it matches the source
finds it matches perfectly. The one question neither asks is whether the source is the right source.

That is why it survives further than most errors. In this case it survived approach selection, a full
implementation plan, a seven-finding document review, and several rounds of refinement. The cost was
not the compile failure it would eventually have caused — it was the entire body of downstream work
built on it, all of which had to be re-examined once the premise moved.

The asymmetry is what makes the rule worth keeping: verifying costs seconds, and being wrong costs
every decision that inherits the assumption.

## When to Apply

- Before designing against any engine type, interface, or attribute the mod does not already use.
- When a reference source tree exists beside the project and is updated independently of the
  compile target — including vendored SDKs, decompiled sources, and cloned upstream repos.
- Whenever a search for an API succeeds in one tree and fails in another.
- When a plan's central mechanism rests on a single API, regardless of how well documented it is.
- Before recording engine behaviour in `docs/solutions/` or `CONCEPTS.md` — a doc grounded in the
  wrong version outlives the session that wrote it.

## Examples

The probe that settled it, run against the package the mod resolved **at the time**. The mod has
since retargeted and no longer resolves this package — see the Guidance above for the probe to run
today. The snippet is kept because the result is the incident:

```bash
D=~/.nuget/packages/eco.referenceassemblies/0.13.0.4-beta-release-1024/lib/net10.0/Eco.Gameplay.dll
for t in IWorldObjectComponentSource ComponentInstallation IDeclaresMayHaveComponents \
         ComponentSourceRestriction GetOrCreateComponent IOperatingWorldObjectComponent; do
  printf "%-32s " "$t"
  grep -qa "$t" "$D" && echo PRESENT || echo ABSENT
done
```

```text
IWorldObjectComponentSource      ABSENT
ComponentInstallation            ABSENT
IDeclaresMayHaveComponents       ABSENT
ComponentSourceRestriction       ABSENT
GetOrCreateComponent             PRESENT
IOperatingWorldObjectComponent   PRESENT
```

Four absent types were the plan's foundation. The two present ones are why a partial workaround
looked tempting and was still wrong — `GetOrCreateComponent` would have compiled, but
`IDeclaresMayHaveComponents` is what keeps dynamically installed components alive across save/load,
so the compiling half without the absent half is a silent data-loss path.

The signal that was walked past, an hour earlier:

```bash
# deployed server's shipped sources -- zero hits
grep -rn "ComponentsToInstall" /path/to/deployed/Server/Mods/ | head
# (no output)

# source checkout -- eight hits, so research continued there
grep -rn "ComponentsToInstall" Server/Mods/__core__/Items/ | head
# Server/Mods/__core__/Items/TruckFlatbedAttachments.cs:15: ...
```

Read together these say *the trees are at different versions*. Read one after the other, they say
*keep looking until something turns up*.

## Related

- `docs/solutions/workflow-issues/validate-the-instrument-before-the-hypothesis.md` — the same family
  aimed at measurement rather than research: there a bisect's apparatus produced false results; here
  the research produced true results about the wrong artifact. Both fail by agreeing with you.
- `docs/solutions/conventions/document-the-path-you-actually-deploy-to.md` — the deploy-side twin.
  Same underlying question — is the thing I am acting on the thing that runs — asked of binaries
  rather than of APIs.
- `docs/solutions/conventions/requirecomponent-is-re-enforced-on-every-server-load.md` — another case
  where what the source *says* and what a running world *has* diverge silently.
- `docs/solutions/workflow-issues/a-remembered-capability-and-a-cited-file-are-claims.md` — where this
  check stops. There the remembered thing existed in the tree, ran, and produced the very classes in
  question; what was false was its output, so the presence probe would have returned PRESENT and
  confirmed the wrong belief.
