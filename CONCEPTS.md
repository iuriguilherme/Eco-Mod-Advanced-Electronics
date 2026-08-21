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

## Progression

### The Mechanics-to-Electronics Yardstick
The rule for how hard this mod's content should be to reach: **the jump from Industry to Advanced
Electronics should feel like the jump from Mechanics to Electronics.** One step up a tier, not a
wall.

This is a design intent, not something the code states, and nothing enforces it — it only shows up
in the accumulated choices about which table hosts a recipe, which skill gates it, and how much a
recipe consumes. It is written down because those choices are made one at a time and each looks
locally reasonable; the yardstick is what keeps them pointing the same way. Reach for it whenever
a recipe's cost, its skill level, or its bench is being picked.

Two consequences already in force:

- **Tier does not buy a stronger upgrade module.** Every vanilla specialty upgrade — Industry,
  Composites, Advanced Masonry, Electronics, and the tier 5 Cutting Edge Cooking — declares the
  same pair of bonuses. The Advanced Electronics Upgrade matches them rather than scaling with its
  own tier.
- **The bench is part of the cost.** Moving the drone and dock recipes to the Robotic Assembly Line
  raised their real cost more than any ingredient change would: that table is Industry-gated and
  draws 6000 W. A relocation is a progression decision.

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

Its **component set is re-validated on every server load**, not fixed at creation. The engine walks
every placed object as the world loads and converges its components on what the class currently
declares — adding ones that are required and missing, and removing ones no longer required. So a
declaration change reaches objects already in the world, and it reaches them in both directions.

The removing direction is destructive: a component dropped this way takes its contents with it, so
narrowing what a class declares can delete player inventory at the next restart rather than merely
changing what new objects get. A class can opt a component out of that sweep, which is how a
mod keeps a tab whose contents must survive across a declaration change.

One case escapes that destruction: replacing a component with a **base or derived type of itself**.
The engine recognises the two as the same lineage, keeps whichever survives the sweep, and hands the
displaced one's state to it before dropping it — the only reconciliation path that announces itself
in the server log. How much actually carries over is the component's own decision, so a lineage swap
is safer than an outright removal without being free. A swap to an unrelated type gets no such
treatment and is a removal plus an addition.

The client-side half is a **template**, not a per-object asset: the client holds one inactive copy
and instantiates an enabled clone for each World Object the server reports. A template shipped
active breaks that cloning, and the failure looks like a rendering glitch rather than a packaging
one — the object is listed and located correctly by the server while nothing is drawn, appearing
only once the area is re-loaded. That symptom is shared with an [[Unrendered Object]] and the two
are worth telling apart: a bad template recovers on reload, a failed view never does.

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

Skills, skill books, skill scrolls and research papers are items for this purpose — a skill derives from item, and item is what declares an icon — so all four bind by class name through one mechanism rather than four. Nothing has to be declared to opt in: the bound name is the class's own name, and the icon attributes an item can carry govern redirection and opting *out*.

### Unrendered Object
A placed object the server has, ticks, and reports correctly, but which the client cannot build — so
it has no model, cannot be interacted with, and cannot be targeted by admin tools.

Distinct from a [[Name Match]] failure, which draws a placeholder: an unrendered object draws
nothing at all. Its floating name label and map marker still appear, which is the tell — the server
knows exactly where it is. The server log stays clean, because nothing failed on the server.

The consequence that makes this worse than a visual bug: the tool that exists to remove
unremovable objects also cannot see it, so there is no in-game recovery. It is caused by declaration
shape rather than logic — a component tab the client has no view for, or a component deriving a
client-drawn base the client cannot resolve — which is why it appears the moment an object is
placed, on every instance, rather than intermittently.

### Capability Flag
An engine component with an empty body whose only job is to be present. It has no state, no logic
and no server behaviour; the client reads the object's component list, sees the type, and enables an
optional part of a tab it was already drawing.

The tell is the declaration shape — `{}`, `[ForceCreateView]` so it reaches the client despite
syncing nothing, usually `[NoIcon]`. `InOutLinkedInventoriesComponent` is the worked example: without
it the Storage tab lists linkable inventories but draws no per-target Take From / Put Into controls,
and the engine's own comment on the type says it "works like a flag".

Two consequences for a [[World Object]] declared here. First, the flag is almost never in the vanilla
object's own attribute block — `[RequireComponent]` is recursive, so the effective component set is a
transitive closure and a capability usually arrives through some other component's requirements.
Comparing two objects' visible attribute lists compares the wrong thing. Second, adding one to a live
object changes its component set, which is a [[Panel Rebuild]] for everyone with that window open.

Distinct from an [[Unrendered Object]], where a declaration stops the client drawing anything: here
the surface renders correctly and only an affordance is absent, with no error on either side.

### Animated State
A named value the server pushes to a placed object's client half so the client can react to what the
object is doing — the only channel by which server behaviour reaches an animation or a rendered
readout.

It is a signal, never saved state: the value is recomputed from live status and pushed on change, so
persisting it would let a save file contradict the code that derives it.

Its name is the entire binding, and the client does the wiring itself: on building an object it
walks that object's animation parameters and connects each one to the state of the same name. There
is nothing to configure — no component to write, no event to hook, and no possibility of one — the client cannot load mod code
at all, so the name binding is not a convenience but the only channel there is — and consequently nothing that
reports a name present on one side and absent on the other. This is [[Name Match]]'s failure shape
one level down: the object renders correctly and simply never moves, with a clean build and a clean
server log.

A small set of names is reserved, because the engine already publishes those states itself; reusing
one is the rare loud failure in this area, and it is loud only in the *client* log. Contrast
[[Persisted State]], which is what a value must be to survive a reload.

### Position Authority
The rule that exactly one writer sets a placed object's transform: the server, which assigns the
position and pushes it to clients. Everything on the client side animates the model *within* that
transform and must never move the transform itself.

The rule exists because nothing enforces it. A client-side animation that displaces its own root is
a second writer, and by the general rule that the last write to a transform in a frame is the one
that renders, what a player sees is the interleaving of both rather than the output of either. The resulting error is not constant, which is what
makes it hard to name: while the server is actively moving the object it re-asserts its position
every tick and mostly wins, but wherever the server deliberately holds still the other writer
accumulates unopposed. The object therefore drifts most exactly where it is supposed to be most
stable, and the symptom reads as a wrong resting height rather than a contested one. The
recognisable tell is a resting-position constant that improves every time it is tuned and never
converges. Contrast [[Animated State]], which is the sanctioned server-to-client channel and carries
signals the client reacts to — never position.

### Persisted State
A value written to disk when the world saves and restored when it loads, as opposed to one recomputed
on demand.

Restoring is a write, so only a member the engine can assign into can carry it — a property that
derives its value on every read has nothing to receive the saved value, and marking one as persisted
prevents the mod from loading at all. The failure produces no log line, because registration fails
before the mod is far enough along for errors to be attributed to it; the tell is the absence of the
mod's own load line rather than the presence of an error. The distinction from [[Animated State]] is
the useful test: if a value can be recomputed from something already known, it is a signal and
persisting it creates a second source of truth.

## Dock and assignment

### Drone Dock
The placed world object that owns Survey Areas, holds the drone item, carries the player-facing
survey interface, and bears the drone's running costs.

The dock is the unit of persistence for the whole survey system — areas, findings, and assignment
live on it. Picking the dock up therefore discards them.

It is also the mod's only component host: fuel and cargo live on the dock, not the drone, because a
dock is an ordinary placed object with an item behind it and a drone is not. The player is told these
are the drone's stats. The drone is a [[Module]] of the dock, and the dock has only the capabilities
its current drone lends it.

Condition splits in two, and the halves behave differently. The dock's own parts degrade with use and
stay with the dock. A drone's condition rides the drone item itself, so it travels when the drone is
moved to another dock — a worn drone stays worn, and the dock it left keeps its own wear.

### Module
An item that, while slotted into a host object, lends that host a set of components it would not
otherwise have — and takes them away again when removed.

The host owns none of it. What the host can do depends entirely on what is slotted into it, so two
otherwise identical hosts differ by their module. Component state survives the move: a module pulled
from one host and put into another carries its contents and condition with it, rather than being
reset.

Installing and uninstalling are not free and not invisible: each one changes the host's component set
and so triggers a [[Panel Rebuild]] for anyone with the host's window open. A module system therefore
has to be certain the set has genuinely changed before acting on it.

A module is the mod's answer to a host that cannot itself be the thing players interact with. It
lets the player be told the capability belongs to the module, while the engine sees a host that
borrowed it.

Not to be confused with the base game's **upgrade modules**, which slot into crafting tables to
grant crafting bonuses. Those are a separate mechanism that happens to share the word: an upgrade
module changes what a table *costs*, never what it *can do*, and a table admits one by matching the
module's own slot tag rather than by naming it. When both senses are in play, say "upgrade module"
for the base-game kind and leave "module" for this one.

### Electric Fuel
The mod's own fuel class, and the only one a dock accepts. Nothing in the base game carries it —
the mod defines the tag and the Battery is its sole holder, so a dock runs on fuel this mod makes
or it does not run.

The exclusivity is the point: the drone's operating cost belongs to the mod rather than to a
commodity from another tech branch, which is what makes it something the mod can tune or gate.

Switching a world onto it is gentler than it sounds, because a fuel class filters what may be
*added* to a tank and nothing else. A dock holding fuel of the older kind keeps burning it until it
is spent, then empties itself and asks for the new kind; the old fuel can also be taken out by hand
at any point. Nothing is stranded and nothing is destroyed.

### Serviceable
A dock that can currently support work: it has fuel to burn and none of its parts are broken.

Serviceability gates dispatch rather than the drone itself, so an unserviceable dock is
distinguishable from an unassigned one — a player who is out of fuel must be told that, not shown an
area that looks like nobody asked for it.

### Drone Status
Where a dispatched drone is in its round trip: waiting at the dock, travelling to the area, working
on station, or unable to get where it was sent.

The statuses are not merely a readout — they are what decides the drone's next move each tick, so a
status the drone cannot leave is a drone that stops working forever. The unable-to-reach status is
the one that matters: it is entered both by a journey that failed part-way and by a departure that
never began, and a drone in it may be standing anywhere, including on its own dock. Every escape
from it must therefore be testable without assuming travel is under way, or the drone that most
needs the escape is the one that cannot take it.

Statuses are drone state, unlike [[Assignment]], which is the dock's. Clearing the assignment does
not by itself move a stuck drone, because the status machine advances on its own each tick rather
than being driven by the assignment.

### Recall
The drone's return to its dock because the dock stopped being Serviceable, as opposed to returning
because the survey finished or the area was unassigned.

A recall preserves the area's Coverage and Findings, so servicing the dock resumes the survey rather
than restarting it. The return leg itself is treated as always possible: a drone is never stranded by
the same shortage that recalled it. That guarantee is progressive, and its last resorts abandon the
[[Cruise Profile]] entirely — a return that cannot be flown is eventually simply performed.

That guarantee covers the *movement* of getting home; it does not cover reaching the point where the
return is attempted. A drone whose [[Drone Status]] never transitions toward home is stranded just as
surely, and no amount of movement fallback rescues it, because the fallback is never invoked.

### Cruise Profile
The shape a drone's flight takes whenever it is actually flown: rise in place, cross level,
descend in place onto the destination. The level leg is flown high enough to clear the highest ground anywhere on the route,
so obstacles between the two ends are passed over rather than followed.

Climbing and descending are their own legs rather than something blended into forward travel. A
drone that gained height while moving forward would cut into rising ground, and one that took its
height from whatever it happened to be flying over would dive into every hollow on the way —
neither is what a hovering machine should look like, and the second is slow as well as absurd.

Only the two ends sit at ground level; the drone still lands where it was sent, including at the
bottom of a shaft. This is what lets a drone work terrain it has itself reshaped: the route is
computed over the excavation rather than through it.

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

### Panel Rebuild
The teardown and reconstruction of a host's whole window that follows any change to which components
the host carries. The set of tabs is derived from the set of components, so changing the set
invalidates the window rather than updating it.

The cost falls on whoever has that window open. A rebuild discards the live controls and builds new
ones, and a control discarded while it still holds a subscription to a synced value is not replaced —
it is simply absent for the rest of that viewer's session, with no error raised anywhere on the
server. Controls therefore disappear one at a time, silently, until a tab is unusable and only
reconnecting restores it.

This makes a [[Module]]'s install and uninstall a UI event as much as a capability change, and makes
any guard deciding "does the installed set still match what is slotted?" load-bearing: a guard that
fires more often than the set genuinely changes will empty the panels of every player watching.
Prefer a stable set whose members disable themselves over a set that installs and uninstalls.

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
