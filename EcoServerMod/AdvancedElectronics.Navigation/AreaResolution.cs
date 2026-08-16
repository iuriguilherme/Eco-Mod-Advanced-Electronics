using System;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// What the Eco-side lookup of a cross-dock area reference found, this tick (KTD2).
    /// Three outcomes, not two: a reference can dangle for reasons other than deletion
    /// (a redraw, or a load-ordering tick where the owning dock is not yet registered),
    /// and only genuine deletion should end a job.
    /// </summary>
    public enum AreaLookupSignal
    {
        /// <summary>The owning dock and the area both resolved.</summary>
        Found,

        /// <summary>Did not resolve this tick, but not confirmed gone -- retry silently.</summary>
        NotYetResolved,

        /// <summary>The owning dock or the area is confirmed destroyed/removed.</summary>
        ConfirmedGone
    }

    /// <summary>The pure policy's verdict on a cross-dock area reference (KTD2).</summary>
    public enum AreaResolutionOutcome
    {
        /// <summary>Still good -- continue the job.</summary>
        StillValid,

        /// <summary>Not resolved this tick -- retry silently, leave the job's reason untouched.</summary>
        NotYetResolved,

        /// <summary>Confirmed gone, or the reference's change token no longer matches -- end the job.</summary>
        Invalidated
    }

    /// <summary>
    /// Decides what a cross-dock area reference's resolution means for the job that
    /// depends on it (R6, R7, KTD2). Only the Eco-side lookup (does the dock exist, does
    /// the area still exist on it) stays outside this seam -- everything about what that
    /// lookup's outcome MEANS is decided here, testably.
    /// </summary>
    public static class AreaResolutionPolicy
    {
        /// <summary>
        /// Resolves <paramref name="signal"/> against the reference's own change-token
        /// check: a <see cref="AreaLookupSignal.Found"/> area whose current change token
        /// no longer matches <paramref name="storedChangeToken"/> invalidates the job the
        /// same way a redraw does, even though the area itself still exists.
        /// </summary>
        public static AreaResolutionOutcome Resolve(AreaLookupSignal signal, string storedChangeToken, string currentChangeToken)
        {
            switch (signal)
            {
                case AreaLookupSignal.ConfirmedGone:
                    return AreaResolutionOutcome.Invalidated;
                case AreaLookupSignal.NotYetResolved:
                    return AreaResolutionOutcome.NotYetResolved;
                case AreaLookupSignal.Found:
                    return string.Equals(storedChangeToken, currentChangeToken, StringComparison.Ordinal)
                        ? AreaResolutionOutcome.StillValid
                        : AreaResolutionOutcome.Invalidated;
                default:
                    throw new ArgumentOutOfRangeException(nameof(signal));
            }
        }
    }
}
