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
under a different name, breaks the binding with no error at build or load time. The same applies to
items and their icon assets.

## Dock and assignment

### Drone Dock
The placed world object that owns Survey Areas, holds the drone item, and carries the player-facing
survey interface.

The dock is the unit of persistence for the whole survey system — areas, findings, and assignment
live on it. Picking the dock up therefore discards them.

### Assignment
The single Survey Area a dock has directed its drone to work on. Assigning is distinct from viewing:
any area's Findings can be read without dispatching the drone there.

Assignment is dock state, not drone state — it is transmitted to whichever drone is docked, so a
drone can be removed and replaced without the dock forgetting what it was working on. Editing the
assigned area's geometry restarts the survey as if it had been unassigned and reassigned.

### Material Target
A player-chosen filter over which materials the survey readout displays. It narrows what is shown,
never what is recorded — an empty selection shows everything found.
