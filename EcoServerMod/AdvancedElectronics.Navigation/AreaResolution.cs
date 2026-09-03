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
        Invalidated,

        /// <summary>
        /// The area was edited under this reference, but the edit took nothing the job still has
        /// to work, so the job carries on over the plots the edit retained (U9, R21). The caller
        /// adopts the area's current change token and treats the reference as valid from here.
        ///
        /// <para>
        /// A separate member rather than folding into <see cref="StillValid"/>, because the
        /// caller has something to DO: a reference left carrying the old token would re-decide
        /// this on every tick, and re-deciding is not free once the answer can be "end the job".
        /// </para>
        /// </summary>
        Reacquired
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

        /// <summary>
        /// As <see cref="Resolve(AreaLookupSignal, string, string)"/>, but narrowed by R21: a
        /// change token that moved because the area was EDITED only invalidates the job when the
        /// edit removed plots that job still has to work
        /// (<see cref="AreaEdit.RemovesPendingWork"/>). Otherwise the reference is
        /// <see cref="AreaResolutionOutcome.Reacquired"/> and the job runs on over the plots the
        /// edit retained.
        ///
        /// <para>
        /// The narrowing is on the change-token half only. A <see cref="AreaLookupSignal"/> of
        /// <see cref="AreaLookupSignal.ConfirmedGone"/> still ends the job whatever the job has
        /// left to do -- there is no area to run on -- so an area that vanished and an area that
        /// was reshaped stay different answers, which is why they were given different end
        /// reasons in the first place.
        /// </para>
        /// <para>
        /// Kept as an overload rather than a new parameter on the existing method, so the two
        /// callers that must agree opt in explicitly and a third that does not pass the test
        /// cannot silently inherit a laxer rule.
        /// </para>
        /// <para>
        /// The test is a DELEGATE rather than a bool because both callers sit on the drone's
        /// per-tick path and answering it means walking the job's ledger against the area's
        /// plots. An edit is a rare event; the overwhelmingly common answer is a token that
        /// matches, and that answer must stay free. The delegate is invoked at most once.
        /// </para>
        /// </summary>
        public static AreaResolutionOutcome Resolve(
            AreaLookupSignal signal, string storedChangeToken, string currentChangeToken,
            Func<bool> editRemovedPendingWork)
        {
            var outcome = Resolve(signal, storedChangeToken, currentChangeToken);

            if (outcome != AreaResolutionOutcome.Invalidated || signal != AreaLookupSignal.Found)
                return outcome;

            // A caller with nothing to ask is treated as having nothing pending: no job means no
            // work an edit could have taken, and the reference simply catches up with the area.
            return editRemovedPendingWork != null && editRemovedPendingWork()
                ? AreaResolutionOutcome.Invalidated
                : AreaResolutionOutcome.Reacquired;
        }
    }
}
