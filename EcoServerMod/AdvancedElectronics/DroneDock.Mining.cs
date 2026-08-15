using System.Collections.Generic;
using AdvancedElectronics.Navigation;
using Eco.Core.Utils;
using Eco.Gameplay.Auth;
using Eco.Gameplay.Players;
using Eco.Shared.IoC;
using Eco.Shared.Items;
using Eco.Shared.Serialization;
using Eco.Shared.SharedTypes;

namespace Eco.Mods.TechTree
{
    // Mining-specific dock state: U8 (cross-dock area reference, mined stamps), U12
    // (citizen stamp, re-check, full-access gate), and U9 (the mining job's persisted
    // ledger). Split from DroneDock.cs (maintainability) -- the mining surface is
    // self-contained enough to read on its own, and DroneDock.cs was crossing 1000 lines.
    public partial class DroneDockObject
    {
        // ---------------------------------------------------------------
        // U8: a mining dock's reference to an area published by a survey dock (KTD2), and
        // this dock's own per-plot mined stamps (KTD12). Only meaningful on a dock holding
        // a mining drone, but declared here rather than on the drone: the reference and
        // the mined ledger are dock-owned state, the same way SurveyAreas is.
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
        /// This dock's own mined stamps (KTD12), flattened as (x, z, stamp) triples --
        /// compared against the SOURCE area's surveyed stamps to decide which plots are
        /// mineable (<see cref="AdvancedElectronics.Navigation.PlotFreshness.IsMineable"/>).
        /// Deliberately not cleared on reassignment: a plot already mined stays recorded
        /// mined even if the dock is later pointed at a different area, since the mined
        /// stamp describes the WORLD position, not the assignment.
        /// </summary>
        [Serialized] public ThreadSafeList<long> MinedStamps { get; set; } = new();

        /// <summary>This dock's persisted mined stamps, rehydrated into a live accumulator.</summary>
        public PlotStampAccumulator ReadMinedStamps()
        {
            var entries = new Dictionary<PlotCoord, long>();
            for (var i = 0; i + 2 < this.MinedStamps.Count; i += 3)
                entries[new PlotCoord((int)this.MinedStamps[i], (int)this.MinedStamps[i + 1])] = this.MinedStamps[i + 2];
            return PlotStampAccumulator.FromSnapshot(entries);
        }

        /// <summary>Records <paramref name="plot"/> mined at <paramref name="stampValue"/> and persists it immediately -- unlike the survey side, there is no live/throttled projection step, since a mined stamp is written once per plot, not accumulated per column.</summary>
        public void RecordMinedPlot(PlotCoord plot, long stampValue)
        {
            var accumulator = this.ReadMinedStamps();
            accumulator.Record(plot, stampValue);

            var flat = new ThreadSafeList<long>();
            foreach (var entry in accumulator.Snapshot())
            {
                flat.Add(entry.Key.X);
                flat.Add(entry.Key.Z);
                flat.Add(entry.Value);
            }
            this.MinedStamps = flat;
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
        /// including to the same area. Refuses the whole call (no assignment, no stamp) if
        /// the acting citizen has a permission-ignoring tool selected (R37) or lacks full
        /// access on this dock (R40); returns whether it succeeded.
        /// </summary>
        public bool AssignMiningArea(DroneDockObject sourceDock, SurveyAreaEntry area, User actingCitizen)
        {
            if (area != null)
            {
                if (actingCitizen == null || actingCitizen.DevToolSelected || !this.HasFullAccess(actingCitizen))
                    return false;

                this.StampedCitizenName = actingCitizen.Name;
                this.StampedCitizenId = actingCitizen.Id;
            }

            this.AssignedMiningArea = area == null ? null : MiningAreaRef.For(sourceDock, area);
            this.miningAssignmentEpoch++;
            return true;
        }

        /// <summary>Clears this dock's mining area assignment (R7). The mined ledger, hold, and stamp are untouched.</summary>
        public void UnassignMiningArea()
        {
            this.AssignedMiningArea = null;
            this.miningAssignmentEpoch++;
        }

        /// <summary>
        /// Re-checks the stamped citizen against live access (KTD9 -- at each plot arrival,
        /// not once per dispatch): full access on this dock (R33, R40), and not a
        /// permission-ignoring tool now selected (R37). False ends the job.
        /// </summary>
        public bool RecheckStamp()
        {
            var citizen = this.StampedCitizen;
            return citizen != null && !citizen.DevToolSelected && this.HasFullAccess(citizen);
        }

        // ---------------------------------------------------------------
        // U9: the mining job's ledger lives with the dock, not the drone world object, so
        // it survives a drone despawn (U9's own reasoning). Persisted as a flattened
        // snapshot (U2's ToSnapshot/FromSnapshot), the live-accumulator-plus-snapshot
        // pattern the survey side already uses for findings.
        // ---------------------------------------------------------------

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
