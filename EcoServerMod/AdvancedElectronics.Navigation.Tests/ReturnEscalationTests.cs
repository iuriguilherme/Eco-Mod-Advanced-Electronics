using System.Collections.Generic;
using System.Linq;
using AdvancedElectronics.Navigation;
using Xunit;

namespace AdvancedElectronics.Navigation.Tests
{
    /// <summary>
    /// The return-leg ladder (R11). A drone recalled to its dock must always get home, so a
    /// failed path escalates rather than reporting Unreachable: climb higher, then hover over
    /// obstacles, then clip through them, and teleport as the last resort.
    ///
    /// These are pure-policy tests. The ladder holds no world state and no Eco types, which is
    /// the whole reason it lives in the navigation project instead of the mover.
    /// </summary>
    public class ReturnEscalationTests
    {
        [Fact]
        public void Ladder_advances_through_every_tier_in_order_and_ends_final()
        {
            var visited = new List<ReturnTier>();
            var attempt = ReturnEscalation.First;

            visited.Add(attempt.Tier);
            while (ReturnEscalation.TryNext(attempt.Tier, out var next))
            {
                attempt = next;
                visited.Add(attempt.Tier);
            }

            Assert.Equal(
                new[] { ReturnTier.Normal, ReturnTier.HighClimb, ReturnTier.Hover, ReturnTier.Clip, ReturnTier.Teleport },
                visited);
            Assert.True(attempt.IsFinal);
        }

        [Fact]
        public void Final_tier_reports_no_further_tier()
        {
            var final = ReturnEscalation.Ladder.Last();

            Assert.True(final.IsFinal);
            Assert.False(ReturnEscalation.TryNext(final.Tier, out _));
        }

        [Fact]
        public void Only_the_last_tier_is_final()
        {
            foreach (var attempt in ReturnEscalation.Ladder.Take(ReturnEscalation.Ladder.Count - 1))
                Assert.False(attempt.IsFinal);
        }

        [Fact]
        public void Climb_heights_increase_monotonically()
        {
            var heights = ReturnEscalation.Ladder.Select(a => a.MaxStepHeight).ToList();

            for (var i = 1; i < heights.Count; i++)
                Assert.True(heights[i] >= heights[i - 1],
                    $"tier {i} climbs {heights[i]}, lower than tier {i - 1}'s {heights[i - 1]}");
        }

        [Fact]
        public void Escalation_relaxes_constraints_rather_than_tightening_them()
        {
            // Once a tier ignores obstacles, no later tier may go back to respecting them --
            // otherwise the ladder could fail at a tier looser than one it already passed.
            var ignoring = false;
            foreach (var attempt in ReturnEscalation.Ladder)
            {
                if (attempt.IgnoresObstacles) ignoring = true;
                else Assert.False(ignoring, $"tier {attempt.Tier} re-tightens after an ignoring tier");
            }

            Assert.True(ReturnEscalation.Ladder.Last().Teleports);
            Assert.False(ReturnEscalation.First.Teleports);
        }

        [Fact]
        public void Sequence_is_independent_of_world_state()
        {
            // Same input, same answer, every time -- the policy is a pure function of the tier.
            for (var run = 0; run < 3; run++)
                foreach (var tier in ReturnEscalation.Ladder.Select(a => a.Tier))
                {
                    var got = ReturnEscalation.TryNext(tier, out var next);
                    var again = ReturnEscalation.TryNext(tier, out var nextAgain);

                    Assert.Equal(got, again);
                    if (got) Assert.Equal(next.Tier, nextAgain.Tier);
                }
        }

        [Fact]
        public void First_tier_matches_the_movers_ordinary_climb_height()
        {
            // The opening attempt must be the drone's normal behaviour, or every return leg
            // would start by relaxing a constraint it never needed to relax.
            Assert.Equal(ReturnTier.Normal, ReturnEscalation.First.Tier);
            Assert.Equal(ReturnEscalation.OrdinaryMaxStepHeight, ReturnEscalation.First.MaxStepHeight);
            Assert.False(ReturnEscalation.First.IgnoresObstacles);
        }

        [Fact]
        public void OrdinaryClimbHeight_ClearsTheDroneSOwnDeepestShaft()
        {
            // THE invariant tying the tier to the pathfinder. A pass cuts MiningShaftDepth layers
            // below each column's surface, so re-entering that hole is a descent of at least that
            // much -- and more where a plot's own surface is not flat, since the pass floor is its
            // deepest column. A step limit below this means the machine cannot path back into the
            // hole it just dug: live pass #7, "dispatched to area point 302,617" followed by
            // "no path ... to area point 302,617" six layers later.
            //
            // Asserted as a relationship, not a number. The limit was 16 by hand, which is 15 + 1
            // and correct by luck; raising the tier without raising the limit would recreate the
            // bug a release later, where it would be far harder to recognise.
            Assert.True(
                ReturnEscalation.OrdinaryMaxStepHeight > DroneTier.MiningShaftDepth,
                $"Ordinary climb height {ReturnEscalation.OrdinaryMaxStepHeight} must exceed the " +
                $"tier's own shaft depth {DroneTier.MiningShaftDepth}, or the drone cannot re-enter its own shaft.");
        }

        [Fact]
        public void For_returns_the_ladder_entry_for_each_tier()
        {
            foreach (var attempt in ReturnEscalation.Ladder)
                Assert.Equal(attempt.Tier, ReturnEscalation.For(attempt.Tier).Tier);
        }
    }
}
