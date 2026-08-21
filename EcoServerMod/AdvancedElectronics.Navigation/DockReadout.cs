using System;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// One survey area reduced to exactly what the dock's panel prints about it. Eco-free by
    /// construction: the caller does the filtering and the world lookups, then hands over the
    /// result. <see cref="TopVisibleFinding"/> is already narrowed by the player's material
    /// filter, which is why "nothing matching" and "nothing found" are the same shape here.
    /// </summary>
    public readonly struct AreaSnapshot
    {
        /// <summary>1-based position in the dock's area list — what the player sees, not an index.</summary>
        public int Position { get; }

        public string Name { get; }

        public int PlotCount { get; }

        public float CoveragePercent { get; }

        /// <summary>The largest finding the player's filter still shows, or <see cref="SurveyFinding.NotFound"/>.</summary>
        public SurveyFinding TopVisibleFinding { get; }

        /// <summary>Whether this is the area the drone is working on.</summary>
        public bool IsAssigned { get; }

        /// <summary>
        /// Whether the drone currently cannot get here. Only ever true for the assigned area --
        /// reachability is a fact about a trip in progress, not a stored property of an area, so
        /// an unassigned area has no answer to report rather than a negative one.
        /// </summary>
        public bool IsUnreachable { get; }

        public AreaSnapshot(int position, string name, int plotCount, float coveragePercent, SurveyFinding topVisibleFinding, bool isAssigned, bool isUnreachable = false)
        {
            Position = position;
            Name = name;
            PlotCount = plotCount;
            CoveragePercent = coveragePercent;
            TopVisibleFinding = topVisibleFinding;
            IsAssigned = isAssigned;
            IsUnreachable = isUnreachable;
        }
    }

    /// <summary>
    /// Pure formatting and cursor arithmetic for the dock's survey panel. Zero dependency on any
    /// Eco.* namespace, and — unlike the version this replaces — it lives in the assembly the test
    /// project references, so the claim is now enforceable rather than aspirational.
    ///
    /// The panel component and the chat commands both format through here, so a wording change
    /// lands in one place.
    /// </summary>
    public static class DockReadout
    {
        /// <summary>Marks the area the drone is assigned to. Trailing-appended, so it survives truncation last.</summary>
        public const string AssignedMarker = "   <color=yellow>[assigned]</color>";

        /// <summary>Marks the assigned area the drone cannot currently reach. Appended after <see cref="AssignedMarker"/>.</summary>
        public const string UnreachableMarker = "   <color=red>[unreachable]</color>";

        /// <summary>Colour name for a fully-surveyed area, so the completed ones can be picked out of a list at a glance.</summary>
        private const string CompleteColor = "green";

        /// <summary>
        /// Formats one per-material line (R2), quantity-led: how much of the material was found, the
        /// shallowest block to dig, and the depth range. Quantity is the headline because it is
        /// meaningful for common bulk materials (rock) as well as rare ore, where a concentration
        /// ratio read as noise (KTD2/R3).
        /// </summary>
        /// <param name="iconItemName">
        /// Item id to draw ahead of the line as Eco text markup, or null for no icon. Passed in
        /// rather than derived here because resolving a material to an item needs the item
        /// registry, which is an Eco type this assembly deliberately cannot see (KTD6).
        /// </param>
        public static string FormatOreLine(SurveyFinding finding, string iconItemName = null)
        {
            var icon = string.IsNullOrWhiteSpace(iconItemName) ? string.Empty : $"<icon name='{iconItemName}'> ";

            if (!finding.Found)
                return $"{icon}{finding.OreType}: no data yet";

            var depth = finding.DepthMax > finding.DepthBelowSurface
                ? $"depth {finding.DepthBelowSurface}-{finding.DepthMax}"
                : $"{finding.DepthBelowSurface} blocks deep";
            return $"{icon}{finding.OreType}: ~{finding.Count} blocks, shallowest at {finding.Position}, {depth}";
        }

        /// <summary>
        /// Wraps a block of readout text at a larger font size.
        ///
        /// PERCENT, not a bare number, and that distinction is the whole entry. The wiki's
        /// "7 is biggest, 1 is standard" scale describes signs and chat. A component panel renders
        /// through the client's rich-text layer, where a bare <c>&lt;size=2&gt;</c> means two
        /// ABSOLUTE units -- microscopic, not double. The engine keeps both forms as separate
        /// helpers (<c>RichTextUtils.Size(int)</c> emits <c>&lt;size=N&gt;</c> verbatim,
        /// <c>Size(float)</c> emits <c>&lt;size=N%&gt;</c>), which is the tell that they are not
        /// interchangeable.
        ///
        /// The first attempt shipped <c>&lt;size=2&gt;</c> and read as "the tag does nothing here".
        /// It was doing something: shrinking the block to near-invisibility, which is also why the
        /// icons on those lines were too small to confirm.
        /// </summary>
        public static string AtReadableSize(string text) =>
            string.IsNullOrEmpty(text) ? text : $"<size={ReadableSizePercent}%>{text}</size>";

        /// <summary>Findings-list font size as a percentage of default.</summary>
        private const int ReadableSizePercent = 125;

        /// <summary>
        /// The "how is this area doing" fragment: coverage plus the biggest visible find. Split out
        /// from <see cref="FormatAreaLine"/> because a compact control label needs the same summary
        /// without the position-and-plots preamble.
        ///
        /// Three states, not two. An area at zero coverage reads "not surveyed yet" even when the
        /// filter would have hidden everything anyway — telling a player nothing matched before the
        /// drone has looked is a different (and wrong) claim.
        /// </summary>
        public static string FormatAreaSummary(AreaSnapshot area)
        {
            var top = area.TopVisibleFinding;

            if (top.Found)
                return $"{area.CoveragePercent:F0}% surveyed, most {top.OreType} (~{top.Count} blocks)";

            // Order matters. A zero-coverage area with nothing visible has not been looked at;
            // saying "nothing matching" there would report a result the drone never produced.
            return area.CoveragePercent > 0f
                ? $"{area.CoveragePercent:F0}% surveyed, nothing matching"
                : "not surveyed yet";
        }

        /// <summary>
        /// One area's line in the panel's roster: position, name, size, summary, assignment.
        ///
        /// The completed-area colour is applied HERE rather than inside
        /// <see cref="FormatAreaSummary"/>, so it belongs to the area roster and only to the area
        /// roster. The summary is also the shape a compact control label wants, and the findings
        /// readout states coverage in its own words -- neither should inherit a colour because the
        /// roster wanted one. A formatter that returns markup its callers did not ask for is how
        /// colour leaks into surfaces nobody chose it for.
        /// </summary>
        public static string FormatAreaLine(AreaSnapshot area)
        {
            var summary = FormatAreaSummary(area);

            // Coloured whole rather than just the percentage: the point is to find the finished
            // areas by scanning the list, and a green "100%" inside an otherwise uniform line is
            // barely easier to spot than a plain one.
            if (area.CoveragePercent >= 100f)
                summary = $"<color={CompleteColor}>{summary}</color>";

            return $"{area.Position}. {area.Name} -- {area.PlotCount} plots, {summary}"
                   + (area.IsAssigned ? AssignedMarker : string.Empty)
                   + (area.IsUnreachable ? UnreachableMarker : string.Empty);
        }

        /// <summary>Names the area whose findings are on screen, and where it sits in the list.</summary>
        public static string FormatViewingLine(AreaSnapshot area, int totalAreas) =>
            $"Viewing: {area.Position} of {totalAreas} -- {area.Name}"
            + (area.IsAssigned ? AssignedMarker : string.Empty)
            + (area.IsUnreachable ? UnreachableMarker : string.Empty);

        /// <summary>
        /// The notice shown when the player has more areas than the panel has controls. Empty when
        /// every area is reachable from the panel, so the caller can append unconditionally.
        /// </summary>
        public static string FormatOverflowNotice(int areaCount, int controlPoolSize, string fallbackCommand) =>
            areaCount <= controlPoolSize
                ? string.Empty
                : $"Areas past {controlPoolSize} have no control -- assign them with {fallbackCommand}.";

        /// <summary>
        /// Keeps a view cursor inside a list that changed size under it. Returns 0 for an empty list
        /// rather than -1, so callers never index with a sentinel.
        /// </summary>
        public static int ClampCursor(int index, int count)
        {
            if (count <= 0) return 0;
            if (index < 0) return 0;
            return index >= count ? count - 1 : index;
        }

        /// <summary>
        /// Moves a view cursor by <paramref name="direction"/>, wrapping at both ends. Wrapping is
        /// deliberate: with no scrollbar and no jump-to control, a cursor that stops at the end
        /// makes the last area cost N clicks to reach from the first.
        /// </summary>
        public static int CycleCursor(int index, int direction, int count)
        {
            if (count <= 0) return 0;

            // Clamp first: the list can shrink between the player seeing it and clicking.
            var next = (ClampCursor(index, count) + direction) % count;
            return next < 0 ? next + count : next;
        }
    }
}
