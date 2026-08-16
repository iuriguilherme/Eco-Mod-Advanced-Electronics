using AdvancedElectronics.Navigation;

namespace Eco.Mods.TechTree
{
    /// <summary>What one tick of parked work at a plot produced (KTD3's four-call contract).</summary>
    public enum ParkedWorkOutcome
    {
        /// <summary>The plot isn't finished -- call again next tick while still parked there.</summary>
        StillWorking,

        /// <summary>The strategy is done with this plot; the lifecycle should ask for the next target.</summary>
        PlotDone,

        /// <summary>The strategy abandoned this plot (already recorded internally); the lifecycle should ask for the next target.</summary>
        PlotFailed
    }

    /// <summary>
    /// The seam between the drone lifecycle (travel, parking, the return leg -- unchanged by
    /// job kind) and what a parked drone actually does (KTD3). The lifecycle is the only
    /// intended caller: it owns dispatch and travel, and reports an arrival failure to the
    /// strategy as a skip outcome rather than the strategy counting arrival attempts itself.
    /// </summary>
    public interface IJobStrategy
    {
        /// <summary>
        /// The plot to travel to/work next. False when there is nothing to target right now --
        /// check <see cref="IsComplete"/> to tell "not ready yet, retry next tick" (e.g. the
        /// assigned area hasn't resolved) from "no plots left, the job is done".
        /// </summary>
        bool TryGetNextTarget(out PlotCoord plot);

        /// <summary>True once every plot this strategy knows about has been worked or skipped.</summary>
        bool IsComplete { get; }

        /// <summary>
        /// True when this strategy has nothing left to do EVER, so the lifecycle may discard it and
        /// build a fresh one. Distinct from <see cref="IsComplete"/>, which also reads true for a
        /// temporary stop such as a full hold.
        ///
        /// Conflating the two discarded a mining strategy on every unload trip. The replacement
        /// rebuilt its ShaftPlan against the CURRENT surface -- by then the floor of the pit it had
        /// just dug -- so the drone started a second shaft at the bottom of the first, with no
        /// entrance, and each new plan opened with its own 3x3 layer over ground already cut 5x5.
        /// That is the alternating 3x3/5x5 layers, the skipped layers, the unmined 3x3 core inside
        /// a mined ring, and the plot that stopped at 5 instead of 15 (live pass #8).
        /// </summary>
        bool IsExhausted { get; }

        /// <summary>One tick of work while parked in the plot <see cref="TryGetNextTarget"/> last returned.</summary>
        ParkedWorkOutcome TickParkedWork();

        /// <summary>
        /// The current target could not be reached -- the drone's destination lookup failed, or
        /// the lifecycle's arrival-attempt cap was exceeded. The strategy records the outcome
        /// and advances past the plot; it does not count attempts itself.
        /// </summary>
        void OnArrivalFailed();

        /// <summary>The drone has physically arrived home. A hook for the strategy to act on (e.g. unload); a strategy with nothing to do here no-ops.</summary>
        void OnArrivedHome();

        /// <summary>The job ended for a reason external to the strategy's own plot accounting (area unassigned/gone, halted, ...). A hook to record it; a strategy with no ledger of its own no-ops.</summary>
        void OnEnded(string reason);
    }
}
