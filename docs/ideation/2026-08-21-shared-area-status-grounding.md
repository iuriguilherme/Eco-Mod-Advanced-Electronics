# Grounding dossier — cross-dock comms & shared area status

Verbatim extraction only. Paths relative to repo root.

## 1. Area status / colour rendering

`EcoServerMod/AdvancedElectronics.Navigation/DockReadout.cs:58-70` — the entire markup vocabulary:
```csharp
public const string AssignedMarker    = "   <color=yellow>[assigned]</color>";
public const string UnreachableMarker = "   <color=red>[unreachable]</color>";
public const string MinedMarker       = "   <color=green>[mined]</color>";
private const string CompleteColor = "green";
public static string AsComplete(string text) => $"<color={CompleteColor}>{text}</color>";
```
`DockReadout.cs:72-73` — travel phrases: `DockedPhrase = "docked"`, `AtAreaPhrase = "at the area"`.
`DockReadout.cs:88-92` — `"cannot reach the area"`, `"returning to dock"`, `"flying to the area"`.

`DockReadout.cs:151-163` — three-state summary:
```csharp
if (top.Found) return $"{area.CoveragePercent:F0}% surveyed, most {top.OreType} (~{top.Count} blocks)";
return area.CoveragePercent > 0f ? $"{area.CoveragePercent:F0}% surveyed, nothing matching" : "not surveyed yet";
```
`DockReadout.cs:175-188` — colour applied only in the roster line:
```csharp
if (area.CoveragePercent >= 100f) summary = AsComplete(summary);
return $"{area.Position}. {area.Name} -- {area.PlotCount} plots, {summary}"
       + (area.IsAssigned ? AssignedMarker : string.Empty)
       + (area.IsUnreachable ? UnreachableMarker : string.Empty);
```
`DockReadout.cs:136-140` — `AtReadableSize` wraps blocks at `<size=125%>`.

Mining side, `EcoServerMod/AdvancedElectronics.Navigation/MiningReadout.cs:107-118`:
```csharp
var line = $"{position}. {dockName} -- {areaName} ({plotCount} plots)";
if (isMined) line = DockReadout.AsComplete(line);
if (isAssigned) line += DockReadout.AssignedMarker;
if (isMined) line += DockReadout.MinedMarker;
```
`MiningReadout.cs:97-99` doc: "the same vocabulary the survey tab's roster uses: yellow [assigned] for the one being worked, green [mined] for one with nothing left to do until it is re-surveyed."

Other status vocabularies (words, no colour): `MiningReadout.cs:51-58` job words (`"idle -- no area assigned"`, `"working"`, `"waiting to unload"`, `"complete -- finished, nothing was mineable"`, `"ended"`); `MiningReadout.cs:65-76` stop reasons incl. `"the source survey dock's area is gone"`, `"the source area was redrawn -- reassign it to mine the new shape"`; `MiningReadout.cs:185-193` skip labels (`unreachable`, `not authorized (property)`, `not authorized (settlement law)`, `obstructed`, `other`).

The only other `<color=` in the mod is a debug log: `EcoServerMod/AdvancedElectronics/DroneLifecycle.cs:587`.

Snapshot construction (filter applied before formatting) — `EcoServerMod/AdvancedElectronics/SurveyComponent.cs:449-465`:
```csharp
var top = area.ReadFindings().Where(f => f.Found && dock.IsMaterialShown(f.OreType))
    .OrderByDescending(f => f.Count).FirstOrDefault();
var isAssigned = area.Id == dock.AssignedSurveyAreaId;
var isUnreachable = isAssigned && DroneReportsUnreachable(dock);
return new AreaSnapshot(position, area.Name, area.PlotCount, area.CoveragePercent, top, isAssigned, isUnreachable);
```
Roster assembled at `SurveyComponent.cs:357-361`.
`AreaSnapshot` fields: `DockReadout.cs:11-44` (Position, Name, PlotCount, CoveragePercent, TopVisibleFinding, IsAssigned, IsUnreachable).

## 2. Survey findings storage & resurvey

Live, session-only accumulator — `EcoServerMod/AdvancedElectronics.Navigation/SurveyRecord.cs:40-52`:
```csharp
private readonly HashSet<BlockPos> _sampledBlocks = new HashSet<BlockPos>();
private readonly Dictionary<int, Dictionary<PlotCoord, PlotData>> _byArea = ...;
private readonly Dictionary<int, Dictionary<(int X, int Z), int>> _surfaceByArea = ...;
```
`SurveyRecord.cs:36-38`: "**Not persisted.** This record is session-scoped by design (R8/KTD3) ... The Eco side holds one of these on the dock and never serializes it."
`SurveyRecord.cs:71-93` — `RecordSample` is **accumulate**, idempotent per exact (x,y,z): `if (!_sampledBlocks.Add(new BlockPos(x, y, z))) return;` then `data.SampledCount++; ... data.RecordOre(...)`.
`SurveyRecord.cs:212-223` — the only clear path:
```csharp
public void ClearArea(int areaId) {
    _surfaceByArea.Remove(areaId);
    if (_byArea.Remove(areaId)) { _sampledBlocks.RemoveWhere(...); }
}
```

Persisted mirror — `EcoServerMod/AdvancedElectronics/SurveyAreaEntry.cs:146-156`, **replace not merge**:
```csharp
public void SetFindings(IEnumerable<SurveyFinding> findings, float coveragePercent, int surveyDepth, int medianSurface) {
    var snapshot = new ThreadSafeList<OreFindingSnapshot>();
    foreach (var f in findings.Where(f => f.Found)) snapshot.Add(OreFindingSnapshot.From(f));
    this.Findings = snapshot; ... }
```
`SurveyAreaEntry.cs:134-144` — a redraw clears: `SetPlots(...) { ... this.Epoch++; this.ClearFindings(); }`.
`SurveyAreaEntry.cs:158-166` — `ClearFindings()` also nulls `CoveragePercent`, `SurveyDepth`, `MedianSurface`, `SurveyedStamps`.
`SurveyAreaEntry.cs:88-93` doc: "available until the area is deleted or edited. Reassigning the drone away and back does NOT clear them — they belong to the area, not the drone or the dock's current assignment."
Projection guard — `EcoServerMod/AdvancedElectronics/DroneDock.cs:484-501`:
```csharp
var coverage = this.surveyRecord.Coverage(area);
if (coverage <= 0f) return; // no samples for this area yet — keep any persisted snapshot.
...
entry.SetFindings(this.surveyRecord.Findings(entry.Id), coverage * 100f, depth, median);
```
`DroneDock.cs:399-403` `ClearSurveyData(id)` (delete + edit); `DroneDock.cs:382-391` `DeleteSurveyArea`; `DroneDock.cs:425-427` `OnAreaEdited(int id)`.

Per-plot survey freshness stamp written during the sweep — `EcoServerMod/AdvancedElectronics/SurveyStrategy.cs:80`:
```csharp
this.homeDock.AssignedSurveyArea?.RecordSurveyedPlot(plot, (long)Eco.Simulation.Time.WorldTime.Seconds);
```

## 3. Mining completion

`EcoServerMod/AdvancedElectronics/DroneDock.Mining.cs:39-47`:
```csharp
/// This dock's own mined stamps (KTD12), flattened as (x, z, stamp) triples ...
/// Deliberately not cleared on reassignment: a plot already mined stays recorded mined ...
[Serialized] public ThreadSafeList<long> MinedStamps { get; set; } = new();
```
`DroneDock.Mining.cs:58-72` — `RecordMinedPlot(PlotCoord plot, long stampValue)` rehydrates, records, reflattens.
Freshness predicate — `EcoServerMod/AdvancedElectronics.Navigation/PlotFreshness.cs:59`:
```csharp
public static bool IsMineable(long surveyedStamp, long minedStamp) => surveyedStamp > minedStamp;
```
`PlotFreshness.cs:74-93` — `IsMinedOut(plots, surveyedStamp, minedStamp)`: `if (mined <= 0) return false; if (IsMineable(surveyedStamp(plot), mined)) return false;`
Per-dock verdict — `EcoServerMod/AdvancedElectronics/MiningComponent.cs:263-274`:
```csharp
if (dock.MiningJob?.Status == MiningJobStatus.Complete && dock.MiningJobAreaId == area.Id) return true;
```
with doc at `:256-262`: "Read per dock, not per area ... Two mining docks pointed at one survey area therefore answer this differently, and each is right about itself."

Depth / Y-level: `DroneDock.Mining.cs:206-227` — pass floor persisted as `[Serialized] private ThreadSafeList<int> passInProgress` with layout "`[0] plot X, [1] plot Z, [2] floor Y`", via `SavePassInProgress(PlotCoord plot, int floorY)` / `TryReadPassInProgress`. Plan side: `AdvancedElectronics.Navigation/ShaftPlan.cs:61-66` `public int? FloorY { get; }` — "The deepest Y this plan reaches ... This is the pass's floor"; `ShaftPlan.cs:14-25` `ShaftLayer.Depth` = "Depth below each column's own surface — 0 is the surface layer"; `ShaftPlan.cs:51` `public const int OpeningWidth = 3;`.

## 4. Cross-dock discovery today

`EcoServerMod/AdvancedElectronics/MiningComponent.cs:157-173` — the whole lookup, **no distance filter**:
```csharp
private IEnumerable<(DroneDockObject Dock, SurveyAreaEntry Area)> OfferedAreas()
{
    if (this.Parent is not DroneDockObject self) return Enumerable.Empty<(DroneDockObject, SurveyAreaEntry)>();

    return ServiceHolder<IWorldObjectManager>.Obj.All
        .OfType<DroneDockObject>()
        .Where(d => !d.IsDestroyed && d.HasComponent<SurveyComponent>() && SharesOwnerWith(self, d))
        .SelectMany(d => d.SurveyAreas.Select(a => (Dock: d, Area: a)));
}

private static bool SharesOwnerWith(DroneDockObject self, DroneDockObject other) =>
    ReferenceEquals(self, other) || Equals(self.Owners, other.Owners);
```
Doc `:141-155`: "same owner, holding at least one area (R2, R3, KD15) ... co-owned or company land still shares broadly, because Owners is a Title ... Access filtering (R39) still happens at assign time, where the acting player is known -- ownership answers 'whose docks are these', authorization answers 'may YOU use them', and both are needed."

Assign-time auth predicate — `EcoServerMod/AdvancedElectronics/DroneDock.Mining.cs:74-76`:
```csharp
public bool HasFullAccess(User citizen) =>
    citizen != null && ServiceHolder<IAuthManager>.Obj.IsAuthorized(this, citizen, AccessType.FullAccess, null, out _).Success;
```
Both-ends gate — `DroneDock.Mining.cs:116-140`: `if (!this.HasFullAccess(actingCitizen)) { refusalReason = "you need full access on this mining dock"; ... }` and `if (!sourceDock.HasFullAccess(actingCitizen)) { refusalReason = $"you need full access on '{sourceDock.Name}', the dock that surveyed this area"; ... }`.

Only distance filter anywhere is the chat-command dock picker — `EcoServerMod/AdvancedElectronics/DroneCommands.cs:471-490`: nearest-by-`Vector3.DistanceSquared(user.Position, dock.Position)` among docks passing `dock.IsAuthorized(user, AccessType.FullAccess)` (no radius cap).

## 5. Storage reach

Mod-side constant — `EcoServerMod/AdvancedElectronics/DroneDock.cs:156-157`:
```csharp
/// <summary>R26: the vanilla Store's own radius, not the engine's default of 9.</summary>
private const float LinkRadius = 20f;
```
Applied at `DroneDock.cs:550-555` (quoted in `docs/solutions/conventions/an-empty-marker-component-is-a-client-ui-feature-flag.md:310-316`):
```csharp
// Guarded: an NRE here aborts server startup entirely rather than degrading one dock.
if (this.TryGetComponent<LinkComponent>(out var link))
    link.Initialize(LinkRadius);
```
The radius symbol itself is the Eco reference assembly's `Eco.Gameplay.Components.LinkComponent.Initialize(float)`; the mod's link component derives from `SharedLinkComponent` (`EcoServerMod/AdvancedElectronics/DroneDockLink.cs:55-56`). Authorization filter named in `DroneDockLink.cs:41-46`: "Every read still goes through `LinkComponent.GetAuthorizedLinkedObjects`, which drops anything the querying alias lacks consumer access to". Vanilla default quoted at `DroneDockLink.cs:30-33`:
```
shouldLink = linkedObjDeed != null && linkedObjDeed == parentObjDeed
             && !compType.HasAttribute<DefaultToUnlinkedAttribute>();
```
No other link-range/reach constant exists in the repo (grep for `LinkRadius|LinkDistance|LinkRange|MaxLinkDistance|StorageLink` returns only the above and its doc).

## 6. Area identity

`EcoServerMod/AdvancedElectronics/MiningAreaRef.cs:17-22`:
```csharp
[Serialized]
public class MiningAreaRef {
    [Serialized] public Guid OwningDockId { get; set; }
    [Serialized] public int AreaId { get; set; }
    [Serialized] public int ObservedEpoch { get; set; }
```
`MiningAreaRef.cs:53-58` — `For(WorldObject owningDock, SurveyAreaEntry area)` uses `owningDock.ObjectID`.
`MiningAreaRef.cs:69-73` — resolution: `GetFromID(this.OwningDockId) as WorldObject` then `dock.SurveyAreas.FirstOrDefault(a => a.Id == this.AreaId)`.
`MiningAreaRef.cs:84-87` — change token: `$"{this.AreaId}:{this.ObservedEpoch}"`.
Area-side ids — `EcoServerMod/AdvancedElectronics/SurveyAreaEntry.cs:64-67`: `[Serialized] public int Id` ("Dock-local id, assigned by the owning dock. Stable across renames"), `[Serialized] public string Name` ("Not unique — two areas may share a name and stay distinct by Id"), `SurveyAreaEntry.cs:85` `[Serialized] public int Epoch`.
Dock-side tokens: `DroneDock.cs:417-418` `AssignedAreaToken => $"area:{this.AssignedSurveyAreaId}:{this.assignedAreaEpoch}"`; `DroneDock.Mining.cs:36-37` `AssignedMiningAreaToken => $"mining:{this.AssignedMiningArea.AreaId}:{this.miningAssignmentEpoch}"`.
No mod-wide area registry — `SurveyAreaEntry.cs:50-53`: "Owned by the `DroneDockObject` that created it (KTD9) — there is no mod-wide registry".

## 7. Landfill / mining arms — what a sibling would mirror

Item — `EcoServerMod/AdvancedElectronics/MiningDrone.cs:34-39`:
```csharp
[Serialized] [Weight(500)] [LocDisplayName("Mining Drone")]
[LocDescription("A craftable mining drone. Insert into a Drone Dock to pair it for dispatch.")]
[Ecopedia("Crafted Objects", "Advanced Electronics", true, true, null)]
public class MiningDroneItem : RepairableItem, IWorldObjectComponentSource, IPersistentData
```
Installed components — `MiningDrone.cs:103-122`:
```csharp
public IEnumerable<ComponentInstallation> ComponentsToInstall => new[] {
    ComponentInstallation.For<FuelSupplyComponent>(configure: c => c.Initialize(2, fuelTagList),
        canUninstall: c => c.Inventory.IsEmpty, proxyInteractions: false),
    ComponentInstallation.For<FuelConsumptionComponent>(configure: c => c.Initialize(FuelJoulesPerSecond), proxyInteractions: false),
    DroneCargo.Installation(),
    ComponentInstallation.For<MiningComponent>(proxyInteractions: false),
};
```
plus `MiningDrone.cs:69` `OriginalMaxDurability => 1000f`, `:75` `RepairItem => Item.Get<AdvancedCircuitItem>()`, `:80` `fuelTagList = { "Electric Fuel" }`, `:130` `FuelJoulesPerSecond = 75f`.

WorldObject — `MiningDrone.cs:183-205`:
```csharp
[Serialized]
[RequireComponent(typeof(DroneMoverComponent))]
[RequireComponent(typeof(DroneLifecycle))]
[Tag("Usable", Unset = true)]
public partial class MiningDroneObject : WorldObject, IDroneOwnable, IDroneToolbearer
{
    public DroneTool Tool => DroneTool.Mining;
    public DroneJobKind Job => DroneJobKind.Mining;
```
plus static ctor `AddOccupancy<MiningDroneObject>(... new BlockOccupancy(new Vector3i(0,0,0)))` (`:221-227`), `OwnerName`/`OwnerId` + `SetOwner(User)` (`:231-253`), and recipe `MiningDroneRecipe` (`:265-307`) ending `CraftingComponent.AddRecipe(tableType: typeof(RoboticAssemblyLineObject), recipeFamily: this);`.

Arm — `EcoServerMod/AdvancedElectronics/MiningArm.cs:41-58`:
```csharp
[Serialized]
[Category("Tool")]
[Tag("Excavation")]
[LocDisplayName("Mining Arm")]
[LocDescription("The mining drone's excavation tool. Never held or crafted.")]
public class MiningArmItem : Item { }
```
`MiningArm.cs:9-15` doc: same "Excavation" tag vanilla pickaxes declare, so `DigOrMine.ToolUsed` with `RequiredTag: "Excavation"` offers it in a law editor's tool picker. `MiningArm.cs:42-51` warns NOT to use `Category("Hidden")`.

## 8. Persistence

Representative `[Serialized]` class — `EcoServerMod/AdvancedElectronics/SurveyAreaEntry.cs:61-76, 114-117`:
```csharp
[Serialized]
public class SurveyAreaEntry {
    [Serialized] public int Id { get; set; }
    [Serialized] public string Name { get; set; }
    [Serialized] public ThreadSafeList<int> PlotCoords { get; set; } = new();
    ...
    [Serialized] public ThreadSafeList<long> SurveyedStamps { get; set; } = new();
    public SurveyAreaEntry() { }   // "Parameterless constructor required by the Eco serializer."
```
`SurveyAreaEntry.cs:70-75` records the constraint: "A `ThreadSafeList<T>`, not a plain `List`: Eco's serializer rejects a non-immutable `[Serialized]` member ('Attempting to serialize non-immutable member ... Either make immutable or add [ThreadSafe]') and fails server init." And `:55-59`: `Vector2i` is not `[Serialized]`, so plot sets are flattened int lists.

Live-accumulator + flat-snapshot pattern — `AdvancedElectronics.Navigation/PlotFreshness.cs:10-14`: "Mirrors the live-accumulator-plus-flat-snapshot pattern (docs/solutions/architecture-patterns/persist-derived-data-as-serialized-snapshot-on-its-owner.md): this is the live half; the Eco side projects `Snapshot()` onto a persisted flattened list and rehydrates it with `FromSnapshot`." Guard at `PlotFreshness.cs:34-35` (`IsEmpty`) and its use at `SurveyAreaEntry.cs:174-177`.

Job ledger persistence — `DroneDock.Mining.cs:236-290`: `[Serialized] public int MiningJobAreaId`, `[Serialized] private int miningJobStatusValue = -1; // -1 = no job yet`, `[Serialized] private int miningJobEndReasonValue = -1;`, `[Serialized] private ThreadSafeList<int> miningJobLedger = new();` with `PersistMiningJob()` / `RehydrateMiningJob()`.

`IPersistentData` example — `EcoServerMod/AdvancedElectronics/MiningDrone.cs:51-52`:
```csharp
[Serialized, SyncToView, NewTooltipChildren(CacheAs.Instance, flags: TTFlags.AllowNonControllerTypeForChildren)]
public object PersistentData { get; set; }
```
Explicitly-not-serialized fields that matter: `MiningAreaRef.cs:31` `private bool hasResolvedOnce;` and `:47` `private int consecutiveFailures;` (with `FailuresBeforeConfirmedGone = 20` at `:49`); `DroneDock.cs:410` `private int assignedAreaEpoch;`.
