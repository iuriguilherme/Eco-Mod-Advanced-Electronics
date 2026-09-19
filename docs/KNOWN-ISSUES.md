# Known issues carried past v0.4.0

Written 2026-09-19, when 0.4.0 was packaged. This is the development-facing list: what is known
to be wrong or unproven, where each item is described in full, and what closing it would take.
None of it is repeated in the zip's `README.txt`, which is written for server admins and players
and carries only the one caution a player can act on (do not overlap a farming area with a mining
area).

Each item names the requirement or finding ID it belongs to, so it can be matched against the
plan and review documents rather than re-derived.

## Rules that read correctly and do not hold

- **R47 — farmland is not protected from a mining claim.** The claim system builds its
  projections from survey areas only. A farm a citizen plants and assigns is a separate persisted
  type that never enters them, so the reservation R47 describes applies only to a survey area
  someone retyped to `[farm]` by command. **Player-visible effect:** a mining dock can take ground
  a real farm is using. **This is the one that reaches the shipped README**, as the instruction
  not to overlap the two area kinds. Full description:
  `docs/plans/2026-08-21-001-feat-shared-area-status-plan.md` (Known Findings).

- **R44 and R45 deadlock.** A `[cleared]` area is not offered to a mining dock (R44), but
  reassigning that area to the dock is the only thing that lifts the dock's refusal (R45).
  **Player-visible effect:** an area whose plots were all refused cannot be retried without
  editing or redrawing it. Full description: same Known Findings section.

- **F8 — access levels disagree between the two halves of the mod.** The survey tab's assign,
  unassign and map-edit actions run at consumer access; the farming half gates its equivalents at
  full access. Neither is wrong on its own; they are inconsistent with each other. This needs a
  product ruling before it needs code. Full description:
  `docs/protocols/2026-09-04-shared-area-status-handoff.md`.

- **F11 — claim conflicts are ownership-blind.** A stranger can draw a farming area across your
  mine and your mining dock is refused that ground. Because farmland's reservation outlives its
  assignment, the block is permanent unless they delete their area. Also a product ruling first.
  Full description: same handoff.

## Unproven rather than wrong

- **R17 and R43 — write attribution has never been confirmed on a live server.** The marker that
  identifies this mod's own ground writes is thread-scoped, and the engine fires its block-write
  notification from a parallel loop. If the two do not share a thread, the mod treats its own
  drones' work as someone else's. Since U1 that means the write is ignored rather than acted on,
  so the failure is a missed mark rather than a destroyed survey — but it is still unconfirmed.
  Settling it takes one live pass: `docs/protocols/2026-09-05-ground-change-attribution-handoff.md`
  lists the seven checks in the order to run them.

- **The 0.3.0 → 0.4.0 save fold has not been run against a real 0.3.0 save.** The fold itself is
  unit-tested (`LegacyMinedStampsTests`), and the dock half is in
  `EcoServerMod/AdvancedElectronics/DroneDock.Migration.cs`, but no world saved by 0.3.0 has been
  loaded on 0.4.0. The zip's `README.txt` still tells admins to remove their docks first.

- **Protocol rows T12 and T13 (icons) were never run**, and neither were the shared-area live
  passes (the two colour checks and the four phase passes in the plan's Verification Contract).
  0.4.0 ships on the load line — the server starts, the mod loads on Eco 0.14.1.1, 629 tests pass
  — and the rest is post-release verification.

## Documentation that contradicts itself

- **`CONCEPTS.md` describes two different `[farm]` protection models**: the asymmetric
  marker-driven one written down, and the symmetric geometry-only one actually implemented.
  Reconcile it when the R47 gap is fixed, not before — the right wording depends on which model
  wins.
