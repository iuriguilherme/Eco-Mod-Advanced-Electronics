using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using AdvancedElectronics.Navigation;
using Eco.Core.Controller;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Shared.IoC;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Networking;
using Eco.Shared.Serialization;
using Eco.Shared.Services;
using Eco.Shared.SharedTypes;
using Eco.Shared.Voxel;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// The dock's "Mining" tab (U9): present only while a mining drone is slotted
    /// (R29, KD7), reporting the assigned area, the stamped citizen, job progress, and
    /// linked-storage headroom. Follows <see cref="SurveyComponent"/>'s structure: a
    /// readiness flag guards every setter so deserialization does not replay it as a
    /// click, values are derived strings rebuilt on refresh, and the one commit action
    /// (the assign button) is declared last because RPC methods always render after
    /// properties.
    ///
    /// Browsing and committing are separate controls (R40): a number selector pages
    /// through the survey docks the tab can see, and the single button assigns the
    /// selected one -- it is a remote call and therefore carries the acting player,
    /// which is the only way the stamp gets a citizen (the selector's setter never does).
    /// </summary>
    [Serialized, CreateComponentTabLoc("Mining", true), HasIcon]
    public class MiningComponent : WorldObjectComponent, IOperatingWorldObjectComponent
    {
        private const int MaxBrowsePositions = 40;

        public override WorldObjectComponentClientAvailability Availability =>
            WorldObjectComponentClientAvailability.UI;

        /// <summary>True while the mining job is working (R45) -- what makes the dock Operating, so fuel and wear flow.</summary>
        public bool Operating => this.Parent is DroneDockObject dock && dock.DroneIsWorking;

        private bool ready;
        private int browseIndex;

        // Four rows, not nine. The member NAME is the row label, so these read "Assigned Area",
        // "Current Owner", "Job Status", "Progress".
        //
        // What was dropped -- stop reason, skips by category, last refusal, shaft depth with its
        // two stamps, and a static headroom note -- was a debugging surface that had grown on a
        // player-facing panel, one row per fact discovered during a live pass. Every one of them
        // is still printed by `/drone state`, which is where "why was that plot skipped" belongs.
        // Nothing was lost, and the headroom row carried no data at all: it was a fixed sentence.

        [SyncToView, Autogen, UITypeName("String")]
        public string AssignedArea { get; private set; } = string.Empty;

        /// <summary>The citizen every removal is performed as, and who is accountable for it (R18).</summary>
        [SyncToView, Autogen, UITypeName("String")]
        public string CurrentOwner { get; private set; } = string.Empty;

        [SyncToView, Autogen, UITypeName("String")]
        public string JobStatus { get; private set; } = string.Empty;

        /// <summary>Area progress and the current plot's shaft depth, on one line.</summary>
        [SyncToView, Autogen, UITypeName("String")]
        public string Progress { get; private set; } = string.Empty;

        [SyncToView, Autogen, UITypeName("StringTitle")]
        public string BrowseHeader { get; private set; } = "Available mining areas";

        /// <summary>The offered survey docks and their areas, numbered -- what the selector below refers to.</summary>
        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string AvailableAreas { get; private set; } = string.Empty;

        /// <summary>Selection cursor, by position in the offered list. View-only -- it never assigns anything (R40).</summary>
        [Serialized, Eco, Range(0, MaxBrowsePositions), UITypeName("Int32")]
        public int SelectArea
        {
            get => this.browseIndex + 1;
            set
            {
                if (!this.ready) return;
                var offered = this.OfferedAreas().ToList();
                if (this.browseIndex == value - 1) return;

                this.browseIndex = DockReadout.ClampCursor(value - 1, offered.Count);
                this.RefreshAll();
            }
        }

        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Assign Selected Area")]
        public void AssignSelectedArea(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;

            var offered = this.OfferedAreas().ToList();
            if (this.browseIndex < 0 || this.browseIndex >= offered.Count)
            {
                player?.MsgLocStr("No area is selected to assign.", NotificationStyle.Error);
                return;
            }

            var (sourceDock, area) = offered[this.browseIndex];
            if (!dock.AssignMiningArea(sourceDock, area, player?.User, out var refusalReason))
                player?.MsgLocStr($"Could not assign -- {refusalReason}.", NotificationStyle.Error);

            this.RefreshAll();
        }

        /// <summary>
        /// Clears this dock's mining assignment (R7). Until this existed the only way to stop a
        /// drone was to point it at another area, which is not a stop -- it is a different job, and
        /// it meant an assignment survived every restart and re-ran itself on load.
        /// </summary>
        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Unassign Area")]
        public void UnassignArea(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;

            if (dock.AssignedMiningArea == null)
            {
                player?.MsgLocStr("This dock has no mining area assigned.", NotificationStyle.Warning);
                return;
            }

            var released = dock.UnassignMiningArea();
            this.RefreshAll();

            // R38 rides this message. It is a DIFFERENT carrier from R39's refusal on purpose:
            // the refusal string exists only on a refused assignment, while this has to be
            // unconditional -- a player who does not know they dropped their claim cannot know
            // they are exposed (KTD10).
            var release = MiningReadout.FormatClaimRelease(released, PlotUtil.PropertyPlotLength);
            player?.MsgLocStr(
                release.Length == 0
                    ? "Mining area unassigned. The drone returns to its dock."
                    : $"Mining area unassigned. The drone returns to its dock -- {release}.",
                NotificationStyle.Info);
        }

        public override void Initialize()
        {
            base.Initialize();
            this.ready = true;
            this.RefreshAll();
        }

        /// <summary>
        /// The survey docks this dock may consume areas from: same owner, holding at least one
        /// area (R2, R3, KD15).
        ///
        /// The owner filter is the answer to "a drone dock should not interact with every other
        /// drone dock in the world -- it is a private world object like any other". Until it
        /// existed, this listed every dock on the server, including ones the player had no access
        /// to, and the assign path would happily take one.
        ///
        /// It is a rule rather than a player action, which is its known weakness: co-owned or
        /// company land still shares broadly, because Owners is a Title and two docks on the same
        /// deed legitimately share it. That is the accepted cost of not adding a pairing UI.
        /// Access filtering (R39) still happens at assign time, where the acting player is known
        /// -- ownership answers "whose docks are these", authorization answers "may YOU use them",
        /// and both are needed.
        /// </summary>
        private IEnumerable<(DroneDockObject Dock, SurveyAreaEntry Area)> OfferedAreas()
        {
            if (this.Parent is not DroneDockObject self) return Enumerable.Empty<(DroneDockObject, SurveyAreaEntry)>();

            var exclusionHolders = DroneDockObject.DocksHoldingExclusions();

            // R14: the radius sits on top of the owner test, never inside the walk. R44 and R47
            // sit on top of both.
            return this.OwnedAreas()
                .Where(o => self.IsInDockNetwork(o.Dock))
                .Where(o => OfferableToMiningDock(o.Dock, o.Area, exclusionHolders))
                .ToList();
        }

        /// <summary>
        /// Whether one area may appear on a mining dock's offered list (R44, R47) -- the same test
        /// <see cref="RefreshAll"/> applies, factored out because the two lists must agree PLOT
        /// FOR PLOT. The selector commits by POSITION in the offered list, so a filter applied to
        /// one list and not the other assigns the area the player did not pick.
        ///
        /// <para>
        /// <c>[cleared]</c> and <c>[empty]</c> are withheld because there is nothing a mining
        /// drone could take; both stay visible on both tabs and stay assignable to a survey dock,
        /// which is the only thing that returns them to the ramp (R44). Farmland is withheld
        /// because its ground is reserved (R47), whether or not the farm is assigned -- which is a
        /// question about KIND that the lifecycle ladder cannot answer, since a farming area never
        /// reads a rung of the ramp at all.
        /// </para>
        /// <para>
        /// This is the OFFER half only. Whether the ground is already held is settled at the
        /// assignment itself, under the one lock KTD6 defines, because it depends on every area
        /// overlapping this one and that is a world walk per area -- not something a roster tick
        /// can absorb.
        /// </para>
        /// </summary>
        private static bool OfferableToMiningDock(
            DroneDockObject sourceDock, SurveyAreaEntry area, IReadOnlyCollection<DroneDockObject> exclusionHolders) =>
            AreaClaims.MayBeOfferedToMiningDock(
                area.Kind,
                DroneDockObject.StatusOfArea(sourceDock.ObjectID, area, exclusionHolders, area.Kind));

        /// <summary>
        /// The RAW enumeration (KTD8): every dock in the world that publishes survey areas, with
        /// no owner test and no distance test applied.
        ///
        /// <para>
        /// This is split out rather than inlined because two consumers want the same walk under
        /// different rules and would otherwise contradict each other. Offering areas is narrowed
        /// by ownership and by the dock-network radius; overlap detection has to see PAST both,
        /// since two areas overlap on the ground whether or not the docks that drew them are
        /// neighbours or share a deed. Folding either filter in here would silently make overlap
        /// blind to exactly the cases it exists to catch. Nothing may be filtered here.
        /// </para>
        /// </summary>
        public static IEnumerable<(DroneDockObject Dock, SurveyAreaEntry Area)> AllPublishedAreas() =>
            ServiceHolder<IWorldObjectManager>.Obj.All
                .OfType<DroneDockObject>()
                .Where(Publishes)
                .SelectMany(d => d.SurveyAreas.Select(a => (Dock: d, Area: a)));

        /// <summary>Whether a dock is one whose survey areas the raw walk yields.</summary>
        private static bool Publishes(DroneDockObject dock) =>
            dock != null && !dock.IsDestroyed && dock.HasComponent<SurveyComponent>();

        /// <summary>
        /// <b>The same raw walk (KTD8), reduced to what the overlap path is allowed to know
        /// (R41, R34).</b> Every published area in the world as an
        /// <see cref="AreaProjection"/> -- plots, identity, claim, kind -- and nothing else.
        ///
        /// <para>
        /// The projection is built HERE, at the enumeration site, and that placement is the
        /// mechanism rather than a convention. R41 makes this channel internal: the system may
        /// see where two areas want the same ground, but a dock near another player's area grants
        /// no sight of it. If a <see cref="SurveyAreaEntry"/> were handed on instead, every
        /// present and future consumer would be one dereference away from that area's findings,
        /// stamps and exclusions, and the rule would hold only as long as nobody added a call
        /// site. Projecting at the boundary makes the leak unrepresentable downstream.
        /// </para>
        /// <para>
        /// Unfiltered in both directions, exactly as <see cref="AllPublishedAreas"/> is: no owner
        /// test and no radius, because two areas collide however far apart their docks sit and
        /// whoever owns them (R34).
        /// </para>
        /// <para>
        /// ONE world walk, not two. The claim is now the area's OWN record (U13), so the walk no
        /// longer has to find the holders -- but the <c>[empty]</c> test does need the docks
        /// carrying exclusions, and those are the same objects. Both come off the single
        /// materialised list, because this feeds a roster refresh that runs on the dock's tick.
        /// </para>
        /// </summary>
        public static IReadOnlyList<AreaProjection> AllAreaProjections()
        {
            var docks = ServiceHolder<IWorldObjectManager>.Obj.All
                .OfType<DroneDockObject>()
                .Where(d => !d.IsDestroyed)
                .ToList();

            // The same subset DocksHoldingExclusions() collects, taken off the list already in
            // hand rather than by walking the world a second time.
            var exclusionHolders = docks.Where(d => d.MiningExclusions.Count > 0).ToList();

            var projections = new List<AreaProjection>();
            foreach (var dock in docks)
            {
                if (!Publishes(dock)) continue;

                foreach (var area in dock.SurveyAreas)
                {
                    // R37's [empty] exception, which is why the status is read here at all: an
                    // area with nothing left to take holds nothing, and that is what lets ground
                    // pass from one purpose to the next with no release negotiated. A [cleared]
                    // area keeps its claim -- its exclusion may lift.
                    var holds = AreaClaims.HoldsClaim(
                        area.HasClaim,
                        DroneDockObject.StatusOfArea(dock.ObjectID, area, exclusionHolders, area.Kind));

                    projections.Add(new AreaProjection(
                        area.Id, dock.ObjectID, area.Plots(), holds, area.Kind));
                }
            }

            return projections;
        }

        /// <summary>
        /// Whether one area collides with any other area in the world (R34, R35) -- the single
        /// bit both roster lines carry as an uncoloured <c>[overlap]</c>.
        /// </summary>
        /// <param name="published">
        /// The projections, hoisted by a caller rendering a whole roster. Null makes this collect
        /// them itself, which is right for a one-area read and wrong for a roster: a refresh runs
        /// off the dock's TICK, and re-walking the world once per area would put an
        /// O(areas x world objects) sweep on a repeating path.
        /// </param>
        public static bool OverlapsAnything(
            DroneDockObject owner, SurveyAreaEntry area, IReadOnlyList<AreaProjection> published = null) =>
            AreaOverlap.HasAny(Project(owner, area), published ?? AllAreaProjections());

        /// <summary>
        /// Every collision one area has, with the plots each covers -- what the diagnostic
        /// command turns into a centre block per shared plot (R36).
        /// </summary>
        public static IReadOnlyList<AreaOverlapMatch> OverlapsOf(
            DroneDockObject owner, SurveyAreaEntry area, IReadOnlyList<AreaProjection> published = null) =>
            AreaOverlap.Matches(Project(owner, area), published ?? AllAreaProjections());

        /// <summary>
        /// One area as the geometry side of a comparison. The claim flag and the kind are left at
        /// their defaults deliberately: this is the area being ASKED about, and nothing reads
        /// either field off the asking side -- the answer is about what the OTHER areas hold.
        /// </summary>
        private static AreaProjection Project(DroneDockObject owner, SurveyAreaEntry area) =>
            owner == null || area == null
                ? null
                : new AreaProjection(area.Id, owner.ObjectID, area.Plots());

        /// <summary>
        /// The raw walk narrowed by ownership alone (R15) -- everything this dock is entitled to
        /// see, whether or not it can reach it. The distance filter is applied one layer up, so
        /// this is also what answers "how many owned docks are merely too far away" for R23's
        /// notice: a dock that fails the owner test is none of this player's business and must
        /// not be counted, or the panel would report strangers' docks as out of range.
        /// </summary>
        private IEnumerable<(DroneDockObject Dock, SurveyAreaEntry Area)> OwnedAreas()
        {
            if (this.Parent is not DroneDockObject self) return Enumerable.Empty<(DroneDockObject, SurveyAreaEntry)>();

            return AllPublishedAreas().Where(o => SharesOwnerWith(self, o.Dock));
        }

        /// <summary>
        /// Whether two docks belong to the same owner. An unowned dock matches only another
        /// unowned one -- deliberately, since "nobody owns it" is not a household, and treating
        /// null as a wildcard would put every unclaimed dock in the world back on the list.
        /// </summary>
        private static bool SharesOwnerWith(DroneDockObject self, DroneDockObject other) =>
            ReferenceEquals(self, other) || Equals(self.Owners, other.Owners);

        public void RefreshAll()
        {
            if (this.Parent is not DroneDockObject dock) return;

            // The 0.3.0 save fold (DroneDock.Migration.cs). Here rather than in Initialize
            // because it needs the dock's area to resolve, and at Initialize the survey dock
            // that holds it may not have loaded yet. This runs on the first refresh and on
            // every one after it, so a fold that could not resolve its area is retried instead
            // of lost; it costs one count check once the fold has happened.
            dock.MigrateLegacyMinedStamps();

            // The owner-filtered list is materialised ONCE and the radius applied to it here,
            // rather than calling OfferedAreas() and then walking the world a second time for the
            // out-of-range count. This runs off the dock's tick; a second world sweep per refresh
            // is not a cost this path can absorb. Order is the raw walk's order either way, so the
            // positions the selector commits by are unchanged.
            // Hoisted once for the whole list: this runs off the dock's tick, and collecting it
            // per area would put an O(areas x world objects) sweep on a repeating path. Collected
            // BEFORE the offered list because R44's filter reads it.
            var exclusionHolders = DroneDockObject.DocksHoldingExclusions();

            var owned = this.OwnedAreas().ToList();
            var offered = owned
                .Where(o => dock.IsInDockNetwork(o.Dock))
                .Where(o => OfferableToMiningDock(o.Dock, o.Area, exclusionHolders))
                .ToList();

            // R23: counted as DOCKS, so one distant dock holding nine areas reads as one thing to
            // move. Distinct because the walk yields one entry per area.
            var outOfRangeDocks = owned
                .Where(o => !dock.IsInDockNetwork(o.Dock))
                .Select(o => o.Dock)
                .Distinct()
                .Count();

            this.browseIndex = DockReadout.ClampCursor(this.browseIndex, offered.Count);

            var assigned = dock.AssignedMiningArea;

            // Hoisted for the same reason as exclusionHolders above, and read RAW (R34): the overlay has to see past the
            // radius this list was just narrowed by, and past ownership -- two areas collide
            // however far apart their docks sit and whoever owns them.
            var published = AllAreaProjections();

            this.AvailableAreas = DockReadout.AtReadableSize(MiningReadout.FormatAvailableAreas(
                offered
                    .Select((o, i) => MiningReadout.FormatOfferedAreaLine(
                        Snapshot(dock, o.Dock, o.Area, i + 1, assigned, exclusionHolders, published), o.Dock.Name))
                    .ToList(),
                outOfRangeDocks,
                DroneDockObject.DockNetworkRadius));

            var reference = assigned;
            var assignmentOutOfRange = false;
            this.AssignedArea = reference == null
                ? "none"
                : DescribeAssignment(dock, reference, out assignmentOutOfRange);

            var citizen = dock.StampedCitizen;
            this.CurrentOwner = citizen == null ? "unstamped" : citizen.Name;

            var job = dock.MiningJob;

            var travel = TravelPhrase(dock);

            if (job != null)
            {
                // The halt refuses dispatch before a job exists, so a blocked reason has to be
                // able to speak with no job present -- setting it only inside this branch is what
                // made a halted dock silent. Folded into the status line now that the tab has no
                // row of its own for it.
                this.JobStatus = MiningReadout.FormatJobStatus(job.Status, job.WorkedCount, reference != null, travel);

                // The shaft half only while one is actually being cut. A finished or abandoned job
                // keeps its last layer counts, and reporting them next to "returning to dock" reads
                // as a shaft still in progress.
                var cutting = job.Status == MiningJobStatus.Working || job.Status == MiningJobStatus.WaitingToUnload;

                // And nothing at all once the area is gone. The counts describe work on an area
                // this dock is no longer pointed at, so leaving them up meant an unassigned dock
                // still reporting a plot tally from the job it had just been taken off.
                this.Progress = reference == null
                    ? string.Empty
                    : MiningReadout.FormatProgress(
                        job.PlotCount, job.WorkedCount, job.SkippedCount,
                        cutting ? job.ShaftLayersDone : 0,
                        cutting ? job.ShaftLayersTotal : 0);
            }
            else
            {
                this.JobStatus = string.IsNullOrEmpty(travel) ? "no drone docked" : travel;
                this.Progress = string.Empty;
            }

            var blocked = MiningReadout.FormatBlockedReason(
                MiningHalt.IsHalted, job?.EndReason, assignmentOutOfRange);
            if (!string.IsNullOrWhiteSpace(blocked))
                this.JobStatus = blocked;

            this.Changed(nameof(this.AssignedArea));
            this.Changed(nameof(this.CurrentOwner));
            this.Changed(nameof(this.JobStatus));
            this.Changed(nameof(this.Progress));
            this.Changed(nameof(this.AvailableAreas));
            this.Changed(nameof(this.SelectArea));
        }

        /// <summary>Where this dock's drone is, or empty when there is no drone to ask.</summary>
        private static string TravelPhrase(DroneDockObject dock)
        {
            var drone = dock.SpawnedDrone;
            if (drone == null || drone.IsDestroyed) return string.Empty;

            return drone.TryGetComponent<DroneLifecycle>(out var lifecycle)
                ? DockReadout.FormatTravel(lifecycle.Status, lifecycle.TravelTarget)
                : string.Empty;
        }

        /// <summary>
        /// One offered area reduced to the same shape the Survey tab renders (R29), so the two
        /// tabs cannot disagree about anything but the dock prefix and this dock's own filter.
        ///
        /// <para>
        /// The old per-tab mined test lived here and is gone: it mixed a fact about the AREA (the
        /// stamps) with a fact about THIS DOCK's last job, and a status the area owns cannot be
        /// answered partly from the reader. <see cref="DroneDockObject.StatusOfArea"/> answers it
        /// once, from the shared record, for every dock that looks (KTD3).
        /// </para>
        /// </summary>
        /// <param name="reader">The mining dock doing the reading -- its material filter narrows the summary, and nothing else.</param>
        /// <param name="owner">The survey dock that owns the area, whose name prefixes the line.</param>
        private static AreaSnapshot Snapshot(
            DroneDockObject reader,
            DroneDockObject owner,
            SurveyAreaEntry area,
            int position,
            MiningAreaRef assigned,
            IReadOnlyCollection<DroneDockObject> exclusionHolders,
            // No default: this tab only ever renders a whole roster, and a per-area collection
            // here would be the O(areas x world objects) sweep the hoists above exist to avoid.
            IReadOnlyList<AreaProjection> published)
        {
            var top = area.ReadFindings()
                .Where(f => f.Found && reader.IsMaterialShown(f.OreType))
                .OrderByDescending(f => f.Count)
                .FirstOrDefault();

            var isAssigned = assigned != null
                             && assigned.OwningDockId == owner.ObjectID
                             && assigned.AreaId == area.Id;

            return new AreaSnapshot(
                position, area.Name, area.PlotCount, area.CoveragePercent, top,
                // The area's KIND selects which status is derived at all (R30, R46), exactly as
                // the Survey tab does it. Omitting it here defaulted every line to the mining
                // ladder, so a farming area listed on this tab rendered a ladder rung instead of
                // [farm] -- a hole in an invariant whose whole point is that it is structural.
                // The choice is made once, inside StatusOfArea; nothing here derives a mining
                // status and then corrects it.
                DroneDockObject.StatusOfArea(owner.ObjectID, area, exclusionHolders, area.Kind),
                isAssigned,
                // Reachability is a fact about a trip in progress, so only the assigned area has
                // an answer at all -- an unassigned one has none rather than a negative one.
                isUnreachable: isAssigned && DroneReportsUnreachable(reader),
                // R35/R36: the panel says only THAT this area collides with another. Which plots,
                // and whether the other area holds them, is the diagnostic command's to say --
                // one summary row per fact here, a row per plot there.
                hasOverlap: OverlapsAnything(owner, area, published),
                // R8. Same label as the Survey tab, for the same reason and from the same fact:
                // both tabs render one area through one line builder, so a player reading either
                // is told when the figures in front of them describe ground that has since
                // changed. The figures are neither recalculated nor hidden.
                needsResurvey: area.AnyPlotNeedsReReading);
        }

        /// <summary>True when this dock's drone is currently reporting that it cannot reach its area.</summary>
        private static bool DroneReportsUnreachable(DroneDockObject dock)
        {
            var drone = dock.SpawnedDrone;
            return drone != null
                   && !drone.IsDestroyed
                   && drone.TryGetComponent<DroneLifecycle>(out var lifecycle)
                   && (lifecycle.Status == AdvancedElectronics.Navigation.DroneStatus.Unreachable
                       || lifecycle.CannotReachAssignedArea);
        }

        /// <summary>
        /// The assigned-area row, and whether that assignment has fallen outside the dock network
        /// (R23, R24).
        ///
        /// <para>
        /// Both answers come out of ONE resolve. <see cref="MiningAreaRef.Resolve"/> is not a
        /// pure query -- it carries the consecutive-failure count that decides when a reference
        /// is finally called gone -- so asking it twice per refresh would burn that tolerance at
        /// double rate and shorten the load-ordering grace period the counter exists to provide.
        /// </para>
        /// </summary>
        private static string DescribeAssignment(
            DroneDockObject dock, MiningAreaRef reference, out bool outOfRange)
        {
            outOfRange = false;

            var signal = reference.Resolve(out var sourceDock, out var area);
            switch (signal)
            {
                case AreaLookupSignal.Found:
                    // R24: the area still exists and the assignment is untouched. Being out of
                    // range is something the dock REPORTS, never something it acts on by
                    // clearing -- a world that upgrades into the radius must not look like the
                    // mod quietly losing the player's assignment.
                    outOfRange = !dock.IsInDockNetwork(sourceDock);
                    return MiningReadout.FormatAssignedArea(sourceDock.Name, area.Name, !outOfRange);

                case AreaLookupSignal.NotYetResolved:
                    return "resolving...";

                default:
                    return "gone";
            }
        }
    }
}
