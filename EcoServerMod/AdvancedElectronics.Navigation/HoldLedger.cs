using System;

namespace AdvancedElectronics.Navigation
{
    /// <summary>Whether an unload attempt emptied the hold, moved part of it, or moved nothing.</summary>
    public enum UnloadOutcome
    {
        Full,
        Partial,
        Refused
    }

    /// <summary>One unload attempt's result: what moved, what is left, and the outcome it adds up to.</summary>
    public readonly struct UnloadPlan
    {
        public UnloadOutcome Outcome { get; }
        public int Moved { get; }
        public int Remaining { get; }

        private UnloadPlan(UnloadOutcome outcome, int moved, int remaining)
        {
            Outcome = outcome;
            Moved = moved;
            Remaining = remaining;
        }

        public static UnloadPlan Create(int holdQuantity, int moved)
        {
            if (moved < 0 || moved > holdQuantity)
                throw new ArgumentOutOfRangeException(nameof(moved));

            var remaining = holdQuantity - moved;
            var outcome = remaining == 0
                ? UnloadOutcome.Full // nothing left, whether because it all moved or there was nothing to move
                : moved == 0 ? UnloadOutcome.Refused : UnloadOutcome.Partial;
            return new UnloadPlan(outcome, moved, remaining);
        }
    }

    /// <summary>
    /// The hold's arithmetic, shared by the removal path (does this yield fit) and the
    /// unload path (what moves, what remains) so AE2 has one implementation, not two
    /// (KTD6, U7). Every quantity is a plain aggregate item count -- this type has no
    /// opinion about item types or engine stack limits; the Eco side (<see cref="CargoUnloader"/>)
    /// translates a real attempt's outcome into these numbers.
    /// </summary>
    public static class HoldLedger
    {
        /// <summary>
        /// Given the hold's current total quantity and how much of it a real unload
        /// attempt actually moved, reports the outcome (R27, R30): full when nothing is
        /// left, partial when some moved but not all, refused when nothing moved at all
        /// (covers zero destinations and a fully-refused push identically).
        /// </summary>
        public static UnloadPlan Plan(int holdQuantity, int moved) => UnloadPlan.Create(holdQuantity, moved);

        /// <summary>
        /// Whether the hold has room for one more yield of <paramref name="nextYield"/> items
        /// before it is full (U13's per-removal check -- the signal to head home rather than
        /// attempt a removal the hold cannot receive).
        /// </summary>
        public static bool HasRoomFor(int currentHoldQuantity, int holdCapacity, int nextYield) =>
            currentHoldQuantity + nextYield <= holdCapacity;
    }
}
