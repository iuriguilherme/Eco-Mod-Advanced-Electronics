namespace AdvancedElectronics.Navigation
{
    /// <summary>What an area asks of the player right now.</summary>
    public enum FarmAttention
    {
        /// <summary>The drone has work in hand here.</summary>
        Working,

        /// <summary>Nothing to do until something that happens by itself -- a crop growing, surplus being used.</summary>
        Waiting,

        /// <summary>The drone is stopped here until a player acts.</summary>
        NeedsYou
    }

    public static partial class FarmReadout
    {
        // Dark values on purpose. The tab's background is a light tan (sampled at about
        // #CBA165), and bright colours vanish on it: the first choices measured 1.1-1.8:1.
        // These measure 3.6-3.9:1 against that background.

        /// <summary>The colour of an area that needs a player. Red: it is the one line a player must not miss.</summary>
        public const string NeedsYouColor = "#9E0000";

        /// <summary>The colour of an area with work in hand.</summary>
        public const string WorkingColor = "#14531A";

        /// <summary>The colour of an area waiting on something that resolves by itself -- quiet, not alarming.</summary>
        public const string WaitingColor = "#4A4A4A";

        /// <summary>
        /// Sorts an area's state into working, waiting, or needing a player. Waiting is
        /// reserved for what clears without anyone: a crop growing, a ceiling a harvest will
        /// fall back under, an area with nothing to do until storage changes. Everything
        /// that only a player can clear -- a setting, a supply, a law, land rights, a tree,
        /// an overlap -- needs them.
        /// </summary>
        public static FarmAttention AttentionFor(FarmAreaState area)
        {
            if (!area.Stall.HasValue)
                return area.NextAction == FarmAction.LeaveAlone || !area.NextAction.HasValue
                    ? FarmAttention.Waiting
                    : FarmAttention.Working;

            switch (area.Stall.Value)
            {
                case FarmStallReason.WaitingOnGrowth:
                case FarmStallReason.CeilingReached:
                    return FarmAttention.Waiting;

                case FarmStallReason.Skipped:
                    return FarmAttention.Working;

                default:
                    return FarmAttention.NeedsYou;
            }
        }

        /// <summary>
        /// The line under an area's name: what the drone is doing and what comes next, what
        /// it is waiting for and that it will carry on by itself, or -- in the attention
        /// colour -- what is wrong and what the player should do about it.
        /// </summary>
        public static string FormatDetail(FarmAreaState area)
        {
            var attention = AttentionFor(area);
            string text;

            switch (attention)
            {
                case FarmAttention.Working:
                    text = FormatWorking(area);
                    return $"<color={WorkingColor}>{text}</color>";

                case FarmAttention.Waiting:
                    text = FormatWaiting(area);
                    return $"<color={WaitingColor}>{text}</color>";

                default:
                    text = $"needs you -- {FormatWhatToDo(area)}";
                    return $"<color={NeedsYouColor}>{text}</color>";
            }
        }

        private static string FormatWorking(FarmAreaState area)
        {
            if (area.Stall == FarmStallReason.Skipped)
                return "working -- some blocks were passed over; the rest of the area carries on";

            var crop = area.Crop;
            switch (area.NextAction)
            {
                case FarmAction.Plow: return $"working -- plowing; next: sowing {crop}";
                case FarmAction.Relay: return $"working -- re-laying desert sand as dirt; next: plowing, then sowing {crop}";
                case FarmAction.PlaceDirt: return $"working -- placing dirt; next: plowing, then sowing {crop}";
                case FarmAction.Sow: return $"working -- sowing {crop}; next: waiting for it to grow";
                case FarmAction.Harvest: return $"working -- harvesting {crop}; next: replanting";
                default: return $"working -- {FormatNextAction(area)}";
            }
        }

        private static string FormatWaiting(FarmAreaState area)
        {
            switch (area.Stall)
            {
                case FarmStallReason.WaitingOnGrowth:
                    return $"waiting -- {area.Crop} is growing, ready in {FormatHours(area.NextDueHours)}; the drone comes back by itself";

                case FarmStallReason.CeilingReached:
                    return $"waiting -- {area.Crop} is at its ceiling, so ripe plants are left standing; harvesting resumes by itself once storage falls below it";

                default:
                    return "waiting -- nothing to do right now; the drone comes back by itself when storage changes or a crop is due";
            }
        }

        /// <summary>What is wrong, then one sentence telling the player what to do.</summary>
        private static string FormatWhatToDo(FarmAreaState area)
        {
            switch (area.Stall)
            {
                case FarmStallReason.NoCropSelected:
                    return "no crop chosen. Pick one with Area Crop, then Set Crop For Selected Area.";

                case FarmStallReason.MissingMaterial:
                    return $"no {area.MissingMaterial} in storage. Put {area.MissingMaterial} in a chest linked to this dock as Take From.";

                case FarmStallReason.UnfitGround:
                    return $"{area.UnfitCondition} refuses {area.Crop} here. Fix the ground, or choose a crop that tolerates it.";

                case FarmStallReason.LawRefusal:
                    return "a settlement law forbids this work here. Change the law, or move the area.";

                case FarmStallReason.PropertyRefusal:
                    return "the dock's owner has no rights on this land. Get access to the plot, or move the area.";

                case FarmStallReason.LevelPassBlocked:
                    return $"levelling stopped: {area.UnfitCondition}.";

                case FarmStallReason.BlocksRefused:
                    return $"every block here was refused: \"{area.UnfitCondition}\". Roots refuse the plow next to trees -- clear the trees nearby, or redraw the area away from them.";

                case FarmStallReason.PackRejected:
                    return "the drone stopped on an internal error. Please report it.";

                case FarmStallReason.HeldByOverlap:
                    return $"{area.HeldPlotCount} plots overlap another area, and work stops on shared ground. Redraw one of the areas.";

                default:
                    return FormatStall(area);
            }
        }
    }
}
