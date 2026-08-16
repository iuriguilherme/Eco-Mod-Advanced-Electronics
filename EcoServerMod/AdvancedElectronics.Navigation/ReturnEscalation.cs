using System;
using System.Collections.Generic;
using System.Linq;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// How hard the drone is currently willing to try, on its way back to the dock.
    /// Ordered loosest-last: each tier relaxes a constraint the previous one respected.
    /// </summary>
    public enum ReturnTier
    {
        /// <summary>Ordinary movement — the same constraints an outbound leg uses.</summary>
        Normal,

        /// <summary>Same pathing, but willing to climb considerably steeper terrain.</summary>
        HighClimb,

        /// <summary>Hover: fly over obstacles instead of walking around them.</summary>
        Hover,

        /// <summary>Clip: ignore obstruction entirely and take the straight line.</summary>
        Clip,

        /// <summary>Last resort — appear at the dock.</summary>
        Teleport,
    }

    /// <summary>One rung of the ladder: what the mover is allowed to do on this attempt.</summary>
    public readonly struct ReturnAttempt
    {
        public ReturnAttempt(ReturnTier tier, float maxStepHeight, bool ignoresObstacles, bool teleports, bool isFinal)
        {
            this.Tier             = tier;
            this.MaxStepHeight    = maxStepHeight;
            this.IgnoresObstacles = ignoresObstacles;
            this.Teleports        = teleports;
            this.IsFinal          = isFinal;
        }

        public ReturnTier Tier { get; }

        /// <summary>Climb height to build the pathfinder with for this attempt.</summary>
        public float MaxStepHeight { get; }

        /// <summary>True once the drone stops routing around obstruction and goes over or through it.</summary>
        public bool IgnoresObstacles { get; }

        /// <summary>True on the tier that gives up on movement and places the drone at the dock.</summary>
        public bool Teleports { get; }

        /// <summary>True when there is nothing further to escalate to.</summary>
        public bool IsFinal { get; }
    }

    /// <summary>
    /// The return-leg escalation ladder (R11): a drone recalled to its dock always gets home.
    ///
    /// A dock-bound leg that cannot path does not report Unreachable and stop, the way an
    /// outbound leg does. It climbs the ladder instead — higher climb, then hovering over
    /// obstacles, then clipping through them, then teleporting. Only the outbound legs still
    /// have a genuine failure state, because failing to reach a survey area is a normal
    /// gameplay outcome and failing to get home is not: the dock's own shortages are what
    /// trigger a recall, so a return leg that could strand would strand exactly the drone
    /// that had just been called back.
    ///
    /// Deliberately pure. No world state, no Eco types, no sampler — the tier sequence is a
    /// function of the current tier and nothing else, which is what makes it testable offline
    /// and what keeps "which rung are we on" out of the mover's movement code.
    /// </summary>
    public static class ReturnEscalation
    {
        /// <summary>
        /// The drone's everyday climb height. The first rung must match it, or every return leg
        /// would open by relaxing a constraint it had no reason to relax. DroneMoverComponent
        /// builds its ordinary pathfinder from this same value.
        ///
        /// This was 1f -- a WALKING entity's step, one block. The drone flies. That single
        /// mismatch was the whole of the "drone gets stuck" family: a shaft cut to the mining
        /// tier's 15 layers presents a 15-block step, so the machine that dug the hole could not
        /// path back into it, and the same trip that succeeded outbound failed on the return
        /// (live pass #7: "dispatched to area point 302,617" then "no path ... to area point
        /// 302,617"). It equally explains a freshly drawn area on a slope reading unreachable to
        /// the survey drone, and non-contiguous plots being skipped -- any terrain step over one
        /// block was a wall to a machine that hovers.
        ///
        /// 16f rather than a new number: it is the Hover rung's existing value, so the everyday
        /// capability now matches what the ladder already considered reasonable for this drone,
        /// and it clears MiningTierDepth (15) with a block to spare. Obstacle avoidance is
        /// untouched -- this changes how far the drone may climb or descend, not what it may pass
        /// through.
        /// </summary>
        public const float OrdinaryMaxStepHeight = 16f;

        private static readonly ReturnAttempt[] ladder =
        {
            // Rung heights re-based on the everyday value now that it reflects flight rather than
            // walking. HighClimb was 4 and Hover 16, both of which now sit at or under the
            // ordinary rung -- an "escalation" that climbed less than the drone already could.
            // The monotonic-ladder test caught exactly that, which is the test doing its job.
            new(ReturnTier.Normal,    OrdinaryMaxStepHeight,      ignoresObstacles: false, teleports: false, isFinal: false),
            new(ReturnTier.HighClimb, OrdinaryMaxStepHeight * 2f, ignoresObstacles: false, teleports: false, isFinal: false),
            new(ReturnTier.Hover,     OrdinaryMaxStepHeight * 4f, ignoresObstacles: true,  teleports: false, isFinal: false),
            new(ReturnTier.Clip,      float.MaxValue,        ignoresObstacles: true,  teleports: false, isFinal: false),
            new(ReturnTier.Teleport,  float.MaxValue,        ignoresObstacles: true,  teleports: true,  isFinal: true),
        };

        /// <summary>Every rung, loosest last.</summary>
        public static IReadOnlyList<ReturnAttempt> Ladder => ladder;

        /// <summary>The rung a fresh return leg starts on.</summary>
        public static ReturnAttempt First => ladder[0];

        /// <summary>The rung for a given tier.</summary>
        public static ReturnAttempt For(ReturnTier tier)
        {
            foreach (var attempt in ladder)
                if (attempt.Tier == tier) return attempt;

            throw new ArgumentOutOfRangeException(nameof(tier), tier, "not a rung of the return ladder.");
        }

        /// <summary>
        /// The next rung after <paramref name="current"/>, or false when there is none. False
        /// means the drone has exhausted the ladder — which, since the last rung teleports,
        /// can only happen after it is already home.
        /// </summary>
        public static bool TryNext(ReturnTier current, out ReturnAttempt next)
        {
            for (var i = 0; i < ladder.Length - 1; i++)
                if (ladder[i].Tier == current)
                {
                    next = ladder[i + 1];
                    return true;
                }

            next = default;
            return false;
        }
    }
}
