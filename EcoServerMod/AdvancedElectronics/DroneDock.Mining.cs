using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedElectronics.Navigation;
using Eco.Core.Utils;
using Eco.Gameplay.Auth;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Shared.IoC;
using Eco.Shared.Items;
using Eco.Shared.Serialization;
using Eco.Shared.SharedTypes;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// One persisted attempt-fact exclusion (U3, R18, R19, KTD5): a plot ONE mining pass could
    /// not take, kept past the job's end so the next offer suppresses it again.
    ///
    /// It lives on the dock that hit the refusal, which is what makes its reach per dock without
    /// storing a holder: whoever reads it off this dock is reading that dock's own knowledge.
    /// The (source dock, area id) pair identifies the area, because an area id is dock-local —
    /// two survey docks each publish an area 1.
    ///
    /// A flat <c>[Serialized]</c> class of primitives inside a <see cref="ThreadSafeList{T}"/>,
    /// the shape <see cref="OreFindingSnapshot"/> already proves: the refusal wording is a string,
    /// so the flattened-into-one-primitive-list form the coordinate lists use cannot carry it.
    /// Every serialized member has a setter — a computed one stops the mod loading, with a clean
    /// build and a silent log (docs/solutions/conventions/serialized-needs-a-member-to-write-back-into.md).
    /// </summary>
    [Serialized]
    public class MiningExclusionEntry
    {
        /// <summary>Object id of the survey dock that published the area, stringified — an area id alone is dock-local.</summary>
        [Serialized] public string SourceDockId { get; set; }

        [Serialized] public int AreaId { get; set; }

        [Serialized] public int PlotX { get; set; }

        [Serialized] public int PlotZ { get; set; }

        /// <summary><see cref="SkipCategory"/>'s ordinal — the mining job's own vocabulary, not a second one (KTD5).</summary>
        [Serialized] public int CategoryValue { get; set; }

        /// <summary>The engine's own refusal wording (R27), or null when the refusal had none.</summary>
        [Serialized] public string Detail { get; set; }

        /// <summary>Parameterless constructor required by the Eco serializer.</summary>
        public MiningExclusionEntry() { }

        public PlotCoord Plot => new PlotCoord(this.PlotX, this.PlotZ);

        public bool IsFor(string sourceDockId, int areaId) =>
            this.AreaId == areaId && this.SourceDockId == sourceDockId;
    }

    // Mining-specific dock state: U8 (cross-dock area reference, mined stamps), U12
    // (citizen stamp, re-check, full-access gate), and U9 (the mining job's persisted
    // ledger). Split from DroneDock.cs (maintainability) -- the mining surface is
    // self-contained enough to read on its own, and DroneDock.cs was crossing 1000 lines.
    public partial class DroneDockObject
    {
        // ---------------------------------------------------------------
        // U8: a mining dock's reference to an area published by a survey dock (KTD2).
        // Only meaningful on a dock holding a mining drone, but declared here rather than
        // on the drone: the reference is dock-owned state, the same way SurveyAreas is.
        //
        // The per-plot MINED stamps used to live here too. They do not any more (U2, R1):
        // they are the area's record now, beside its surveyed stamps, so a block one dock
        // digs is dug for every dock that can see the area. What stays per dock is the
        // mining JOB and its ledger (R2), further down this file.
        // ---------------------------------------------------------------

        /// <summary>The area this mining dock currently consumes, or null when unassigned.</summary>
        [Serialized] public MiningAreaRef AssignedMiningArea { get; set; }

        // Bumped on every AssignMiningArea call (U10), so the lifecycle's change-detection
        // token changes even when the SAME area is reassigned -- mirroring
        // assignedAreaEpoch's own reasoning for a redraw. A fresh token is what starts a
        // fresh MiningJob rather than silently resuming a finished or stale one.
        [Serialized] private int miningAssignmentEpoch;

        /// <summary>Change-detection token for the mining assignment (U10), or null when unassigned.</summary>
        public string AssignedMiningAreaToken =>
            this.AssignedMiningArea == null ? null : $"mining:{this.AssignedMiningArea.AreaId}:{this.miningAssignmentEpoch}";

        /// <summary>
        /// Records <paramref name="plot"/> mined at <paramref name="stampValue"/> ON THE AREA
        /// THIS DOCK IS WORKING (U2, R1), resolved through the assignment's existing
        /// <see cref="MiningAreaRef"/> -- the same reference every dispatch already resolves.
        /// The dock keeps no mined list of its own: the stamp is a fact about the ground, so it
        /// belongs where every dock that can see that ground will read it, beside the area's
        /// surveyed stamps that <see cref="AdvancedElectronics.Navigation.PlotFreshness.IsMineable"/>
        /// compares it against.
        ///
        /// A no-op when the dock is unassigned or the area has gone: there is no ground record
        /// to write into, and a vanished area ends the job on the next tick anyway.
        /// </summary>
        public void RecordMinedPlot(PlotCoord plot, long stampValue)
        {
            if (this.AssignedMiningArea == null) return;
            if (this.AssignedMiningArea.Resolve(out _, out var area) != AreaLookupSignal.Found) return;

            area.RecordMinedPlot(plot, stampValue);
        }

        // ---------------------------------------------------------------
        // U3: the exclusion ledger's dock half (R18, R19, R27, KTD5).
        //
        // EVERY skip category a mining pass records is an ATTEMPT fact -- Unreachable,
        // Property, SettlementLaw, Obstructed and Other alike -- so the whole ledger belongs
        // here, on the dock that hit the refusals, and binds nobody else. A mining pass
        // records no ground facts at all: Obstructed is the classifier's catch-all for a
        // refusal that was neither law nor property (R7's single-column obstruction, which
        // never stops a plot reaching bedrock), and bedrock never reaches the ledger because
        // MiningStrategy filters NotRemovable positions out before submitting a layer.
        //
        // The one ground fact is the at-bedrock observation the survey pass writes per column
        // onto the AREA (U4, SurveyAreaEntry.BedrockPlotCoords). It is read here, never
        // duplicated here.
        // ---------------------------------------------------------------

        /// <summary>
        /// This dock's persisted attempt-fact exclusions, across every area it has worked
        /// (U3). Kept past the job that produced them, which is the whole point: a job ends,
        /// and the next offer still has to know which plots this dock was refused.
        ///
        /// NOT cleared by <see cref="UnassignMiningArea"/> -- walking away from an area does
        /// not unlearn what the drone was refused there. The ONE thing that clears these is the
        /// player assigning the area to this dock again (R45), which
        /// <see cref="AssignMiningArea(DroneDockObject, SurveyAreaEntry, User, out string)"/>
        /// does before the pass begins. Nothing else lifts one: only a mining drone can learn a
        /// refusal, by attempting the action in place, so only another attempt can learn it has
        /// gone -- and the player asking for that attempt IS the assignment.
        /// </summary>
        [Serialized] public ThreadSafeList<MiningExclusionEntry> MiningExclusions { get; set; } = new();

        /// <summary>This dock's identity in an exclusion ledger — which exclusions are its own to be bound by (R19).</summary>
        public string ExclusionHolderId => this.ObjectID.ToString();

        /// <summary>
        /// Persists <paramref name="job"/>'s skipped plots as this dock's exclusions on the
        /// area it is currently assigned to (R18, KTD5). Reads the job's own ledger — plot,
        /// category and the engine's refusal wording — so no second refusal vocabulary exists.
        ///
        /// Called at each skip rather than once at the job's end: the wording is captured while
        /// it is still in hand (the dock's flat int projection of the job cannot carry a string,
        /// so a restart would lose it), and a job that is never cleanly ended still leaves its
        /// record. Idempotent — re-persisting restates the same plots.
        ///
        /// A job that skipped nothing leaves nothing behind.
        /// </summary>
        public void PersistMiningExclusions(MiningJob job)
        {
            if (job == null) return;
            if (this.AssignedMiningArea == null) return;
            if (this.AssignedMiningArea.Resolve(out _, out var area) != AreaLookupSignal.Found) return;

            var skipped = job.SkippedPlots();
            if (skipped.Count == 0) return;

            var sourceDockId = this.AssignedMiningArea.OwningDockId.ToString();

            // Rebuilt rather than mutated in place: every other serialized collection here is
            // replaced wholesale on write, and an entry edited inside the list is the one shape
            // whose serializability is not already proven.
            var rebuilt = new ThreadSafeList<MiningExclusionEntry>();
            foreach (var existing in this.MiningExclusions)
                if (!(existing.IsFor(sourceDockId, area.Id) && skipped.Any(s => s.Plot.X == existing.PlotX && s.Plot.Z == existing.PlotZ)))
                    rebuilt.Add(existing);

            foreach (var skip in skipped)
                rebuilt.Add(new MiningExclusionEntry
                {
                    SourceDockId = sourceDockId,
                    AreaId = area.Id,
                    PlotX = skip.Plot.X,
                    PlotZ = skip.Plot.Z,
                    CategoryValue = (int)skip.Category,
                    Detail = skip.Detail,
                });

            this.MiningExclusions = rebuilt;
        }

        /// <summary>
        /// Adds this dock's exclusions for <paramref name="area"/> into <paramref name="ledger"/>,
        /// tagged with <see cref="ExclusionHolderId"/>.
        ///
        /// A pure read: it drops nothing and re-tests nothing. An exclusion is lifted only by
        /// this dock being assigned the area again (R45), never by anything a later survey
        /// observed -- a survey cannot test settlement law or property, and material standing
        /// at the plot proves nothing about a permit refusal, since the refusal left the
        /// material exactly where it was.
        ///
        /// Takes the ledger rather than returning one so a caller deriving the AREA's status can
        /// merge several docks' exclusions into a single ledger and read them all regardless of
        /// holder (R26) -- the split R19 draws is between what a dock is OFFERED and what the
        /// area reads, and only the offer side filters by holder.
        /// </summary>
        public void AddMiningExclusionsTo(MiningExclusionLedger ledger, Guid owningDockId, SurveyAreaEntry area)
        {
            if (ledger == null || area == null) return;

            var sourceDockId = owningDockId.ToString();
            foreach (var entry in this.MiningExclusions.Where(e => e.IsFor(sourceDockId, area.Id)))
                ledger.Record(new MiningExclusion(
                    this.ExclusionHolderId, entry.Plot, (SkipCategory)entry.CategoryValue, entry.Detail));
        }

        /// <summary>
        /// The exclusions in force on <paramref name="area"/> as this dock sees them: the area's
        /// own ground facts (U4's at-bedrock observation, binding every dock) unioned with this
        /// dock's surviving attempt facts. What <see cref="MiningExclusionLedger.SuppressesFor"/>
        /// then answers is which plots this dock is offered (R19).
        /// </summary>
        public MiningExclusionLedger ReadMiningExclusions(Guid owningDockId, SurveyAreaEntry area)
        {
            var ledger = new MiningExclusionLedger();
            if (area == null) return ledger;

            ledger.RecordGroundFacts(area.ReadBedrockPlots());
            this.AddMiningExclusionsTo(ledger, owningDockId, area);
            return ledger;
        }

        /// <summary>
        /// The exclusion set an area's STATUS is derived from (R26, U5 step 5): the area's own
        /// at-bedrock observations unioned with the attempt facts of EVERY dock in the world, not
        /// only the one doing the reading.
        ///
        /// <para>
        /// Deliberately not <see cref="ReadMiningExclusions"/>, which is the per-dock OFFER read.
        /// Derive the status from one dock's exclusions and the same area reads <c>[mined]</c> to
        /// that dock and <c>[cleared]</c> to its neighbour -- the cross-dock disagreement this
        /// whole model exists to remove. What varies per dock is which plots it is offered, never
        /// what the area says it is.
        /// </para>
        /// <para>
        /// Static because the answer must not depend on who asks. It reads the world-object
        /// enumeration rather than a registry the mod has deliberately never had.
        /// </para>
        /// </summary>
        /// <param name="exclusionHolders">
        /// The docks to collect attempt facts from -- <see cref="DocksHoldingExclusions"/>, hoisted
        /// by the caller. Null makes this collect them itself, which is right for a one-area read
        /// and wrong for a roster: a roster refresh runs off the dock's TICK, so re-walking every
        /// world object once per area would put an O(areas x world) sweep on a repeating path.
        /// </param>
        public static MiningExclusionLedger AssembleAreaExclusions(
            Guid owningDockId, SurveyAreaEntry area, IReadOnlyCollection<DroneDockObject> exclusionHolders = null)
        {
            var ledger = new MiningExclusionLedger();
            if (area == null) return ledger;

            ledger.RecordGroundFacts(area.ReadBedrockPlots());

            foreach (var dock in exclusionHolders ?? DocksHoldingExclusions())
                dock.AddMiningExclusionsTo(ledger, owningDockId, area);

            return ledger;
        }

        /// <summary>
        /// Every dock in the world carrying at least one attempt-fact exclusion -- the only docks
        /// <see cref="AssembleAreaExclusions"/> can learn anything from. Collected once per roster
        /// refresh and reused across its areas.
        /// </summary>
        public static IReadOnlyCollection<DroneDockObject> DocksHoldingExclusions() =>
            ServiceHolder<IWorldObjectManager>.Obj.All
                .OfType<DroneDockObject>()
                .Where(d => !d.IsDestroyed && d.MiningExclusions.Count > 0)
                .ToList();

        /// <summary>
        /// One area's status slot (R3, R30), read the same way by every surface that shows it --
        /// both roster lines and, when it lands, the offer test R44 defines. Computed on read and
        /// never stored (KTD3).
        ///
        /// <para>
        /// The KIND chooses first: a mining area runs the ladder, a farming area reads
        /// <c>[farm]</c> and the ladder is never consulted. That is why the ladder is passed to
        /// <see cref="AreaLifecycle.StatusFor"/> as a delegate rather than computed here -- for
        /// farmland this method does not even assemble the exclusion set, so there is no moment
        /// at which a mining status and <c>[farm]</c> both exist.
        /// </para>
        /// <para>
        /// Every area a survey dock owns is mining ground today, so <paramref name="kind"/>
        /// defaults accordingly. When kind moves onto the area itself, this parameter is the seam
        /// it fills -- and a repurposed area's surviving mined stamps and bedrock observations
        /// stop being read at that moment, rather than needing a render-order rule to hide them.
        /// </para>
        /// </summary>
        public static AreaLifecycleStatus StatusOfArea(
            Guid owningDockId,
            SurveyAreaEntry area,
            IReadOnlyCollection<DroneDockObject> exclusionHolders = null,
            AreaKind kind = AreaKind.Mining)
        {
            if (area == null) return AreaLifecycleStatus.Unsurveyed;

            return AreaLifecycle.StatusFor(kind, () =>
            {
                var surveyed = area.ReadSurveyedStamps();
                var mined = area.ReadMinedStamps();

                return AreaLifecycle.DeriveStatus(
                    area.ToSurveyArea().EnumeratePlots(),
                    surveyed.StampFor,
                    mined.StampFor,
                    AssembleAreaExclusions(owningDockId, area, exclusionHolders));
            });
        }

        /// <summary>
        /// Drops this dock's attempt-fact exclusions for one area (R45) -- what an assignment
        /// does before the pass begins, and the only thing that lifts one.
        ///
        /// Assignment is the retry. The mod is never told that a settlement claim lapsed or a
        /// property boundary moved: it does not poll and does not re-test permission, because
        /// only a mining drone can learn a refusal at all -- it attempts the action in place and
        /// captures the engine's reason. The player who wants the ground worked says so by
        /// assigning a drone to it, and that request is what wipes the slate.
        ///
        /// Scoped to this dock and this area, and to attempt facts only. Another dock's
        /// exclusions are its own knowledge and are untouched; another area's are untouched; and
        /// the area's ground facts are not this dock's to clear -- the survey re-derives
        /// at-bedrock from the ground on every pass, so they need no lift (AE5).
        /// </summary>
        public void ClearMiningExclusions(Guid owningDockId, int areaId)
        {
            var sourceDockId = owningDockId.ToString();
            if (!this.MiningExclusions.Any(e => e.IsFor(sourceDockId, areaId))) return;

            var survivors = new ThreadSafeList<MiningExclusionEntry>();
            foreach (var entry in this.MiningExclusions)
                if (!entry.IsFor(sourceDockId, areaId))
                    survivors.Add(entry);
            this.MiningExclusions = survivors;
        }

        /// <summary>True when <paramref name="citizen"/> holds full access on this dock (R39, R40) -- the level the dig-or-mine action itself declares, not the attribute default.</summary>
        public bool HasFullAccess(User citizen) =>
            citizen != null && ServiceHolder<IAuthManager>.Obj.IsAuthorized(this, citizen, AccessType.FullAccess, null, out _).Success;

        // ---------------------------------------------------------------
        // U12: the assignment's citizen stamp (R18, R33, R37, R40, KD10) -- who is
        // accountable for every removal this dock's mining job performs. A plain (name,
        // id) snapshot, not a live User reference (mirrors DroneOwnership's own reasoning:
        // a stale live reference would dangle across a session boundary).
        // ---------------------------------------------------------------

        [Serialized] public string StampedCitizenName { get; private set; }
        [Serialized] public int StampedCitizenId { get; private set; }

        /// <summary>The stamped citizen, re-resolved live every read, or null if never stamped or now offline/unknown.</summary>
        public User StampedCitizen => this.StampedCitizenId == 0 ? null : UserManager.FindUserByID(this.StampedCitizenId);

        /// <summary>
        /// Assigns this mining dock to consume <paramref name="area"/>, published by
        /// <paramref name="sourceDock"/> (R2, R3, R5, KD15), and stamps <paramref name="actingCitizen"/>
        /// as the party accountable for it (R18, R40) -- re-stamping on every reassignment,
        /// including to the same area. Refuses the whole call (no assignment, no stamp) if the
        /// acting citizen lacks full access on this dock (R40) or on the dock that published
        /// the area (R39); returns whether it succeeded.
        /// </summary>
        public bool AssignMiningArea(DroneDockObject sourceDock, SurveyAreaEntry area, User actingCitizen) =>
            this.AssignMiningArea(sourceDock, area, actingCitizen, out _);

        /// <inheritdoc cref="AssignMiningArea(DroneDockObject, SurveyAreaEntry, User)"/>
        /// <param name="refusalReason">Why the call was refused, for the caller to show; null on success.</param>
        public bool AssignMiningArea(DroneDockObject sourceDock, SurveyAreaEntry area, User actingCitizen, out string refusalReason)
        {
            refusalReason = null;

            if (area != null)
            {
                if (actingCitizen == null)
                {
                    refusalReason = "no acting citizen";
                    return false;
                }

                if (!this.HasFullAccess(actingCitizen))
                {
                    refusalReason = "you need full access on this mining dock";
                    return false;
                }

                // R39's other half, and the one that was missing: full access on the dock that
                // PUBLISHED the area, not just on the one consuming it. Without this, holding your
                // own mining dock was enough to send it mining an area drawn by a survey dock you
                // have no access to -- the mining dock's own auth said yes, and nothing ever asked
                // the source. Live pass #1 confirmed the drone flew there and worked.
                //
                // Checked here rather than in the RPC because this is the state operation every
                // caller goes through; a gate on one caller is a gate one new caller forgets.
                if (sourceDock == null || sourceDock.IsDestroyed)
                {
                    refusalReason = "that area's survey dock is gone";
                    return false;
                }

                if (!sourceDock.HasFullAccess(actingCitizen))
                {
                    refusalReason = $"you need full access on '{sourceDock.Name}', the dock that surveyed this area";
                    return false;
                }

                this.StampedCitizenName = actingCitizen.Name;
                this.StampedCitizenId = actingCitizen.Id;

                // R45: assignment IS the retry, so it lifts this dock's exclusions on this area
                // before the pass begins. Only a mining drone can learn a refusal -- it attempts
                // the action in place and captures the engine's reason -- so only another attempt
                // can learn the refusal has gone, and the player asking for that attempt is the
                // only signal the mod gets. Nothing here polls, re-tests a permit, or asks a
                // survey to answer for one.
                //
                // Placed after every gate above, so a REFUSED assignment lifts nothing: the whole
                // call is meant to be a no-op on refusal, and clearing early would let a player
                // with no access wipe a record by trying.
                //
                // Scoped to this dock and this area. Another dock's exclusions on the same area
                // stand -- they are its own knowledge -- and so do this dock's on other areas.
                this.ClearMiningExclusions(sourceDock.ObjectID, area.Id);
            }

            this.AssignedMiningArea = area == null ? null : MiningAreaRef.For(sourceDock, area);
            this.miningAssignmentEpoch++;
            return true;
        }

        /// <summary>
        /// Clears this dock's mining area assignment (R7). The hold and the citizen stamp are
        /// untouched, and so is the area's mined record -- which the dock never owned and cannot
        /// drop by walking away from the area (U2, R1).
        /// </summary>
        public void UnassignMiningArea()
        {
            this.AssignedMiningArea = null;
            this.miningAssignmentEpoch++;

            // A job outlives its assignment otherwise, and the panel keeps reporting "working"
            // at whatever plot count it had reached while the drone flies home to nothing.
            this.MiningJob?.End(MiningEndReason.Unassigned);
            this.PersistMiningJob();
        }

        /// <summary>
        /// Re-checks the stamped citizen against live access (KTD9 -- at each plot arrival,
        /// not once per dispatch): a citizen is stamped, and still holds full access on this
        /// dock (R33, R40). False ends the job.
        ///
        /// R37's permission-ignoring-tool test used to live here and was retired: what the
        /// owner happens to be holding is not a property of the drone. The removal pack names
        /// the Mining Arm as its tool and never reads the player's hands, so a dev tool could
        /// not reach the drone's actions in the first place -- the test guarded a path that
        /// does not exist, while being able to stop a job from anywhere in the world the
        /// moment the owner picked something up.
        /// </summary>
        public bool RecheckStamp() => this.StampRefusalReason() == null;

        /// <summary>
        /// As <see cref="RecheckStamp"/>, but names why it failed so the job can end with a
        /// reason the panel can print rather than stopping silently.
        /// </summary>
        public MiningEndReason? StampRefusalReason()
        {
            var citizen = this.StampedCitizen;
            if (citizen == null) return MiningEndReason.StampInvalid;
            return this.HasFullAccess(citizen) ? null : MiningEndReason.StampInvalid;
        }

        // ---------------------------------------------------------------
        // U9: the mining job's ledger lives with the dock, not the drone world object, so
        // it survives a drone despawn (U9's own reasoning). Persisted as a flattened
        // snapshot (U2's ToSnapshot/FromSnapshot), the live-accumulator-plus-snapshot
        // pattern the survey side already uses for findings.
        // ---------------------------------------------------------------

        // ---------------------------------------------------------------
        // The pass in progress on one plot. Held here rather than on the strategy because the
        // strategy is a live object rebuilt from scratch on every load, while a half-cut shaft
        // is exactly the state that must outlive one.
        //
        // Only the FLOOR is kept, not the plan. A pass is re-planned against whatever ground is
        // there now -- that is the intended behaviour, and it is what lets a second pass start
        // from a pit floor -- so the single thing a re-plan cannot re-derive is how deep this
        // pass was already committed to going. Layout: [0] plot X, [1] plot Z, [2] floor Y.
        // ---------------------------------------------------------------

        [Serialized] private ThreadSafeList<int> passInProgress = new();

        /// <summary>Records the plot being cut and the Y its pass stops at.</summary>
        public void SavePassInProgress(PlotCoord plot, int floorY) =>
            this.passInProgress = new ThreadSafeList<int> { plot.X, plot.Z, floorY };

        /// <summary>Forgets the pass in progress -- the plot finished, or was skipped.</summary>
        public void ClearPassInProgress() => this.passInProgress = new ThreadSafeList<int>();

        /// <summary>The plot being cut and the floor its pass stops at, or false when there is none.</summary>
        public bool TryReadPassInProgress(out PlotCoord plot, out int floorY)
        {
            plot = default;
            floorY = 0;

            var flat = this.passInProgress;
            if (flat == null || flat.Count < 3) return false;

            plot = new PlotCoord(flat[0], flat[1]);
            floorY = flat[2];
            return true;
        }

        /// <summary>
        /// The area id the persisted job was built for, so a job is not resumed against a
        /// different area than it was created for -- its ledger is keyed to the old area's
        /// plots. Zero means "unknown", which is what a save written before this field
        /// existed carries; unknown resumes rather than discarding, since discarding a
        /// player's real progress is the worse of the two mistakes.
        /// </summary>
        [Serialized] public int MiningJobAreaId { get; set; }

        [Serialized] private int miningJobStatusValue = -1; // -1 = no job yet
        [Serialized] private int miningJobEndReasonValue = -1;
        [Serialized] private ThreadSafeList<int> miningJobLedger = new();

        private MiningJob liveMiningJob;

        /// <summary>The current mining job, rehydrated from the persisted snapshot on first access after load. Null until one is created.</summary>
        public MiningJob MiningJob
        {
            get
            {
                if (this.liveMiningJob == null && this.miningJobStatusValue >= 0)
                    this.liveMiningJob = this.RehydrateMiningJob();
                return this.liveMiningJob;
            }
            set => this.liveMiningJob = value;
        }

        /// <summary>Projects the live job onto its persisted snapshot fields. Called from the dock's own throttled tick.</summary>
        public void PersistMiningJob()
        {
            if (this.liveMiningJob == null) return;

            var snapshot = this.liveMiningJob.ToSnapshot();
            this.miningJobStatusValue = (int)snapshot.Status;
            this.miningJobEndReasonValue = snapshot.EndReason.HasValue ? (int)snapshot.EndReason.Value : -1;

            var flat = new ThreadSafeList<int>();
            foreach (var entry in snapshot.Ledger)
            {
                flat.Add(entry.Plot.X);
                flat.Add(entry.Plot.Z);
                flat.Add((int)entry.Outcome);
                flat.Add(entry.Category.HasValue ? (int)entry.Category.Value : -1);
            }
            this.miningJobLedger = flat;
        }

        private MiningJob RehydrateMiningJob()
        {
            var entries = new List<MiningJobSnapshot.LedgerEntry>();
            for (var i = 0; i + 3 < this.miningJobLedger.Count; i += 4)
            {
                var plot = new PlotCoord(this.miningJobLedger[i], this.miningJobLedger[i + 1]);
                var outcome = (PlotOutcome)this.miningJobLedger[i + 2];
                var categoryValue = this.miningJobLedger[i + 3];
                entries.Add(new MiningJobSnapshot.LedgerEntry(plot, outcome, categoryValue < 0 ? (SkipCategory?)null : (SkipCategory)categoryValue));
            }

            var status = (MiningJobStatus)this.miningJobStatusValue;
            var endReason = this.miningJobEndReasonValue < 0 ? (MiningEndReason?)null : (MiningEndReason)this.miningJobEndReasonValue;
            return MiningJob.FromSnapshot(new MiningJobSnapshot(status, endReason, entries));
        }
    }
}
