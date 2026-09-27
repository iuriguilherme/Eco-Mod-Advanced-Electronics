using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AdvancedElectronics.Navigation;
using Eco.Core.Controller;
using Eco.Gameplay.Civics.GameValues;
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
    /// The dock's single "Survey" tab: manage areas, choose which one the drone works on, and read
    /// any area's findings.
    ///
    /// ONE selector and three buttons, mirroring the mining tab. The selector chooses which area you
    /// are looking at AND which one Assign acts on; assignment happens only when a button is pressed.
    ///
    /// This panel was built around a one-button rule, and that rule has been retired deliberately.
    /// The rule bought a compact layout and cost correctness: with assignment living on the stepper
    /// itself, moving the stepper to read a neighbouring area's findings reassigned the working drone
    /// to it. A player cannot browse without disturbing the drone, and no amount of layout thrift is
    /// worth that. Three BigButtons cost roughly ten standard rows; the panel wears it.
    ///
    /// What the button budget was protecting against is still true and still shapes this file.
    /// BigButton is the panel's COMMIT control — fixed size, not groupable — so at ~70px each costs
    /// 3.2 standard rows and leaves the horizontal axis empty. And RPC methods render AFTER all
    /// properties whatever the declaration order, so no button can serve as a top anchor.
    ///
    /// Selection is ONE stepper rather than a control per area, for a reason beyond layout: N
    /// controls cannot share one field. The client writes back EVERY editable member as a batch on
    /// any interaction, in declaration order, so one click on a per-area checkbox arrived as pos1=true
    /// followed by pos2..pos6=false and the trailing writes unassigned what the first one set —
    /// measured as six setter calls per click, dock left at id 0. Comparing against derived state to
    /// make the siblings no-ops did not save it. One member holding the value has no sibling to stomp
    /// it, and costs one row whatever the area count. That batch write-back is also why assignment
    /// must not live in a setter at all: an RPC fires once, on purpose, when the player asks for it.
    ///
    /// Members render one per row in declaration order, so the order below IS the reading order. The
    /// numbered list sits above the steppers because their values are positions and mean nothing
    /// until the list that names them has been read — not a preference: a row label cannot be
    /// generated at runtime, since DynamicTitle resolves once at window-open and never re-resolves.
    /// </summary>
    [Serialized, CreateComponentTabLoc("Survey", true), HasIcon]
    public class SurveyComponent : WorldObjectComponent, IOperatingWorldObjectComponent
    {
        private const int MaxAreaPlots = 40; // v1 tier cap (R1b); drone-tier-owned later.

        // MaxSurveyAreas used to be declared here, at 10. There is ONE per-dock area limit now
        // (R19, KTD7), covering farms and survey areas together because they are one collection
        // since U3, and it lives in AreaCapacity in the Eco-free assembly -- both tabs and both
        // pickers read it, and it is the one part of U10 that can carry a unit test.
        //
        // The number is unchanged at ten, so the layout reasoning that chose it still holds: the
        // panel measures roughly 552px against a ~605px viewport at ten areas, and the worst case
        // -- ten areas AND one fully surveyed area reporting every material the drone detects --
        // lands within a few pixels of the fold. What moved is the SCOPE: ten now counts a dock's
        // farms too, and a dock the fold left above ten keeps every area and may add none.
        //
        // It must stay a compile-time constant because the steppers' Range is a plain C#
        // attribute and the view system has no RangeParam(nameof(...)) sibling to track a live
        // count.

        public override WorldObjectComponentClientAvailability Availability =>
            WorldObjectComponentClientAvailability.UI;

        /// <summary>
        /// True exactly while the drone is working (R9). This is what makes the dock Operating, and
        /// FuelConsumptionComponent burns only while its parent is Operating -- so the fuel half of
        /// R9 needs no code of its own, including the return-leg exemption.
        /// </summary>
        // NOT [Serialized] -- derived on every read, so there is no member to load a saved value
        // back into. It answers "is the dock working right now", which is recomputed, never stored.
        public bool Operating => this.Parent is DroneDockObject dock && dock.DroneIsWorking;

        /// <summary>
        /// False until <see cref="Initialize"/> has run. Deserialization assigns `[Serialized]`
        /// members by invoking their setters, and the setters below write through to the dock — so
        /// without this gate a world load replays persisted positions as drone assignments. Not
        /// itself `[Serialized]`: it must start false on every load.
        /// </summary>
        private bool ready;

        /// <summary>
        /// Index into the dock's area list of the area whose findings are shown. Purely a VIEW
        /// cursor: it never changes what the drone is assigned to, so reading area 4 does not
        /// dispatch the drone there.
        /// </summary>
        private int viewIndex;

        // ---------------------------------------------------------------
        // Status and area list
        // ---------------------------------------------------------------

        /// <summary>
        /// What the drone is DOING. Three states, not two: assignment does not require a drone to
        /// exist, so without the no-drone variant the tab reports success while nothing happens.
        /// </summary>
        [SyncToView, Autogen, UITypeName("String")]
        public string DroneStatus { get; private set; } = "none docked";

        /// <summary>
        /// Names the assigned area. Directly under the status on purpose: the two answer one
        /// question between them -- what is the drone doing, and to what -- and reading them apart
        /// meant scrolling past the whole area roster to find the second half.
        /// </summary>
        [SyncToView, Autogen, UITypeName("String")]
        public string AssignedArea { get; private set; } = string.Empty;

        // StringTitle, not LinedHeader. LinedHeader and SectionHeader render the MEMBER NAME and
        // discard the value -- an earlier build of this tab showed "Assign Header" on screen.
        // StringTitle and GeneralHeader are the two that render what you assign.
        [SyncToView, Autogen, UITypeName("StringTitle")]
        public string AssignHeader { get; private set; } = "Areas";

        /// <summary>The dock's numbered area list — what the position numbers below refer to.</summary>
        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string AreasDisplay { get; private set; } = string.Empty;

        // ---------------------------------------------------------------
        // Selection and findings
        // ---------------------------------------------------------------

        [SyncToView, Autogen, UITypeName("StringTitle")]
        public string FindingsHeader { get; private set; } = "Selected area";

        /// <summary>
        /// The selected area, by position: whose findings are shown below, AND what
        /// <see cref="AssignSelectedArea"/> acts on. One selector for both, matching the mining
        /// tab's Browse Position.
        ///
        /// Selecting is deliberately inert with respect to the drone. This used to be two
        /// controls, and the assigning one wrote through on every change -- so scrolling the list
        /// to look at a neighbouring area's findings reassigned the working drone to it. Moving
        /// the selection now changes only what you are LOOKING at; the Assign and Unassign
        /// buttons are the only things that change what the drone does.
        ///
        /// <para>
        /// The range reaches every area a pre-fold dock can carry rather than the ten a dock may
        /// ADD (U10, KTD7): a dock the fold left above the limit keeps all of them, and a cursor
        /// bounded by the cap would list the ones past the tenth and let nobody select, assign or
        /// delete them. The live bound is the clamp below, against the count of the areas THIS
        /// tab shows -- farms are the Farming tab's and are not counted here.
        /// </para>
        /// </summary>
        [Serialized, Eco, Range(1, AreaCapacity.MaxAddressablePositions), UITypeName("Int32")]
        public int ViewPosition
        {
            get => this.viewIndex + 1;
            set
            {
                if (!this.ready) return;                    // deserialization, not a player
                if (this.Parent is not DroneDockObject dock) return;
                if (this.viewIndex == value - 1) return;    // batch write-back of an unchanged value

                this.viewIndex = DockReadout.ClampCursor(value - 1, dock.SurveyKindAreas.Count);
                this.RefreshAll();
            }
        }

        /// <summary>Which area the findings below belong to, and whether it is the assigned one.</summary>
        [SyncToView, Autogen, UITypeName("String")]
        public string ViewingDisplay { get; private set; } = string.Empty;

        /// <summary>
        /// Material targets: pick which materials the survey results show, the same way items and tags
        /// are picked in a recipe or a law. Empty shows everything found.
        ///
        /// Scoped to the stock "Excavatable" tag. Established by live diagnostics (`/drone tags`):
        /// every material the drone actually detects -- clay, coal, crushed variants, limestone, peat,
        /// sandstone, sulfur -- carries it, while the crafted buildables that polluted a plain
        /// BlockItem picker (ashlar, brick, lumber, hewn log) and the placeables (gasoline, logs) do
        /// not. It is not a perfect fit (a few non-target soils such as dirt and tailings are also
        /// excavatable, and selecting one simply does nothing), but it is the closest stock tag and
        /// the only kind the client can use.
        ///
        /// A custom tag covering exactly the detectable set was built and did not work: the server-side
        /// registry was correct (30 of 113 block items tagged, tag registered, classifier agreeing) yet
        /// the picker stayed empty. The cause is NOT that mods register too late for tags in general --
        /// InitMods() runs before TagManager.Initialize() (Eco.ModKit/ModDataSync.cs:63-66), so a [Tag]
        /// ATTRIBUTE on a mod type, or on a vanilla item replaced by a .override file, does reach the
        /// client. What fails is RUNTIME association: the client filters RequiredTag against
        /// ViewClassInfo.Tags, built once while ControllerManager is constructed
        /// (Eco.Core/Controller/ControllerMarshalerService.cs:367), and anything tagged after that build
        /// is invisible to the picker. So the attribute/.override route remains open; only the runtime
        /// route is closed. See docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md.
        ///
        /// Confirmed live: GamePickerList renders and filters from a WorldObjectComponent tab, even
        /// though every vanilla usage is inside a civics GameValue.
        /// </summary>
        [Eco, AllowEmpty, RequiredTag(BlockTags.Excavatable)]
        [LocDescription("Materials to show in the survey results. Leave empty to show everything found.")]
        public GamePickerList<BlockItem> MaterialTargets { get; set; } = new();

        /// <summary>The viewed area's findings (R7). Refreshed from the dock's tick.</summary>
        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string ResultsDisplay { get; private set; } = string.Empty;

        // ---------------------------------------------------------------
        // Buttons. Declared last because that is where they render anyway.
        // ---------------------------------------------------------------

        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Manage Areas on Map")]
        public async Task ManageAreasOnMap(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;
            await SurveyAreaPicker.ManageAreas(player, dock, MaxAreaPlots);
            this.RefreshAll();
        }

        /// <summary>
        /// Sends the drone to the area currently selected above (R13 -- reassignment re-paths
        /// immediately). Assigning is an explicit act, which is the whole point of this button
        /// existing: the selector used to assign as a side effect of being moved.
        /// </summary>
        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Assign Selected Area")]
        public void AssignSelectedArea(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;

            var listed = dock.SurveyKindAreas;
            if (this.viewIndex < 0 || this.viewIndex >= listed.Count)
            {
                player?.MsgLocStr("No area is selected to assign.", NotificationStyle.Error);
                return;
            }

            var area = listed[this.viewIndex];

            // R39's refusal rides the string the assign path already returns -- no control is
            // added to the tab (KTD10).
            if (!dock.AssignSurveyArea(area.Id, out var refusalReason, out _))
            {
                this.RefreshAll();
                player?.MsgLocStr(
                    refusalReason == null
                        ? $"Could not assign '{area.Name}'."
                        : $"Could not assign '{area.Name}' -- {refusalReason}.",
                    NotificationStyle.Error);
                return;
            }

            this.RefreshAll();
            player?.MsgLocStr($"Survey area '{area.Name}' assigned.", NotificationStyle.Info);
        }

        /// <summary>
        /// Clears this dock's survey assignment. The map picker can assign but had no way to
        /// un-assign, so stopping a survey drone meant pointing it at a different area -- and
        /// picking a non-existent one left it flipping between idle and unreachable, chasing
        /// somewhere that was never there.
        /// </summary>
        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Unassign Area")]
        public void UnassignArea(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;

            if (dock.AssignedSurveyAreaId == 0)
            {
                player?.MsgLocStr("This dock has no survey area assigned.", NotificationStyle.Warning);
                return;
            }

            dock.AssignSurveyArea(0, out _, out var released);
            this.RefreshAll();

            // R38 rides this message, which is the one surface that fires on EVERY successful
            // unassign -- the refusal string exists only on a refused assignment and could never
            // carry something unconditional (KTD10).
            var release = MiningReadout.FormatClaimRelease(released, PlotUtil.PropertyPlotLength);
            player?.MsgLocStr(
                release.Length == 0
                    ? "Survey area unassigned. The drone returns to its dock."
                    : $"Survey area unassigned. The drone returns to its dock -- {release}.",
                NotificationStyle.Info);
        }

        public override void Initialize()
        {
            base.Initialize();
            this.ready = true;
            this.RefreshAll();
        }

        // --- Refresh ---

        /// <summary>
        /// Rebuilds every synced member and pushes it. Driven by the dock's one-second tick, which is
        /// what actually makes a change appear: a Changed() raised inside a setter does not reach the
        /// client on its own. Nothing here writes a member the player owns, so a push landing in the
        /// same second as a click cannot fight it.
        /// </summary>
        public void RefreshAll()
        {
            if (this.Parent is not DroneDockObject dock) return;

            // Hoisted once for the whole refresh, for the same reason the roster's
            // exclusionHolders and published lists are hoisted inside BuildAreasText: this runs
            // off the dock's one-second tick, and the five builders below used to rebuild this
            // filtered list from the dock's whole collection one after another.
            var listed = dock.SurveyKindAreas;

            this.viewIndex = DockReadout.ClampCursor(this.viewIndex, listed.Count);

            this.DroneStatus     = BuildDroneStatus(dock);
            this.AreasDisplay    = this.BuildAreasText(dock, listed);
            this.AssignedArea = BuildAssignedText(dock, listed);
            this.ViewingDisplay  = this.BuildViewingText(dock, listed);
            this.ResultsDisplay  = this.BuildResultsText(dock, listed);

            this.Changed(nameof(this.DroneStatus));
            this.Changed(nameof(this.AreasDisplay));
            this.Changed(nameof(this.AssignedArea));
            this.Changed(nameof(this.ViewingDisplay));
            this.Changed(nameof(this.ResultsDisplay));
            this.Changed(nameof(this.ViewPosition));
        }

        // --- Text ---

        private static string BuildDroneStatus(DroneDockObject dock)
        {
            var drone = dock.SpawnedDrone;
            if (drone == null || drone.IsDestroyed)
                return dock.AssignedSurveyAreaId != 0
                    ? "none docked -- build and dock one to start surveying"
                    : "none docked";

            // R12: a stopped dock names the reason, and each reason is distinct from "nobody
            // assigned an area". Without this the three shortages and an idle dock all read the
            // same, and the player has no way to tell which one to go fix.
            switch (dock.StopReason)
            {
                case DockStopReason.NoFuel:      return "stopped -- out of fuel";
                case DockStopReason.BrokenParts: return "stopped -- a dock part is broken";
                case DockStopReason.BrokenDrone: return "stopped -- the drone needs repair";
            }

            if (!drone.TryGetComponent<DroneLifecycle>(out var lifecycle))
                return "docked";

            // A drone that has given up reaching its area is parked, so its status enum reads
            // Idle -- which is the physical truth and a useless thing to tell the player. Say the
            // useful half instead; the enum keeps the other one.
            if (lifecycle.CannotReachAssignedArea)
                return "docked -- cannot reach the assigned area";

            // Words, not the enum. "EnRoute" and "OnStation" name states in a state machine; they
            // tell someone watching a drone nothing about what it is doing or whether to step in.
            // "surveying" rather than the shared "at the area", because on a survey dock arriving
            // and working are the same thing.
            var travel = DockReadout.FormatTravel(lifecycle.Status, lifecycle.TravelTarget, atAreaLabel: "surveying");

            // A docked drone with an area still assigned has not finished -- it is between plots,
            // or waiting to set out -- and saying only "docked" invites the player to reassign it.
            if (travel == DockReadout.DockedPhrase && dock.AssignedSurveyAreaId != 0)
                return "docked -- waiting to set out";

            return travel;
        }

        /// <summary>True when this dock's drone is currently reporting that it cannot reach its area.</summary>
        private static bool DroneReportsUnreachable(DroneDockObject dock)
        {
            var drone = dock.SpawnedDrone;
            return drone != null
                   && !drone.IsDestroyed
                   && drone.TryGetComponent<DroneLifecycle>(out var lifecycle)
                   // Either actively failing to get there, or parked having given up. Both are
                   // "the drone cannot reach this area", and only counting the first made the
                   // marker blink in time with the retry loop.
                   //
                   // Fully qualified: this class has its own string property called DroneStatus,
                   // which shadows the enum type of the same name.
                   && (lifecycle.Status == AdvancedElectronics.Navigation.DroneStatus.Unreachable
                       || lifecycle.CannotReachAssignedArea);
        }

        private string BuildAreasText(DroneDockObject dock, List<SurveyAreaEntry> listed)
        {
            if (listed.Count == 0)
                return "No survey areas yet. Use Manage Areas on Map to draw your first one.";

            // Hoisted once for the whole roster: this runs off the dock's tick, and collecting it
            // per area would put an O(areas x world objects) sweep on a repeating path.
            var exclusionHolders = DroneDockObject.DocksHoldingExclusions();

            // And the overlap projections likewise, for the same reason -- read RAW (R34), so the
            // overlay sees past both the owner test and the dock-network radius.
            var published = MiningComponent.AllAreaProjections();

            var sb = new StringBuilder();
            var position = 1;
            foreach (var area in listed)
                sb.Append(DockReadout.FormatAreaLine(Snapshot(area, position++, dock, exclusionHolders, published))).Append('\n');

            return DockReadout.AtReadableSize(sb.ToString());
        }

        private static string BuildAssignedText(DroneDockObject dock, List<SurveyAreaEntry> listed)
        {
            var area = dock.AssignedSurveyArea;
            if (area == null) return "none -- select an area below, then Assign Selected Area";

            // The position as THIS TAB counts rows, not as the dock stores them: the collection
            // holds the farms too since U3, so the stored index would name a different row than
            // the one the player is reading.
            var position = listed.IndexOf(area) + 1;
            return $"{position} -- {area.Name}";
        }

        private string BuildViewingText(DroneDockObject dock, List<SurveyAreaEntry> listed)
        {
            var area = this.ViewedArea(listed);
            if (area == null) return "no areas yet -- draw one on the map";

            return DockReadout.FormatViewingLine(
                Snapshot(area, this.viewIndex + 1, dock), listed.Count);
        }

        private string BuildResultsText(DroneDockObject dock, List<SurveyAreaEntry> listed)
        {
            this.ApplyPickerSelection(dock);

            var entry = this.ViewedArea(listed);
            if (entry == null)
                return "Draw an area on the map, then select it above and click Assign Selected Area.";

            var sb = new StringBuilder();

            // Findings persist with the area (KTD11): these are entry's own, kept until it is edited
            // or deleted -- shown even while the drone is between areas or docked. The material
            // filter narrows what is DISPLAYED; everything stays recorded.
            var all = entry.ReadFindings().Where(f => f.Found).ToList();
            var findings = all
                .Where(f => dock.IsMaterialShown(f.OreType))
                .OrderByDescending(f => f.Count)
                .ToList();

            if (findings.Count == 0 && all.Count > 0)
            {
                sb.Append("No matching materials in this area -- clear the Material Targets picker above to show everything found.\n");
            }
            else if (findings.Count == 0)
            {
                sb.Append(EmptyFindingsMessage(entry)).Append('\n');
            }
            else
            {
                foreach (var f in findings)
                    sb.Append(DockReadout.FormatOreLine(f, SurveyMaterials.IconItemName(f.OreType))).Append('\n');
                sb.Append("Coverage: ").Append(entry.CoveragePercent.ToString("F0")).Append("%\n");
            }

            if (entry.SurveyDepth > 0)
                sb.Append("Scanned to ").Append(entry.SurveyDepth)
                  .Append(" blocks below surface; median surface level ").Append(entry.MedianSurface).Append(".\n");

            if (dock.MaterialFilter.Count > 0)
                sb.Append("Filtered to: ").Append(string.Join(", ", dock.MaterialFilter)).Append('\n');

            // Sized as one block rather than per line: the markup nests across newlines, and a
            // single wrap cannot leave a line behind at the default size the way N wraps can.
            return DockReadout.AtReadableSize(sb.ToString());
        }

        /// <summary>
        /// Coverage-aware message when the viewed area has no findings. Distinguishes "not started"
        /// from "in progress" from "fully covered, nothing here" -- so a finished-but-empty survey no
        /// longer tells the player to keep waiting, which never reveals anything new.
        /// </summary>
        private static string EmptyFindingsMessage(SurveyAreaEntry entry)
        {
            if (entry.CoveragePercent <= 0f)
                return "Not surveyed yet. Set the assign number above to this area's position.";
            if (entry.CoveragePercent >= 99.5f)
                return "Survey complete -- nothing found in this area.";
            return $"Surveyed {entry.CoveragePercent:F0}% so far -- nothing found yet.";
        }

        private SurveyAreaEntry ViewedArea(List<SurveyAreaEntry> listed) =>
            this.viewIndex >= 0 && this.viewIndex < listed.Count ? listed[this.viewIndex] : null;

        // The areas THIS TAB shows -- everything the dock holds that is not a farm (U10, KTD7)
        // -- used to be a private copy of the filter declared here, verbatim beside the picker's
        // own copy of it. It is DroneDockObject.SurveyKindAreas now, the mirror of FarmingAreas,
        // so the tab and its picker cannot drift into listing different rows. The clamp it feeds
        // is DockReadout.ClampCursor, unit-tested in the navigation assembly.

        // ---------------------------------------------------------------
        // U11: changing what an area is FOR (R30, R31, R32).
        //
        // The refusal logic lives here, on the tab that owns survey areas. The INVOCATION does
        // not: it is `/drone areakind`, and there is deliberately no RPC beside it. A commit
        // control is a BigButton, ~3.2 standard rows with two-thirds of the width dead, and this
        // tab already declares three against a stated budget of one (KTD10, and
        // docs/solutions/design-patterns/vertical-stack-only-ui-design.md). Repurposing an area
        // is a rare act under R31 -- rare enough that a command is the right home for it rather
        // than a compromise, and this plan does not make the row budget worse.
        // ---------------------------------------------------------------

        /// <summary>
        /// Changes what <paramref name="area"/> is for (R30, R31, R32), or refuses and says why.
        ///
        /// <para>
        /// <b>Two gates, and neither is an authorization check.</b> R32's "explicit" means the
        /// change never happens as a side effect of other work -- not that it carries a
        /// permission level of its own -- so nothing here re-tests authorization. Reaching this
        /// at all already required full access on the dock, and adding a second,
        /// differently-worded auth check beside the one the caller passed would be a rule nobody
        /// wrote down.
        /// </para>
        /// <para>
        /// The first gate is R11's: no drone may be working this area or one overlapping it. The
        /// second is the claim test (U9), because a kind change is an assignment-time act -- it
        /// can take ground from another dock exactly the way an assignment can, and R11 cannot
        /// see that case because a dock between passes is assigned without working. The comment
        /// at the test says why each direction refuses what it does.
        /// </para>
        /// <para>
        /// <b>Seam.</b> Eco-coupled and carrying no unit test: the world walk, the working-drone
        /// enumeration and the auth-gated refusal wording are all on this side of the boundary,
        /// and the test project references the navigation assembly alone. The refusal text and
        /// the overlap decision are covered there by <c>AreaClaimTests</c>; the wiring is proven
        /// in the batched live session of U12.
        /// </para>
        /// <para>
        /// <b>Nothing is discarded.</b> The whole change is one field write. The area's findings,
        /// its mined stamps and its exclusions survive exactly as R20 preserves them across an
        /// edit -- and that is precisely why R46 has to be structural: repurposed ground still
        /// carries every input the mining ladder reads, all of it still true about its past.
        /// <see cref="SurveyAreaEntry.Kind"/> is what stops the ladder being asked.
        /// </para>
        /// </summary>
        /// <param name="dock">The dock that owns <paramref name="area"/>.</param>
        /// <param name="actingCitizen">
        /// Whoever is operating the dock. Used ONLY to decide how much of a foreign area a
        /// refusal may name (R36, R42) -- never as a gate.
        /// </param>
        /// <param name="refusalReason">
        /// Why the change was refused, in the same shape the assign path already returns
        /// (KTD10); null on success.
        /// </param>
        public static bool ChangeAreaKind(
            DroneDockObject dock,
            SurveyAreaEntry area,
            AreaKind kind,
            User actingCitizen,
            out string refusalReason)
        {
            refusalReason = null;

            if (dock == null || area == null)
            {
                refusalReason = "that area is gone";
                return false;
            }

            // R9/R12. An assigned area cannot change what it is for. Changing the kind under a
            // live claim would leave the area holding ground as an assignment nobody made: the
            // claim records the work value of the OLD kind, so the area drops out of the new
            // kind's assigned list while still reading HasClaim. Releasing the claim silently
            // is the other way out and is worse -- it stops a drone by a side effect of an act
            // the player made for a different reason.
            //
            // Refusing keeps the two steps the owner described: unassign the farm, then turn the
            // area into a mining area. Nothing the area recorded is lost by waiting (R10), and
            // the drone-activity refusal below still covers the narrower mid-pass case for an
            // OVERLAPPING area, which no unassign of this one would clear.
            if (area.HasClaim)
            {
                refusalReason =
                    $"'{area.Name}' is assigned -- unassign it first, then change what it is for";
                return false;
            }

            foreach (var busy in AreasUnderAWorkingDroneNow())
            {
                var isThisArea = ReferenceEquals(busy.Area, area)
                                 || (busy.Owner.ObjectID == dock.ObjectID && busy.Area.Id == area.Id);

                // Overlap is tested against the OTHER area only when it is not this one, so an
                // area never reports as overlapping itself.
                if (!isThisArea && !Overlaps(area, busy.Area)) continue;

                refusalReason = isThisArea
                    ? $"'{busy.Dock.Name}' has a drone working this area right now -- recall it, or wait for the pass to finish"
                    : $"a drone from '{busy.Dock.Name}' is working {DescribeForRefusal(busy.Owner, busy.Area, actingCitizen)}, which covers some of the same ground -- recall it, or wait for the pass to finish";
                return false;
            }

            // ---------------------------------------------------------------
            // U9: the kind change takes the claim test too, because a kind change IS an
            // assignment-time act (Key Decision) -- it can create a conflict exactly the way an
            // assignment can.
            //
            // The loop above is R11 and it is not enough on its own. R11 refuses only while a
            // drone is WORKING, and DroneIsWorking is false for an assigned dock whose drone is
            // sitting docked between passes -- which is most of the time. Without this test,
            // turning a mine's neighbour into farmland leaves that assigned mine holding
            // farmland until the next restart, and undoing exactly that state is what
            // reconciliation exists for. A rule the mod repairs at load and does not enforce at
            // the act is a rule the player only meets as a surprise.
            //
            // The claimant kind is the NEW kind, not the old one: the question is what this
            // ground would be for after the change, and both directions are answered by it. To
            // mining, an overlapping farm on another dock is reserved ground (R2, R47). To
            // farming, an overlapping mining area that still HOLDS its claim is held ground
            // (R2) -- and one that does not is free, which is R4's asymmetry running the one
            // way it is meant to.
            //
            // Same-dock pairs are exempt inside AreaClaims.Conflicts (R7a) and are deliberately
            // not re-tested here. Nothing is passed for the already-holds predicate either: a
            // kind change is not this dock re-dispatching onto its own standing claim, so there
            // is no claim of its own to lift, and refusing is the conservative answer where the
            // two differ.
            // ---------------------------------------------------------------
            // The scan and the write go under the one lock together, exactly as the three
            // assignment paths do. This method has never locked, and while it only WROTE the
            // kind that was survivable -- a torn read of one enum is not a claim decision. It
            // stopped being survivable when the scan above it was added: a scan that decides
            // whether ground is free, followed by an unlocked write, is the same read-then-act
            // window AreaClaimLock exists to close, and a concurrent assignment on another dock
            // can slip between the two and claim the very ground this scan just found clear.
            //
            // So the lock is not tidying up an old omission -- the omission only became a race
            // when this method started asking about claims.
            lock (DroneDockObject.AreaClaimLock)
            {
                var conflicts = AreaClaims.Conflicts(
                    kind,
                    MiningComponent.OverlapsOf(dock, area, MiningComponent.AllAreaProjections()));

                if (conflicts.Count > 0)
                {
                    // The same formatter the two assignment paths refuse with, for the same reason
                    // reconciliation reuses it: one rule, one wording. It names plot coordinates and
                    // the act that lifts them, never the other area or its owner, so this refusal
                    // discloses no more about foreign ground than an assignment refusal already does
                    // (R20).
                    refusalReason = MiningReadout.FormatClaimRefusal(conflicts, PlotUtil.PropertyPlotLength);
                    return false;
                }

                area.Kind = kind;

                // R6 and R16. The ground has a new purpose, so whatever reconciliation recorded
                // about the assignment it undid has stopped describing this area -- and a farm that
                // is no longer a farm must not keep reporting a farm stall. The seam clears both,
                // and clears the farm stall only while it is still the one that record wrote.
                area.ClearReconciliationBlock();
            }
            return true;
        }

        /// <summary>
        /// Every (dock, owning dock, area) an actively working drone is on right now -- the
        /// survey area a dock is sweeping, the mining area a dock is consuming, and every
        /// farming area a dock is assigned to, since any of those passes is one R11 refuses to
        /// change the ground out from under.
        ///
        /// <para>
        /// <b>Farming was missing, and that was the whole of R11's blindness (U9).</b> This
        /// yielded the two single-area assignments only, so a drone ploughing a field was a
        /// drone this enumeration could not see, and the ground under it could be repurposed
        /// mid-pass. A farm is now an ordinary area carrying a kind (R17), so it belongs in the
        /// same enumeration as the other two rather than in a test of its own beside it.
        /// </para>
        /// <para>
        /// It is a LOOP where the other two are single reads, and that asymmetry is real: a dock
        /// holds one survey assignment and one mining assignment, but any number of assigned
        /// farm areas. A fix written against "the area a farming drone is working" would have
        /// covered whichever one the drone happened to be on and left the rest exposed.
        /// </para>
        ///
        /// <para>
        /// "Mid-pass" is <c>DroneIsWorking</c>, the one definition of working the fuel, wear and
        /// Operating channels already share, so this refusal cannot drift from what the panel
        /// says the drone is doing. A pass that STOPPED part-way leaves
        /// <see cref="SurveyAreaEntry.SweepInProgress"/> set with no drone in the air; that is a
        /// resumable record, not a drone mid-pass, and it does not block a change the player is
        /// deliberately asking for.
        /// </para>
        /// <para>
        /// Enumerated raw over every dock in the world, deliberately past both the owner filter
        /// and the dock-network radius (KTD8): a drone working ground that overlaps this area is
        /// a real collision however far apart the two docks sit and whoever owns them. What the
        /// refusal may SAY about a foreign area is gated separately, in
        /// <see cref="DescribeForRefusal"/>.
        /// </para>
        /// </summary>
        private static IEnumerable<(DroneDockObject Dock, DroneDockObject Owner, SurveyAreaEntry Area)> AreasUnderAWorkingDroneNow()
        {
            foreach (var obj in ServiceHolder<IWorldObjectManager>.Obj.All)
            {
                if (!(obj is DroneDockObject dock) || dock.IsDestroyed) continue;
                if (!dock.DroneIsWorking) continue;

                var surveyed = dock.AssignedSurveyArea;
                if (surveyed != null)
                    yield return (dock, dock, surveyed);

                var mining = dock.AssignedMiningArea;
                if (mining != null && mining.Resolve(out var sourceDock, out var minedArea) == AreaLookupSignal.Found)
                    yield return (dock, sourceDock, minedArea);

                // The owning dock is the working dock for every one of these: a farm area is
                // always drawn on the dock that farms it, so there is no published-elsewhere
                // case here of the kind the mining reference resolves.
                foreach (var farmed in dock.AssignedFarmingAreas)
                    yield return (dock, dock, farmed);
            }
        }

        /// <summary>
        /// True when the two areas cover any plot in common. Plot coordinates are world plots, not
        /// dock-local ones, so two areas from two different docks are directly comparable.
        ///
        /// <para>
        /// The test itself is <see cref="AreaOverlap"/>'s, in the pure assembly where it is unit
        /// tested; the local set intersection this replaces was a placeholder for exactly that.
        /// What is passed across is PLOT COORDINATES, never the entries: <c>busy.Area</c> may
        /// belong to another player, and the geometry API takes coordinates so that an entry
        /// carrying findings, stamps and exclusions cannot reach a surface meant only to say that
        /// two areas collide (R41).
        /// </para>
        /// </summary>
        private static bool Overlaps(SurveyAreaEntry a, SurveyAreaEntry b) =>
            a != null && b != null && AreaOverlap.Overlaps(a.Plots(), b.Plots());

        /// <summary>
        /// How much of a colliding area a refusal may name (R36, R42). Named in full when the
        /// citizen already has full access to the dock that published it; otherwise the refusal
        /// says the consequence and nothing about the area itself -- a dock placed near another
        /// player's ground grants no sight of it.
        /// </summary>
        private static string DescribeForRefusal(DroneDockObject owner, SurveyAreaEntry area, User actingCitizen) =>
            actingCitizen != null && owner != null && !owner.IsDestroyed && owner.HasFullAccess(actingCitizen)
                ? $"'{owner.Name} -- {area.Name}'"
                : "an area you do not have access to";

        /// <summary>
        /// Reduces an area to the Eco-free shape <see cref="DockReadout"/> formats. The material
        /// filter is applied HERE, not there: the formatter is handed the top finding the player can
        /// actually see, which is why "nothing matching" and "nothing found" collapse to one case.
        /// </summary>
        private static AreaSnapshot Snapshot(
            SurveyAreaEntry area,
            int position,
            DroneDockObject dock,
            IReadOnlyCollection<DroneDockObject> exclusionHolders = null,
            IReadOnlyList<AreaProjection> published = null)
        {
            var top = area.ReadFindings()
                .Where(f => f.Found && dock.IsMaterialShown(f.OreType))
                .OrderByDescending(f => f.Count)
                .FirstOrDefault();

            var isAssigned = area.Id == dock.AssignedSurveyAreaId;

            // Unreachable is a property of the trip, not the area, so only the assigned one can
            // report it -- and only while the drone that would make the trip actually says so.
            var isUnreachable = isAssigned && DroneReportsUnreachable(dock);

            // The area's own status (R3), derived from the shared record rather than from
            // anything this dock knows -- which is what makes the Mining tab's line agree.
            //
            // The area's KIND is what chooses which status is derived at all (R30, R46): pass it
            // in and a farming area reads [farm] with the mining ladder never consulted, even
            // though a repurposed one is still carrying every stamp and observation the ladder
            // would have read. The choice is made once, inside StatusOfArea; nothing here
            // overwrites one status with another.
            var status = DroneDockObject.StatusOfArea(dock.ObjectID, area, exclusionHolders, area.Kind);

            // R35/R36: this tab, like the Mining tab, says only THAT the area collides. The plots
            // it shares and whether the other area holds them go to the diagnostic command --
            // both lines come through the one annotation channel, so they cannot disagree.
            var hasOverlap = MiningComponent.OverlapsAnything(dock, area, published);

            return new AreaSnapshot(
                position, area.Name, area.PlotCount, area.CoveragePercent, top, status,
                isAssigned, isUnreachable, hasOverlap,
                // R8. While any plot of this area is recorded as needing re-reading, the readout
                // presents these figures under a label saying they are no longer current. The
                // figures themselves are not touched: they remain an accurate record of what the
                // survey pass found, so saying they are old is the honest correction rather than
                // altering them.
                needsResurvey: area.AnyPlotNeedsReReading,

                // R15. The Survey tab's roster is the surface that always shows an area, so it is
                // where the reason a load-time reconciliation undid an assignment has to read.
                // The Mining tab cannot carry it: after reconciliation the dock's assigned area is
                // null and the area no longer names its former holder, so that tab has no route
                // back to this record. Empty record, unchanged line.
                //
                // Guarded on the reason rather than handed straight to the formatter, which
                // early-exits on a null one and returns string.Empty -- the same value this
                // branch produces. Virtually every area has no record, and unflattening its
                // (empty) contested-plot list allocated per area per tick to reach that exit.
                reconciliationBlock: area.ReconciliationBlock == null
                    ? string.Empty
                    : MiningReadout.FormatReconciliationBlock(
                        area.ReconciliationBlock, area.ReconciliationBlockPlots().ToList(), PlotUtil.PropertyPlotLength));
        }

        // --- Material filter ---

        /// <summary>
        /// Projects the picker's current selection into the dock's serialized material filter, so the
        /// readout (and the chat command) work off one source. Maps a picked item type to the material
        /// name the sensor records, mirroring how that name is derived from a block type: strip the
        /// "Item" suffix, then a "Block" suffix if one remains ("IronOreItem" -> "IronOre",
        /// "LimestoneBlockItem" -> "Limestone", matching the sensor's "LimestoneBlock" -> "Limestone").
        /// </summary>
        private void ApplyPickerSelection(DroneDockObject dock)
        {
            var picked = PickedNames(this.MaterialTargets).Distinct().ToList();

            // Only rewrite when the selection actually differs, so the one-second refresh tick does
            // not fight a filter set from chat.
            if (picked.Count == dock.MaterialFilter.Count && picked.All(dock.MaterialFilter.Contains))
                return;

            dock.ClearMaterialFilter();
            foreach (var name in picked)
                dock.ToggleMaterialFilter(name);
        }

        /// <summary>The material names currently selected in the picker (empty when null or unset).</summary>
        private static IEnumerable<string> PickedNames(GamePickerList<BlockItem> picker) =>
            picker?.GetTypes().Select(t => MaterialNameFromItemType(t.Name)) ?? Enumerable.Empty<string>();

        /// <summary>Item type name -> the material name the sensor records. See <see cref="ApplyPickerSelection"/>.</summary>
        private static string MaterialNameFromItemType(string typeName)
        {
            var name = StripSuffix(typeName, "Item");
            return StripSuffix(name, "Block");
        }

        private static string StripSuffix(string value, string suffix) =>
            value.EndsWith(suffix, StringComparison.Ordinal) && value.Length > suffix.Length
                ? value.Substring(0, value.Length - suffix.Length)
                : value;
    }
}
