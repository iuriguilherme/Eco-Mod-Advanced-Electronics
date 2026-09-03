using System.Collections.Generic;
using System.Linq;
using AdvancedElectronics.Navigation;
using Eco.Shared.Voxel;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// The survey drone's job strategy (KTD3, U14): the plot-visiting and per-column-sweep
    /// behaviour <see cref="DroneLifecycle"/> ran inline before this unit, moved behind the
    /// <see cref="IJobStrategy"/> seam unchanged. Owns the plot list and its raster order,
    /// which plot is next, and one tick's work (sampling one row of the parked plot's
    /// columns) at that plot -- the lifecycle still owns travel to the plot centre and the
    /// arrival-attempt cap.
    ///
    /// Since U7 the sweep position is no longer purely local state. It is published to the
    /// dock's live <see cref="SurveyRecord"/> as it moves and persisted from there onto the
    /// area, and a strategy built for an area whose pass is already running starts from the
    /// stored cursor instead of the origin (R25) -- so a pass stopped by fuel, a reassignment
    /// or a restart resumes rather than re-flying the ground it already covered. The strategy
    /// object itself is still rebuilt on every dispatch and still holds no durable state.
    ///
    /// Never unit-tested (every collaborator is an Eco type) -- the regression gate is the
    /// existing suite staying green, the pure-library U7 coverage in <c>SurveyRecordTests</c>,
    /// and one live survey run: a pass stopped and restarted must finish without re-flying
    /// covered ground, and a resurvey must report only what the new pass observed.
    /// </summary>
    public sealed class SurveyStrategy : IJobStrategy
    {
        private readonly DroneDockObject homeDock;
        private readonly OreSensorComponent sensor;

        private List<PlotCoord> plots;
        private int plotIndex;
        private int columnCursor;

        public SurveyStrategy(DroneDockObject homeDock, OreSensorComponent sensor)
        {
            this.homeDock = homeDock;
            this.sensor = sensor;
        }

        public bool IsComplete => this.plots != null && this.plots.Count > 0 && this.plotIndex >= this.plots.Count;

        /// <summary>A survey sweep has no temporary stop -- finished is finished, so this matches <see cref="IsComplete"/> exactly.</summary>
        public bool IsExhausted => this.IsComplete;

        public bool TryGetNextTarget(out PlotCoord plot)
        {
            this.EnsureInitialized();

            plot = default;
            // Not yet resolved, or a degenerate empty area: nothing to target, and IsComplete
            // stays false, so the lifecycle just retries next tick rather than heading home --
            // matching the pre-cut behaviour exactly (an empty area never completed either).
            if (this.plots == null || this.plots.Count == 0)
                return false;
            if (this.plotIndex >= this.plots.Count)
                return false;

            plot = this.plots[this.plotIndex];
            return true;
        }

        public ParkedWorkOutcome TickParkedWork()
        {
            var plot = this.plots[this.plotIndex];
            var plotSide = PlotUtil.PropertyPlotLength;
            var total = PlotUtil.PropertyPlotArea;
            var baseX = plot.X * plotSide;
            var baseZ = plot.Z * plotSide;
            var record = this.homeDock.SurveyRecord;
            var areaId = this.homeDock.AssignedSurveyAreaId;

            for (var k = 0; k < plotSide && this.columnCursor < total; k++, this.columnCursor++)
            {
                var dx = this.columnCursor % plotSide;
                var dz = this.columnCursor / plotSide;
                this.sensor.SampleColumn(baseX + dx, baseZ + dz, record, areaId);
            }

            if (this.columnCursor < total)
            {
                this.PublishCursor();
                return ParkedWorkOutcome.StillWorking;
            }

            // KTD12: the surveyed stamp is written when a plot is swept, from the same
            // monotonic counter the mining dock's mined stamp draws from, so the two are
            // comparable (PlotFreshness.IsMineable) without any coordination between docks.
            this.homeDock.AssignedSurveyArea?.RecordSurveyedPlot(plot, (long)Eco.Simulation.Time.WorldTime.Seconds);

            this.Advance();
            return ParkedWorkOutcome.PlotDone;
        }

        public void OnArrivalFailed() => this.Advance();

        public void OnArrivedHome()
        {
            // Nothing to unload. What a survey drone does on arriving home is retire the
            // assignment it just finished, so a completed sweep does not sit assigned forever --
            // and, in particular, does not resume on the next server start as though there were
            // still work to do.
            //
            // Gated on the SWEEP being finished, not on coverage reaching 100%, and that
            // distinction is load-bearing. Coverage is sticky: findings belong to the area, not to
            // the drone's current target, so DroneDock.ClearSurveyData is called on delete and on
            // redraw but deliberately NOT on reassignment. An area swept once therefore reads 100%
            // forever. Retiring on coverage would make it impossible to re-survey: assigning it
            // again would unassign again on the next tick, and re-surveying is exactly what the
            // survey/mine/survey cycle needs in order to write fresh stamps and let mining go
            // another tier deeper.
            //
            // A sweep that finished with plots skipped as unreachable also counts as finished.
            // The coverage figure will show it fell short, which is the player's cue to act;
            // staying assigned would only mean retrying the same unreachable plots.
            if (this.IsComplete && this.homeDock.AssignedSurveyAreaId != 0)
            {
                // The sweep is finished, so the pass is closed (U7): the next dispatch on this
                // area is a NEW pass and must clear (R10), not resume into a completed one.
                this.homeDock.EndSurveyPass(this.homeDock.AssignedSurveyArea);
                this.homeDock.AssignSurveyArea(0);
            }
        }

        public void OnEnded(string reason)
        {
            // The survey drone keeps no job ledger of its own to close out.
        }

        private void EnsureInitialized()
        {
            if (this.plots != null)
                return;

            var entry = this.homeDock.AssignedSurveyArea;
            if (entry == null)
                return; // Retried next call -- plots stays null until the area resolves.

            // U7: opening the pass is what decides between a newly started resurvey (clears the
            // area's findings, live record and at-bedrock observations -- R10) and one resuming a
            // pass that stopped (keeps them, and the coverage they represent -- R25). The dock
            // owns that decision because it owns both the persisted area and the live record;
            // what comes back is where in the sweep to carry on from.
            var cursor = this.homeDock.StartOrResumeSurveyPass(entry);

            // Raster order (by Z then X) gives a stable, roughly lawn-mower visitation. The
            // resumed cursor indexes into THIS list, which is why a redraw clears the pass --
            // the same index would name a different plot.
            //
            // The order comes from SweepOrder rather than an OrderBy written here, because since
            // U8 a second caller depends on it meaning the same thing: an outside change resets
            // plots and has to rewind the cursor to the earliest of them, which it can only do by
            // knowing where in this exact order they sit.
            this.plots = SweepOrder.RasterOrder(entry.ToSurveyArea().EnumeratePlots()).ToList();

            // Clamped rather than trusted: a persisted cursor outliving a change to the plot list
            // must land the sweep somewhere real, and "past the end" is the finished sweep, which
            // sends the drone home rather than indexing off the list.
            this.plotIndex = System.Math.Clamp(cursor.PlotIndex, 0, this.plots.Count);
            this.columnCursor = this.plotIndex >= this.plots.Count
                ? 0
                : System.Math.Clamp(cursor.ColumnCursor, 0, PlotUtil.PropertyPlotArea);
        }

        private void Advance()
        {
            this.plotIndex++;
            this.columnCursor = 0;
            this.PublishCursor();
        }

        /// <summary>
        /// Hands the sweep's position to the live record, which is where the dock's next readout
        /// tick picks it up and persists it beside the samples it belongs to (U7, R25). The
        /// strategy is rebuilt on every dispatch, so the cursor cannot live here.
        /// </summary>
        private void PublishCursor()
        {
            var areaId = this.homeDock.AssignedSurveyAreaId;
            if (areaId != 0)
                this.homeDock.SurveyRecord.SetSweepCursor(areaId, this.plotIndex, this.columnCursor);
        }
    }
}
