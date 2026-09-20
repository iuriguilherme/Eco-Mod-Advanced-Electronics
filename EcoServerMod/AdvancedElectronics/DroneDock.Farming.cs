using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedElectronics.Navigation;
using Eco.Core.Utils;
using Eco.Gameplay.Components;
using Eco.Gameplay.Items;
using Eco.Gameplay.Players;
using Eco.Shared.Serialization;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// One dock-owned farm area (U9, R5, R17, R24). The same shape
    /// <see cref="SurveyAreaEntry"/> uses -- a dock-local id, a player-facing name, and
    /// plots flattened to (x, z) pairs because Eco's <c>Vector2i</c> is not
    /// <c>[Serialized]</c> -- plus the two settings that are the farm's own: which crop it
    /// grows and whether to level it first.
    /// </summary>
    /// <remarks>
    /// Farming records nothing per plot (R7). The drone decides from the ground each time,
    /// so unlike a survey area there are no findings, no coverage and no stamps here: a
    /// stored belief about a plot would have no reader and could only go stale.
    /// </remarks>
    [Serialized]
    public class FarmAreaEntry
    {
        [Serialized] public int Id { get; set; }

        [Serialized] public string Name { get; set; }

        /// <summary>Drawn plots, flattened as consecutive (x, z) pairs. Even length by construction.</summary>
        [Serialized] public ThreadSafeList<int> PlotCoords { get; set; } = new();

        /// <summary>Bumped on every redraw, so a job built against the old shape can tell.</summary>
        [Serialized] public int Epoch { get; set; }

        /// <summary>
        /// The one crop this area grows (R24), as the engine's own species name. Null until
        /// a citizen picks one, which is the state R26 reports as awaiting a crop and works
        /// not at all.
        /// </summary>
        [Serialized] public string Crop { get; set; }

        /// <summary>The level-first toggle of R17, off by default and cleared when the pass completes (R21).</summary>
        [Serialized] public bool LevelFirst { get; set; }

        /// <summary>
        /// Whether the drone works this area.
        /// </summary>
        /// <remarks>
        /// A local flag standing in for the sibling plan's multi-area assignment, which
        /// this plan consumes rather than defines and which has not landed. When it does,
        /// this field is what it replaces -- the shared model owns which areas a dock
        /// holds and how a claim attaches, and nothing here should be reimplementing it.
        /// </remarks>
        [Serialized] public bool Assigned { get; set; }

        // ---------------------------------------------------------------
        // The area's last reported state, written by the drone's strategy and read by the
        // Farming tab. Persisted so the tab still answers "why is nothing happening" after
        // a restart, before the drone has visited the area again -- a blank tab is the one
        // answer that helps nobody.
        //
        // Deliberately NOT a belief about the ground (R7): none of it decides an action.
        // The drone reads each block fresh every visit; this is only what it reported the
        // last time it did.
        // ---------------------------------------------------------------

        /// <summary>Last reported stall as a <see cref="FarmStallReason"/> ordinal, or -1 for none.</summary>
        [Serialized] public int LastStallReason { get; set; } = -1;

        /// <summary>Last reported next action as a <see cref="FarmAction"/> ordinal, or -1 for none.</summary>
        [Serialized] public int LastNextAction { get; set; } = -1;

        /// <summary>Hours until this area's least-grown plant comes due (R31); negative for none. Display only.</summary>
        [Serialized] public double LastNextDueHours { get; set; } = -1;

        /// <summary>
        /// When that plant comes due, as an absolute world time. The scheduling value:
        /// a stored DURATION re-anchored to the clock on every settle walks the wake
        /// forward each time anyone touches a chest, and the crop never gets picked.
        /// </summary>
        [Serialized] public double LastDueAtWorldSeconds { get; set; }

        /// <summary>Plots held by an overlap with another dock's area (R37).</summary>
        [Serialized] public int LastHeldPlotCount { get; set; }

        /// <summary>The engine's own word for why the ground refused the crop (R38).</summary>
        [Serialized] public string LastUnfitCondition { get; set; }

        /// <summary>What linked storage was short of (R28).</summary>
        [Serialized] public string LastMissingMaterial { get; set; }

        /// <summary>
        /// Whether the surface is flat enough to farm by hand (R8). Re-derived from the
        /// ground rather than stored as an achievement (R9), so this is the last derivation
        /// rather than a claim that survives the ground changing.
        /// </summary>
        [Serialized] public bool LastFlat { get; set; }

        // ---------------------------------------------------------------
        // The level pass in progress (U12). Held on the area rather than on the driver,
        // because the driver is a live object rebuilt from scratch on every dispatch while a
        // half-levelled hillside is exactly the state that must outlive one.
        //
        // Only the target and the banked spoil are kept, never the plan. The plan is
        // re-derived from the ground each dispatch, which is what lets a pass resume against
        // what it has actually dug -- but the TARGET must not be, or re-deriving the median
        // from the half-levelled surface would move it under the pass and it would never
        // converge (R19).
        // ---------------------------------------------------------------

        /// <summary>Whether a level pass is under way. Distinct from the toggle: the toggle is the request, this is the run.</summary>
        [Serialized] public bool LevelPassStarted { get; set; }

        /// <summary>The height the pass is levelling to, pinned at pass entry.</summary>
        [Serialized] public int LevelTargetHeight { get; set; }

        /// <summary>Blocks this pass has removed and still counts as its own material, wherever they now sit (R20).</summary>
        [Serialized] public int LevelBankedSpoil { get; set; }

        /// <summary>Ends the pass and forgets its state, whether it completed or was abandoned.</summary>
        public void ClearLevelPass()
        {
            this.LevelPassStarted = false;
            this.LevelTargetHeight = 0;
            this.LevelBankedSpoil = 0;
        }

        /// <summary>Forgets the last report, so an area no longer worked stops asserting a stale reason.</summary>
        public void ClearLastReport()
        {
            this.LastStallReason = -1;
            this.LastNextAction = -1;
            this.LastNextDueHours = -1;
            this.LastDueAtWorldSeconds = 0;
            this.LastHeldPlotCount = 0;
            this.LastUnfitCondition = null;
            this.LastMissingMaterial = null;
        }

        public FarmAreaEntry() { }

        public FarmAreaEntry(int id, string name, IEnumerable<PlotCoord> plots)
        {
            this.Id = id;
            this.Name = name;
            this.SetPlots(plots);
        }

        public int PlotCount => this.PlotCoords.Count / 2;

        /// <summary>
        /// Replaces the stored plots. Unlike a survey redraw this discards nothing else: a
        /// redraw re-scopes which blocks the area covers and keeps its crop, its toggle and
        /// its markers (R12), because none of them describe the geometry.
        /// </summary>
        public void SetPlots(IEnumerable<PlotCoord> plots)
        {
            this.PlotCoords = new ThreadSafeList<int>();
            foreach (var p in plots)
            {
                this.PlotCoords.Add(p.X);
                this.PlotCoords.Add(p.Z);
            }
            this.Epoch++;
        }

        public IEnumerable<PlotCoord> Plots()
        {
            for (var i = 0; i + 1 < this.PlotCoords.Count; i += 2)
                yield return new PlotCoord(this.PlotCoords[i], this.PlotCoords[i + 1]);
        }

        /// <summary>Projects into the Eco-free area shape for membership and cap logic.</summary>
        public SurveyArea ToArea() => new SurveyArea(this.Id, this.Name, this.Plots());
    }

    /// <summary>One crop's harvest ceiling as the dock persists it (R25).</summary>
    [Serialized]
    public class CropCeilingEntry
    {
        [Serialized] public string Crop { get; set; }

        /// <summary>The quantity at or above which harvesting stops. Never stored as zero -- zero is how a citizen removes a ceiling, and a removed ceiling has no row.</summary>
        [Serialized] public int Ceiling { get; set; }

        public CropCeilingEntry() { }

        public CropCeilingEntry(string crop, int ceiling)
        {
            this.Crop = crop;
            this.Ceiling = ceiling;
        }
    }

    /// <summary>One area as the Farming tab renders it: the entry itself, the pure state the readout formats, and whether the ground is flat.</summary>
    public readonly struct FarmAreaReadout
    {
        public FarmAreaEntry Entry { get; }
        public FarmAreaState State { get; }
        public bool IsFlat { get; }

        public FarmAreaReadout(FarmAreaEntry entry, FarmAreaState state, bool isFlat)
        {
            this.Entry = entry;
            this.State = state;
            this.IsFlat = isFlat;
        }
    }

    // Farming dock state (U9): the areas this dock farms, each area's crop and level-first
    // toggle, and the per-crop harvest ceilings. Split from DroneDock.cs for the same
    // reason the mining partial was -- that file is already past a thousand lines, and the
    // farming surface reads on its own.
    public partial class DroneDockObject
    {
        /// <summary>Every farm area this dock owns. Serialized; survives a restart with the dock.</summary>
        [Serialized] public ThreadSafeList<FarmAreaEntry> FarmAreas { get; private set; } = new();

        // Monotonic and separate from the survey side's counter: farm and survey areas are
        // different collections, and a shared counter would make a farm area's id depend on
        // how many survey areas the dock happens to have drawn.
        [Serialized] private int nextFarmAreaId = 1;

        /// <summary>
        /// The crops a citizen has capped (R25). Only capped crops occupy a row: every other
        /// crop in the game is harvested without limit, which is the default they all start
        /// at rather than an unconfigured state to report (R26).
        /// </summary>
        [Serialized] public ThreadSafeList<CropCeilingEntry> CropCeilings { get; private set; } = new();

        // Bumped on every assignment change or redraw of an assigned area, so the
        // lifecycle's change-detection token changes and a fresh job starts rather than a
        // stale one resuming. In-memory for the same reason the survey side's is: a restart
        // re-dispatches regardless.
        private int farmAssignmentEpoch;

        /// <summary>The areas the drone currently farms.</summary>
        public IEnumerable<FarmAreaEntry> AssignedFarmAreas => this.FarmAreas.Where(a => a.Assigned);

        /// <summary>
        /// Change-detection token across every assigned area, or null when none is
        /// assigned. Folds in each area's own epoch, so redrawing one of several assigned
        /// areas re-dispatches exactly as unassigning and reassigning it would.
        /// </summary>
        public string AssignedFarmAreasToken
        {
            get
            {
                var assigned = this.AssignedFarmAreas.OrderBy(a => a.Id).ToList();
                if (assigned.Count == 0) return null;

                var parts = assigned.Select(a => $"{a.Id}.{a.Epoch}");
                return $"farm:{string.Join(",", parts)}:{this.farmAssignmentEpoch}";
            }
        }

        public FarmAreaEntry FarmArea(int id) => this.FarmAreas.FirstOrDefault(a => a.Id == id);

        /// <summary>Creates a farm area from already-validated plots (the picker enforces the cap before calling).</summary>
        public FarmAreaEntry CreateFarmArea(string name, IEnumerable<PlotCoord> plots)
        {
            var entry = new FarmAreaEntry(
                this.nextFarmAreaId++, string.IsNullOrWhiteSpace(name) ? "Farm Area" : name, plots);
            this.FarmAreas.Add(entry);
            return entry;
        }

        /// <summary>Renames an area. A rename is not a redraw, so it keeps everything else (R12).</summary>
        public void RenameFarmArea(int id, string name)
        {
            var entry = this.FarmArea(id);
            if (entry != null && !string.IsNullOrWhiteSpace(name))
                entry.Name = name;
        }

        /// <summary>Deletes an area and its configuration with it.</summary>
        public void DeleteFarmArea(int id)
        {
            var entry = this.FarmArea(id);
            if (entry == null) return;

            this.FarmAreas.Remove(entry);
            this.farmAssignmentEpoch++;
        }

        /// <summary>
        /// Called after an area's plots are redrawn. The crop, the toggle and the markers
        /// all survive (R12) -- only the geometry changed -- but an assigned area's redraw
        /// re-dispatches the drone, because the block list it was working no longer
        /// describes the area.
        /// </summary>
        public void OnFarmAreaEdited(int id)
        {
            if (this.FarmArea(id)?.Assigned == true)
                this.farmAssignmentEpoch++;
        }

        /// <summary>
        /// Sets the crop this area grows (R24). The choice replaces the previous one rather
        /// than adding to it, and belongs to this area alone -- selecting another area shows
        /// that area's own crop.
        /// </summary>
        public bool SetFarmAreaCrop(int id, string cropKey, User actingCitizen = null)
        {
            var entry = this.FarmArea(id);
            if (entry == null) return false;

            // Same gate as assignment: choosing the crop decides what the drone plants on
            // this ground under the stamped citizen's name, so it is an owner's decision.
            if (!this.HasFullAccess(actingCitizen)) return false;

            entry.Crop = string.IsNullOrWhiteSpace(cropKey) ? null : cropKey;
            if (entry.Assigned) this.farmAssignmentEpoch++;
            return true;
        }

        /// <summary>
        /// Sets an area's level-first toggle (R17), or clears it when its pass completes
        /// (R21).
        ///
        /// Turning it ON requires full access, because a level pass is destructive: it
        /// removes standing ground across the whole area and back-fills from the owner's
        /// storage, and none of that is undoable. Turning it off, and the pass clearing it
        /// on completion, need no citizen -- stopping is always allowed.
        /// </summary>
        public bool SetFarmAreaLevelFirst(int id, bool levelFirst, User actingCitizen = null)
        {
            var entry = this.FarmArea(id);
            if (entry == null) return false;

            if (levelFirst && !this.HasFullAccess(actingCitizen)) return false;

            entry.LevelFirst = levelFirst;
            if (entry.Assigned) this.farmAssignmentEpoch++;
            return true;
        }

        /// <summary>
        /// Assigns or unassigns one farm area, stamping <paramref name="actingCitizen"/> as
        /// the party accountable for everything the drone then does there (R6).
        ///
        /// Refuses the whole call when the citizen lacks full access on this dock: the
        /// farming actions write to the world in that citizen's name, so anything less
        /// would let a passer-by point someone else's drone at ground they cannot touch.
        /// </summary>
        public bool AssignFarmArea(int id, bool assigned, User actingCitizen, out string refusalReason)
        {
            refusalReason = null;

            var entry = this.FarmArea(id);
            if (entry == null)
            {
                refusalReason = "that area no longer exists";
                return false;
            }

            if (actingCitizen == null)
            {
                refusalReason = "no acting citizen";
                return false;
            }

            // Both directions are gated. Unassigning used to ignore the acting citizen
            // entirely, so any passer-by could halt an owner's automation.
            if (!this.HasFullAccess(actingCitizen))
            {
                refusalReason = "you need full access on this drone dock";
                return false;
            }

            if (assigned) this.StampCitizen(actingCitizen);

            entry.Assigned = assigned;
            this.farmAssignmentEpoch++;
            return true;
        }

        /// <summary>
        /// This dock's ceilings as the Eco-free ledger the block decision consumes (U2).
        /// Rebuilt per read rather than cached: the list is at most a handful of rows, and
        /// a cache would be one more thing to invalidate when the tab writes.
        /// </summary>
        public CropCeilingLedger ReadCropCeilings()
        {
            var ledger = new CropCeilingLedger();
            foreach (var entry in this.CropCeilings)
                if (!string.IsNullOrEmpty(entry.Crop) && entry.Ceiling > 0)
                    ledger.Set(entry.Crop, entry.Ceiling);
            return ledger;
        }

        /// <summary>
        /// Sets a crop's harvest ceiling. Zero removes it (R25), so the row disappears
        /// rather than being stored as a limit of nothing -- which is what keeps the
        /// ceilings grid's untouched default from stopping every crop in the game.
        /// </summary>
        public bool SetCropCeiling(string crop, int ceiling, User actingCitizen = null)
        {
            if (!this.HasFullAccess(actingCitizen)) return false;
            return this.WriteCropCeiling(crop, ceiling);
        }

        /// <summary>
        /// Writes a crop's ceiling without an access check of its own, for the Crop Ceilings
        /// tab's per-crop rows. Each row is an editable property, and Eco runs a property's
        /// write as an RPC that has already enforced the row's declared full access before the
        /// setter is reached -- but it passes the setter no citizen, so the check
        /// <see cref="SetCropCeiling"/> makes cannot be made there. Nothing else may call this.
        /// </summary>
        internal bool WriteCropCeilingFromTab(string crop, int ceiling) => this.WriteCropCeiling(crop, ceiling);

        private bool WriteCropCeiling(string crop, int ceiling)
        {
            if (string.IsNullOrWhiteSpace(crop)) return false;
            if (ceiling < 0) ceiling = 0;

            var existing = this.CropCeilings.FirstOrDefault(c => string.Equals(c.Crop, crop, StringComparison.Ordinal));

            if (ceiling == 0)
            {
                if (existing != null) this.CropCeilings.Remove(existing);
                return true;
            }

            if (existing != null) existing.Ceiling = ceiling;
            else this.CropCeilings.Add(new CropCeilingEntry(crop, ceiling));
            return true;
        }

        /// <summary>
        /// How much of <paramref name="cropKey"/>'s produce sits in this dock's linked
        /// storage (R27). Counted through the stamped citizen's own alias, so the tally
        /// covers exactly the containers the drone could actually unload into -- a ceiling
        /// measured against storage the drone cannot reach would stop a harvest for a
        /// surplus that is not there.
        ///
        /// Other drone docks are excluded for the reason the unload path excludes them: a
        /// dock's cargo hold is an ordinary storage component, and counting one drone's
        /// undelivered load as settlement stock would hold the harvest for produce still
        /// in transit.
        /// </summary>
        public int CountInLinkedStorage(string cropKey)
        {
            var crop = CropCatalog.ByKey(cropKey);
            if (crop?.Produce == null) return 0;

            var citizen = this.StampedCitizen;
            if (citizen == null) return 0;

            if (!this.TryGetComponent<LinkComponent>(out var link)) return 0;

            var itemType = crop.Produce.Type;
            return link.GetSortedLinkedEnabledStorages(citizen)
                .Where(storage => storage.Parent is not DroneDockObject)
                .SelectMany(storage => storage.Inventory.NonEmptyStacks)
                .Where(stack => stack.Item?.Type == itemType)
                .Sum(stack => stack.Quantity);
        }

        /// <summary>The configured ceiling for a crop, or zero when it has none -- the value an untouched grid row shows.</summary>
        public int CropCeilingFor(string crop) =>
            this.CropCeilings.FirstOrDefault(c => string.Equals(c.Crop, crop, StringComparison.Ordinal))?.Ceiling ?? 0;

        // ---------------------------------------------------------------
        // R30's wake. The drone must not poll: scanning every column of every assigned area
        // on every tick to discover that nothing has changed is exactly the poll the
        // requirement forbids, and it costs the same whether or not anyone is farming.
        //
        // So the drone scans when something might have changed. Two things can change it:
        // linked storage (a seed delivery, or produce leaving and dropping a crop back under
        // its ceiling), and a crop coming due. The first is this subscription; the second is
        // a time the strategy computes from the least-grown plant (R31).
        //
        // The dock's existing storage subscription is NOT extended to do this. That one
        // watches the dock's own drone-bay slot and spawns or despawns the paired drone; it
        // never fires for a linked container, and giving it a second job would put drone
        // pairing and farm waking on one callback.
        // ---------------------------------------------------------------

        private readonly List<Inventory> watchedFarmStorage = new();
        private Action<User> farmStorageCallback;

        // In-memory and starting non-zero, so a freshly loaded dock scans once before
        // settling. A restart is exactly the case where a cached "nothing to do" cannot be
        // trusted: the world may have changed while the server was down.
        private int farmWakeToken = 1;

        /// <summary>
        /// Changes whenever something might have given the farm work to do. The strategy
        /// compares it against the value it last scanned at, so an unchanged token plus a
        /// due time still in the future means there is nothing to look at.
        /// </summary>
        public int FarmWakeToken => this.farmWakeToken;

        /// <summary>Marks the farm worth re-scanning.</summary>
        public void RequestFarmWake() => this.farmWakeToken++;

        /// <summary>
        /// Re-points the watch at whatever the dock's link currently resolves.
        ///
        /// Driven from the dock's own throttled tick rather than from a link-network event,
        /// because the link component raises none for its membership changing -- the same
        /// reason the mining unload retries from the tick. Comparing the resolved set
        /// against the watched one is what makes this idempotent: an unchanged network
        /// re-subscribes nothing, and a changed one releases the old set before taking the
        /// new, so a container that left the network stops waking this dock.
        /// </summary>
        public void RefreshLinkedStorageWatch()
        {
            var citizen = this.StampedCitizen;
            if (citizen == null || !this.TryGetComponent<LinkComponent>(out var link))
            {
                this.ReleaseLinkedStorageWatch();
                return;
            }

            var current = link.GetSortedLinkedEnabledStorages(citizen)
                .Where(storage => storage.Parent is not DroneDockObject)
                .Select(storage => storage.Inventory)
                .Where(inventory => inventory != null)
                .ToList();

            if (current.Count == this.watchedFarmStorage.Count
                && current.All(this.watchedFarmStorage.Contains))
                return;

            this.ReleaseLinkedStorageWatch();

            this.farmStorageCallback ??= this.OnLinkedStorageChanged;
            foreach (var inventory in current)
            {
                inventory.OnChanged.Add(this.farmStorageCallback);
                this.watchedFarmStorage.Add(inventory);
            }
        }

        /// <summary>
        /// Drops every subscription this dock holds. Called when the link network changes
        /// and when the dock is destroyed -- the destroyed case is the one that matters,
        /// since a container outlives the dock and would otherwise keep a callback into a
        /// world object that no longer exists.
        /// </summary>
        public void ReleaseLinkedStorageWatch()
        {
            if (this.farmStorageCallback != null)
                foreach (var inventory in this.watchedFarmStorage)
                    inventory.OnChanged.Remove(this.farmStorageCallback);

            this.watchedFarmStorage.Clear();
        }

        /// <summary>
        /// Releases the farm's storage subscriptions when the dock goes away.
        ///
        /// Declared in the farming partial rather than in DroneDock.cs so the whole watch --
        /// take, re-point and release -- reads in one file. A linked container outlives the
        /// dock, so without this it keeps a callback into a destroyed world object.
        /// </summary>
        protected override void OnDestroy()
        {
            this.ReleaseLinkedStorageWatch();
            base.OnDestroy();
        }

        /// <summary>
        /// A linked container changed. The token is bumped unconditionally and cheaply; a
        /// drone already working never consults it mid-plot, so a change arriving during
        /// work interrupts nothing and is still seen when the drone next asks for a target.
        /// </summary>
        private void OnLinkedStorageChanged(User user) => this.RequestFarmWake();

        /// <summary>
        /// Whether the stamped citizen still holds the access every farming write is
        /// performed under, re-checked on the ACTING path rather than trusted from
        /// assignment time.
        ///
        /// The mining side does the same through <c>StampRefusalReason</c>, and farming
        /// went without it: a citizen whose full access was revoked kept having the drone
        /// plow, sow and harvest in their name, spending their linked storage, until every
        /// area was unassigned by hand. Assignment-time authorization answers "may you
        /// start this", and only a live re-check answers "may you still be doing it".
        /// </summary>
        public bool FarmStampIsValid()
        {
            var citizen = this.StampedCitizen;
            return citizen != null && this.HasFullAccess(citizen);
        }

        /// <summary>
        /// Records who is accountable for this dock's work. Shared with the mining side on
        /// purpose: a dock holds one drone, so it has one stamped citizen, and two fields
        /// would let the two halves disagree about who is answering for a world write.
        /// </summary>
        private void StampCitizen(User citizen)
        {
            this.StampedCitizenName = citizen.Name;
            this.StampedCitizenId = citizen.Id;
        }

        /// <summary>
        /// Every farm area as the readout shape the Farming tab renders (U10) and the pure
        /// <see cref="FarmJob"/> consumes (U4).
        ///
        /// Two reasons are derived here rather than read back from the last report, because
        /// the dock knows them without the drone: an area with no crop is awaiting one
        /// (R26), and a crop at its ceiling is holding (R27). Both can change while the
        /// drone is nowhere near, and a tab that waited for a visit to notice would be
        /// telling a player their own edit had not taken.
        /// </summary>
        public IReadOnlyList<FarmAreaReadout> ReadFarmJobStates()
        {
            var ledger = this.ReadCropCeilings();
            var readouts = new List<FarmAreaReadout>();

            foreach (var area in this.FarmAreas)
            {
                readouts.Add(new FarmAreaReadout(area, this.StateFor(area, ledger), area.LastFlat));
            }

            return readouts;
        }

        /// <summary>
        /// The job the drone is actually running: the ASSIGNED areas only.
        ///
        /// Separate from <see cref="ReadFarmJobStates"/>, which lists every drawn area so a
        /// citizen can select one to assign. Folding unassigned areas into the job made a
        /// dock with drawn-but-unassigned areas report "working", and made
        /// "no areas assigned" reachable only when nothing was drawn at all.
        /// </summary>
        public FarmJob ReadFarmJob()
        {
            var ledger = this.ReadCropCeilings();
            return new FarmJob(this.AssignedFarmAreas.Select(area => this.StateFor(area, ledger)));
        }

        private FarmAreaState StateFor(FarmAreaEntry area, CropCeilingLedger ledger)
        {
            if (string.IsNullOrEmpty(area.Crop))
                return FarmAreaState.AwaitingCrop(area.Name);

            var cropName = CropCatalog.DisplayNameFor(area.Crop);

            // A full store stops the harvest, not the farm: the ground still wants plowing
            // and sowing. Reporting "holding" while the drone is actively working the area
            // tells a citizen their farm has stopped when it has not.
            if (!ledger.MayHarvest(area.Crop, this.CountInLinkedStorage(area.Crop))
                && area.LastStallReason != (int)FarmStallReason.MissingMaterial
                && area.LastNextAction < 0)
                return FarmAreaState.CeilingReached(area.Name, cropName);

            var stall = area.LastStallReason < 0 ? (FarmStallReason?)null : (FarmStallReason)area.LastStallReason;
            switch (stall)
            {
                case FarmStallReason.MissingMaterial:
                    return FarmAreaState.ShortOfMaterial(area.Name, cropName, area.LastMissingMaterial ?? "a material");
                case FarmStallReason.WaitingOnGrowth:
                    return FarmAreaState.WaitingOnGrowth(area.Name, cropName, Math.Max(0, area.LastNextDueHours));
                case FarmStallReason.UnfitGround:
                    return FarmAreaState.UnfitGround(area.Name, cropName, area.LastUnfitCondition ?? "the ground");
                case FarmStallReason.LevelPassBlocked:
                    return FarmAreaState.LevelPassBlocked(
                        area.Name, cropName, area.LastUnfitCondition ?? "the pass cannot run");
                case FarmStallReason.PackRejected:
                    return FarmAreaState.PackRejected(area.Name, cropName);
                case FarmStallReason.BlocksRefused:
                    return FarmAreaState.BlocksRefused(
                        area.Name, cropName, area.LastUnfitCondition ?? "no reason was given");
                case FarmStallReason.LawRefusal:
                    return FarmAreaState.RefusedByLaw(area.Name, cropName);
                case FarmStallReason.PropertyRefusal:
                    return FarmAreaState.RefusedByProperty(area.Name, cropName);
                case FarmStallReason.HeldByOverlap:
                    return FarmAreaState.HeldByOverlap(area.Name, cropName, Math.Max(1, area.LastHeldPlotCount));
            }

            var action = area.LastNextAction < 0 ? FarmAction.LeaveAlone : (FarmAction)area.LastNextAction;
            return FarmAreaState.Workable(area.Name, cropName, action);
        }
    }
}
