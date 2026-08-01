# Concepts

Shared domain vocabulary for this project — entities, named processes, and status concepts with
project-specific meaning. Seeded with core domain vocabulary, then accretes as ce-compound and
ce-compound-refresh process learnings; direct edits are fine. Glossary only, not a spec or
catch-all.

## Relationships

A Drone Dock owns its Survey Areas outright — there is no mod-wide registry of areas, so an area
exists exactly as long as the dock that created it does. Each Survey Area owns its own Findings
and Coverage; those belong to the area, not to the drone that gathered them and not to the dock's
current Assignment. A dock has at most one Assignment at a time, naming one of its own areas.

## Survey

### Survey Area
A named, dock-owned region of Plots that the player draws on the map for the drone to prospect.

Identity is a dock-local id, not the name — two areas may share a name and remain distinct.
Redrawing an area's geometry is treated as creating a different area: its Findings and Coverage are
discarded, because the old survey no longer describes the new shape. Renaming is not a redraw and
preserves them. Deleting an area destroys its Findings with it.

### Plot
Eco's property subdivision unit — a fixed square of world columns, the granularity at which land is
claimed. Survey Areas are drawn, capped, and swept in whole Plots, never in individual blocks.

### Finding
One material detected inside a Survey Area, carrying how much was seen, where, and how far below
the surface.

Findings persist with their area rather than with the drone or the dock's current Assignment, so
they stay readable while the drone is elsewhere or absent. An area with no Findings is ambiguous
until read together with Coverage — unsurveyed and surveyed-but-barren are different answers.

### Coverage
The fraction of a Survey Area the drone has actually swept. Distinguishes a survey that has not
started from one in progress from one that finished and found nothing.

### Survey Depth
How far below the surface a survey looked, in blocks. Bounds what a Finding-free result can be
taken to mean: nothing found says nothing about material deeper than this.

### Park-and-Sweep
The drone's traversal strategy: travel to a Plot, sweep that Plot's full column grid from there,
then move to the next.

Because each Plot is visited discretely, a Survey Area need not be contiguous, and a Plot the drone
cannot reach is skipped rather than stalling the survey.

## Client–server binding

### World Object
A placed, interactable object in the world, as opposed to an item carried in inventory. Each one is
defined twice: a server-side class that holds its behaviour and state, and a client-side prefab that
supplies its model and any in-world visuals.

Placing an item generally produces a *new* World Object rather than restoring the one that was
picked up, which is why state living on a World Object does not automatically survive relocation.
The exception is **component** state, and it is opt-in: when the item an object turns into declares
itself as carrying persistent data, being picked up sweeps every component's state onto that item
and placing it pours the state back. Nothing else transfers — fields on the object itself are still
lost — and an object spawned and destroyed outside the pickup path never transfers anything at all,
whatever its item declares.

Its **component set is fixed when it is created** and stored with it. Changing which components the
class declares affects only objects made afterwards, so two objects of the same class placed at
different times can carry different components — and in a world that has survived several builds,
"what components does this class have" has no single answer.

The client-side half is a **template**, not a per-object asset: the client holds one inactive copy
and instantiates an enabled clone for each World Object the server reports. A template shipped
active breaks that cloning, and the failure looks like a rendering glitch rather than a packaging
one — the object is listed and located correctly by the server while nothing is drawn, appearing
only once the area is re-loaded.

### Name Match
The rule that binds a World Object's two halves: the client prefab and the server class are linked
by having the **same name**, with nothing else connecting them.

A mismatch fails silently and in the worst direction — the server loads and behaves correctly while
the object renders as a missing-model placeholder, so the symptom appears purely visual and points
away from the cause. Renaming either half without the other, or letting a tool regenerate a prefab
under a different name, breaks the binding with no error at build or load time.

Which artifact carries the bound name differs by kind, and in neither case is it the image file. A
World Object binds through its prefab asset's own name; the scene object that prefab was built from
may be named differently without consequence. An item binds through the name of its object inside
the scene, not the name of the sprite supplying its icon. Naming the image correctly while the bound
artifact is named wrong fails exactly like any other mismatch, and is easier to miss because the
file that looks like the asset is the one that binds nothing.

## Dock and assignment

### Drone Dock
The placed world object that owns Survey Areas, holds the drone item, carries the player-facing
survey interface, and bears the drone's running costs.

The dock is the unit of persistence for the whole survey system — areas, findings, and assignment
live on it. Picking the dock up therefore discards them.

It is also the mod's only component host: fuel and cargo live on the dock, not the drone, because a
dock is an ordinary placed object with an item behind it and a drone is not. The player is told these
are the drone's stats.

Condition splits in two, and the halves behave differently. The dock's own parts degrade with use and
stay with the dock. A drone's condition rides the drone item itself, so it travels when the drone is
moved to another dock — a worn drone stays worn, and the dock it left keeps its own wear.

### Serviceable
A dock that can currently support work: it has fuel to burn and none of its parts are broken.

Serviceability gates dispatch rather than the drone itself, so an unserviceable dock is
distinguishable from an unassigned one — a player who is out of fuel must be told that, not shown an
area that looks like nobody asked for it.

### Recall
The drone's return to its dock because the dock stopped being Serviceable, as opposed to returning
because the survey finished or the area was unassigned.

A recall preserves the area's Coverage and Findings, so servicing the dock resumes the survey rather
than restarting it. The return leg itself is treated as always possible: a drone is never stranded by
the same shortage that recalled it.

### Assignment
The single Survey Area a dock has directed its drone to work on. Assigning is distinct from viewing:
any area's Findings can be read without dispatching the drone there.

Assignment is dock state, not drone state — it is transmitted to whichever drone is docked, so a
drone can be removed and replaced without the dock forgetting what it was working on. Editing the
assigned area's geometry restarts the survey as if it had been unassigned and reassigned.

### Material Target
A player-chosen filter over which materials the survey readout displays. It narrows what is shown,
never what is recorded — an empty selection shows everything found.

## Panel layout

### Row Budget
The visible height of a component tab, treated as the scarce resource the panel's design spends.
Roughly 605 px, or about 27 standard rows.

A tab renders one element per row in member declaration order, so length is the only axis a design
controls, and every element added is subtracted from every other. The unit is the standard
two-column row (22 px, label left and control right). Costs are quoted as multiples of it: a
`BigButton` is 3.2 rows, a `StringPlaque` is 5. Judging a control on whether it works misses what it
costs its neighbours.

### Control Pool
The fixed set of compile-time controls that stands in for a per-object list. A dynamically sized
list does not render from a mod component, so N controls means N members written out by hand, each
gated by a synced bool so unused positions do not occupy the Row Budget.

**Display-only.** An editable pool over a single shared field does not work: an interaction writes
every editable member back at once, so the field ends at whatever the last member says. A player
choosing one of N gets a single cursor instead, which also costs one row regardless of N. The pool
remains the right shape only where each control owns its own value.

Where a pool does apply, its size is a product decision rather than a technical ceiling — chosen to
fit a real workflow, with an overflow path (a chat command) for anything past it.
