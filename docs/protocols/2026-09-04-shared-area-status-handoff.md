# Handoff — shared area status

Written 2026-09-04, at the end of the session that implemented it. Everything below is either
verified or explicitly marked as unverified.

## State

- Branch `feat/tech-tree-icons`, **local only — no upstream, never pushed.** 77 commits ahead of
  `origin/main`; 26 of them are this work, the rest are the tech-tree icons and the farming drone.
- Build clean, 0 errors. 566 tests passing in `AdvancedElectronics.Navigation.Tests`.
- Working tree carries four files that are **not this work and must not be committed by anyone
  picking this up**: both `ProjectSettings/*.asset`, `docs/plans/2026-08-10-001-feat-tech-tree-icons-plan.md`
  (the icons session's), and the untracked `docs/plans/2026-08-22-0010-feat-farming-drones-plan.md`
  (the farming session's). They were dirty before this work began.

## What shipped

All thirteen implementation units of `docs/plans/2026-08-21-001-feat-shared-area-status-plan.md`,
which is `implementation-ready` and now also carries a `## Known Findings` section.

The ground record for a survey area — per-plot findings, mined stamps, at-bedrock observations,
refusal exclusions, kind, and a claim — now lives on the area rather than being split between
docks. One pure function derives a single lifecycle status from it; both tabs render that status
through one line builder. The dock network has a physical radius, overlapping areas are detected
and marked, and assignment takes a claim under one lock.

Two defects that predated this work were found and fixed while proving it: the sampled-block set
was shared across areas, so clearing one area kept blocks another area's plots covered; and
`ResetPlotsToUnsurveyed` did not run the findings-shape upgrade, so an old save hit by an outside
ground change would have frozen unattributable rows as current.

## Outstanding — decisions that are yours, not an agent's

Two review findings are product questions. Neither is a defect; both are the rules working as
written.

1. **Ownership-blind claim conflicts.** A stranger can draw a farming area across your mine and
   your mining dock is refused that ground. Because farmland's reservation outlives its assignment
   (R47), the block is permanent unless they delete their area. Recorded as F11.
2. **Access levels disagree between the two halves of the mod.** The survey tab's assign,
   unassign and map-edit actions run at consumer access; the farming half gates its equivalents at
   full access. Recorded as F8. Note the earlier ruling that a claim coordinates drones and does
   not control access still stands — this is about who may edit and assign areas, which is a
   different question.

## Outstanding — cannot be done without a server

Nothing here is a defect. These are checks that need a deployed server and were never runnable in
session.

- **The load line.** `grep "Loading AdvancedElectronics" <server>/Logs/<newest>.log`. Seven units
  added `[Serialized]` members and one **deleted** one. A serialized member without a setter, or a
  removed member an old save still carries, fails server init with a clean build and a completely
  silent log — no exception, nothing to search for but the missing line. This is the single
  highest-value check outstanding, and the deletion gives it a specific reason rather than a
  general one.
- **Two colours.** The `[empty]` grey (`#808080`) must be confirmed against the client's default
  panel text — R4 requires it, because that rung is the one that cannot be judged from its name.
  The `[farm]` orange (`#FF8C00`) wants the same look.
- **The four phase live passes** listed in the plan's Verification Contract.

## Outstanding — the review

A ten-lens review ran after every unit landed. **Fifteen actionable findings, none fixed** by
explicit decision. Full evidence and per-lens artifacts:
`docs/reviews/2026-09-04-shared-area-status/`.

Four bear on requirements the plan states, and each is a rule that reads correctly and does not
hold:

- **R47 does not protect real farmland.** The claim system builds projections from survey areas
  only; a farm a citizen assigns is a separate persisted type that never enters them. The
  reservation therefore applies only to a survey area someone retyped by command.
- **R44 and R45 deadlock.** A cleared area is not offered to a mining dock, but reassigning it to
  that dock is the only thing that lifts its refusal.
- **R16's reset races itself.** The engine fires its block-write notification from inside a
  parallel loop (`Server/Eco.World/World.cs:367`, invoking at `:388`), and the reset does an
  unsynchronised read-modify-write on the area's lists.
- **R17 and R43's attribution may not hold at all.** The marker identifying the mod's own writes
  is thread-scoped; the notification that reads it runs on that parallel loop. If the two do not
  share a thread, every drone unsurveys the ground it is working. **Unconfirmed, derived from two
  lenses rather than reported by one, and the first thing to settle live.**

Also outstanding: `CONCEPTS.md` documents two contradictory protection models for the same
`[farm]` tag — the asymmetric marker-driven one written here, and the symmetric geometry-only one
actually implemented. Reconcile when the farmland gap is fixed.

## Settled — do not re-open these

Decisions the product owner made in session. A fresh agent that re-derives them from the code
will get them wrong, because in several cases the code is what is wrong.

- **A claim coordinates drones; it does not control access.** Its lifetime is the assignment's, so
  releasing one is not taking anything. Two review findings were dropped on this ruling.
- **Changing an area's kind is explicit, not privileged.** "Owner" names who acts, not a permission
  tier. The only gate R32 defines is the drone-activity refusal.
- **`[farm]` occupies the exclusive status slot**, the same one the lifecycle statuses use — it is
  not an annotation. Both plan documents originally said the opposite.
- **A mining status and `[farm]` must never co-occur**, and the mechanism is structural: kind
  selects which status is derived at all, rather than the ladder running and being overwritten
  (R46).
- **Farmland is reserved by its mark, not its assignment** (R47), and the asymmetry runs one way:
  a farm may take exhausted mining ground, a mine may not take unreleased farmland.
- **Assignment is the retry** that lifts a refusal (R45). A survey drone cannot test law or
  property, so only a mining attempt can learn a refusal has gone, and the mod is deliberately
  never told when a plot becomes workable again.
- **Mining waits for the survey to finish** rather than working the ground alongside it. It is a
  refusal and not a queue because the scheduler belongs to the automation work.
- **An area holding any unsurveyed plot reads unsurveyed**, whatever its other plots say.
- **Existing saved findings are discarded on upgrade** rather than migrated, and the dock-side
  mined stamps go with them.
- **The dock-network radius is 60 metres**, its own constant, deliberately not the storage link's
  twenty.

## Traps that cost time here

- **A `[Serialized]` member with no setter fails server init silently.** Clean build, no
  exception, no stack trace — the only evidence is the absent load line. Every `[Serialized]`
  collection must be a `ThreadSafeList<T>` of flat primitives.
  See `docs/solutions/conventions/serialized-needs-a-member-to-write-back-into.md`.
- **`DroneDock` is a partial class across four files**, and another author's committed code
  reaches `HasFullAccess`, `StampedCitizenName` and `StampedCitizenId` through it nine times.
  Moving or renaming any of the three breaks a file you will not have open.
- **`AdvancedElectronics.Navigation` cannot see Eco types**, by design — that is why it is
  testable. Roughly half this work is Eco-side and has no automated coverage at all; every review
  finding touching it rests on reading rather than a failing test.
- **A worktree cannot build this repo.** `Local.props` carries `EcoRefAssembliesDir` and is
  git-ignored, so a fresh worktree resolves no reference assemblies.
- **`MiningAreaRef.Resolve` is not a pure query** — it carries a consecutive-failure counter, so
  calling it twice per tick halves the grace period it exists to provide.
- Git Bash and PowerShell take different syntax and the wrong one usually fails silently. Never
  pass a multi-line commit message with `-m`; use `-F -` and a quoted heredoc, and read it back
  with `%B` before pushing.

## If you pick this up

The plan is the authority; read its `Known Findings` section before its Implementation Units, or
you will implement against rules that three of the findings say do not hold.

The obvious next step is fixing the four requirement-level findings, in this order: the farmland
gap (a stated rule is inert), the reset race (silent data loss, confirmed against engine source),
the attribution question (settle whether it is real before building around it), then the
cleared/reassign deadlock. None has been started.
