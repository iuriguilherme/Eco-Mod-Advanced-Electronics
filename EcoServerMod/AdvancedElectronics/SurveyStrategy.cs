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
    /// Never unit-tested (every collaborator is an Eco type) -- the regression gate for this
    /// unit is the existing suite staying green (nothing here changed) plus one live survey
    /// run matching its pre-change behaviour.
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
                return ParkedWorkOutcome.StillWorking;

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
                this.homeDock.AssignSurveyArea(0);
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

            // Raster order (by Z then X) gives a stable, roughly lawn-mower visitation.
            this.plots = entry.ToSurveyArea().EnumeratePlots()
                .OrderBy(p => p.Z).ThenBy(p => p.X)
                .ToList();
            this.plotIndex = 0;
            this.columnCursor = 0;
        }

        private void Advance()
        {
            this.plotIndex++;
            this.columnCursor = 0;
        }
    }
}
