using System.Collections.Generic;
using System.Globalization;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// Pure formatting for the Farming tab (U10), following <see cref="MiningReadout"/>'s
    /// precedent: the component holds members and pushes strings, this decides what they
    /// say, and nothing here touches an Eco.* namespace.
    /// </summary>
    public static class FarmReadout
    {
        /// <summary>
        /// One line of the assigned-areas list (R35): the area, its crop, and its markers.
        /// What it will do next and why it will not are separate rows, so a long reason
        /// never pushes the crop off the line.
        ///
        /// <para>
        /// <c>[farm]</c> and <c>[flat]</c> shipped here as this tab's own strings, appended
        /// unconditionally: <c>line += FarmMarker; if (isFlat) line += FlatMarker;</c>. They now
        /// go to two different places, because they are two different KINDS of fact.
        /// <c>[farm]</c> is what the area IS, so it takes the exclusive status slot through
        /// <see cref="AreaLifecycle.StatusFor"/> -- and the mining ladder is never consulted for
        /// a farming area, so no rung of that ramp can reach this line. <c>[flat]</c> is an
        /// observation about the ground and rides the shared annotation channel, last in priority
        /// (R10), where the ordering and the cap live.
        /// </para>
        /// </summary>
        public static string FormatAreaLine(int position, FarmAreaState area, bool isFlat)
        {
            var crop = string.IsNullOrEmpty(area.Crop) ? "no crop selected" : area.Crop;

            // Farmland has no ladder to run, which is exactly why none is passed: the kind is
            // what decides, not a comparison between two computed answers.
            var status = AreaLifecycle.StatusFor(AreaKind.Farming, miningLadder: null);

            var line = $"{position}. {area.AreaName} -- {crop}"
                       + DockReadout.TagSeparator + DockReadout.StatusWord(status)
                       + DockReadout.FormatAnnotations(Annotations(isFlat));

            return DockReadout.InStatusColor(line, status);
        }

        /// <summary>
        /// <c>[flat]</c> only while the ground is, which is re-derived from a surface sample
        /// rather than stored (R9 of the farming plan). Nothing else annotates this line today.
        /// </summary>
        private static IEnumerable<AreaAnnotation> Annotations(bool isFlat)
        {
            if (isFlat) yield return AreaAnnotation.Flat;
        }

        /// <summary>What the drone does here next, or empty for an area that is doing nothing.</summary>
        public static string FormatNextAction(FarmAreaState area)
        {
            if (!area.NextAction.HasValue) return string.Empty;

            switch (area.NextAction.Value)
            {
                case FarmAction.PlaceDirt: return "placing dirt";
                case FarmAction.Plow: return "plowing";
                case FarmAction.Sow: return $"sowing {area.Crop}";
                case FarmAction.Harvest: return $"harvesting {area.Crop}";
                case FarmAction.LeaveAlone: return "nothing to do here";
                case FarmAction.Relay: return "re-laying desert sand as dirt";
                default: return area.NextAction.Value.ToString();
            }
        }

        /// <summary>
        /// Why nothing happens here, or empty when there is work to do (R35). Every reason
        /// reads differently: the whole value of the row is that a citizen can tell a
        /// supply problem from a permission one from the system simply working.
        /// </summary>
        public static string FormatStall(FarmAreaState area)
        {
            if (!area.Stall.HasValue) return string.Empty;

            switch (area.Stall.Value)
            {
                case FarmStallReason.NoCropSelected:
                    // Deliberately says nothing about seed: this is a setting the citizen
                    // has not made, not a supply problem, and the two send them to
                    // different screens.
                    return "awaiting a crop -- pick one and this area starts";

                case FarmStallReason.MissingMaterial:
                    return $"blocked -- linked storage has no {area.MissingMaterial}";

                case FarmStallReason.CeilingReached:
                    return $"holding -- {area.Crop} is at its ceiling, so ripe plants are left standing";

                case FarmStallReason.WaitingOnGrowth:
                    return $"waiting on growth -- due in {FormatHours(area.NextDueHours)}";

                case FarmStallReason.UnfitGround:
                    // R38: the engine's own condition, because "contaminated" sends a
                    // player to fix pollution when the plot was refused for temperature.
                    return $"unfit ground -- {area.UnfitCondition} refuses {area.Crop} here";

                case FarmStallReason.LawRefusal:
                    return "refused by settlement law -- the action is not permitted here";

                case FarmStallReason.PropertyRefusal:
                    return "refused by property authorization -- the dock's owner lacks access here";

                case FarmStallReason.LevelPassBlocked:
                    // The pass's own words, not a supply clause wrapped round them.
                    return $"levelling stopped -- {area.UnfitCondition}";

                case FarmStallReason.BlocksRefused:
                    return $"stopped -- every block here was refused: {area.UnfitCondition}";

                case FarmStallReason.PackRejected:
                    return "stopped -- the drone built an action its own safety checks refused; this is a bug, please report it";

                case FarmStallReason.Skipped:
                    // Not a stall a citizen acts on -- the area is working and one block
                    // was passed over -- so it says so plainly rather than borrowing the
                    // wording of a reason that stops a field.
                    return "working -- some blocks were passed over";

                case FarmStallReason.HeldByOverlap:
                    // R37: how much ground is held and what that means, and nothing about
                    // whose area holds it. The channel docks exchange geometry over is
                    // internal and never a player-facing view.
                    return $"{area.HeldPlotCount} plots held -- they overlap another area, and work stops on shared ground";

                default:
                    return area.Stall.Value.ToString();
            }
        }

        /// <summary>
        /// The job's own line. An idle job with a wake says when it comes back, so
        /// "nothing is happening" reads as a schedule rather than as a fault.
        /// </summary>
        public static string FormatJobStatus(FarmJobStatus status, double? wakeAtHours)
        {
            switch (status)
            {
                case FarmJobStatus.NoAreas: return "idle -- no areas assigned";
                case FarmJobStatus.Working: return "working";
                case FarmJobStatus.Blocked: return "blocked -- every area needs attention";
                case FarmJobStatus.Idle:
                    return wakeAtHours.HasValue
                        ? $"idle -- next area due in {FormatHours(wakeAtHours)}"
                        : "idle -- waiting on a storage change";
                default: return status.ToString();
            }
        }

        private static string FormatHours(double? hours)
        {
            if (!hours.HasValue) return "an unknown time";

            var value = hours.Value.ToString("0.#", CultureInfo.InvariantCulture);
            return $"{value}h";
        }
    }
}
