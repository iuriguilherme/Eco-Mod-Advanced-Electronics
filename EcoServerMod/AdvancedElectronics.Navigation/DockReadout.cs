using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// The uncoloured tags that may ride beside an area's lifecycle status (R9, R29, R30).
    ///
    /// <para>
    /// <b>Declaration order IS display priority</b>, most-blocking first, and
    /// <see cref="DockReadout.FormatAnnotations(System.Collections.Generic.IEnumerable{AreaAnnotation})"/>
    /// sorts by it. R29 fixes the first three: <c>[overlap]</c>, <c>[unreachable]</c>,
    /// <c>[assigned]</c> -- an overlap can stop work on ground two areas share, an unreachable
    /// area stops this trip, and an assignment merely says which area the drone is on.
    /// </para>
    /// <para>
    /// The two kind-and-ground tags follow, because neither blocks anything: <c>[farm]</c> says
    /// what the area is FOR (R30) and <c>[flat]</c> says what the ground is fit for by hand.
    /// <c>[flat]</c> is last of all, which the farming plan's R10 states outright -- it yields
    /// whenever a higher-priority tag needs the room, and shows again once the room is free.
    /// </para>
    /// <para>
    /// None of these carries a colour. Under R9 they inherit the line's lifecycle colour, which
    /// is why the whole line is wrapped once rather than each tag wrapping itself.
    /// </para>
    /// </summary>
    public enum AreaAnnotation
    {
        /// <summary>This area shares plots with another dock's area (R35).</summary>
        Overlap = 0,

        /// <summary>The drone cannot currently get to the area it is assigned to.</summary>
        Unreachable = 1,

        /// <summary>The area the dock's drone is working on.</summary>
        Assigned = 2,

        /// <summary>The area's kind is farming (R30).</summary>
        Farm = 3,

        /// <summary>The surface is flat enough to farm by hand or by tractor. Lowest priority of all.</summary>
        Flat = 4
    }

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

        /// <summary>
        /// The area's one lifecycle status (R3), derived by <see cref="AreaLifecycle.DeriveStatus"/>
        /// and handed over already decided. The readout renders what it is given and never
        /// re-derives -- both tabs read the SAME status for the same area, which is the whole
        /// point of moving the record onto the area.
        /// </summary>
        public AreaLifecycleStatus Status { get; }

        /// <summary>Whether this is the area the drone is working on.</summary>
        public bool IsAssigned { get; }

        /// <summary>
        /// Whether the drone currently cannot get here. Only ever true for the assigned area --
        /// reachability is a fact about a trip in progress, not a stored property of an area, so
        /// an unassigned area has no answer to report rather than a negative one.
        /// </summary>
        public bool IsUnreachable { get; }

        /// <summary>Whether this area shares plots with another dock's area (R35).</summary>
        public bool HasOverlap { get; }

        public AreaSnapshot(
            int position,
            string name,
            int plotCount,
            float coveragePercent,
            SurveyFinding topVisibleFinding,
            AreaLifecycleStatus status,
            bool isAssigned,
            bool isUnreachable = false,
            bool hasOverlap = false)
        {
            Position = position;
            Name = name;
            PlotCount = plotCount;
            CoveragePercent = coveragePercent;
            TopVisibleFinding = topVisibleFinding;
            Status = status;
            IsAssigned = isAssigned;
            IsUnreachable = isUnreachable;
            HasOverlap = hasOverlap;
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
        /// <summary>
        /// What separates the summary from the status, and one tag from the next. Each tag carries
        /// its own leading separator, so an area with no tags ends at its status word with no
        /// trailing gap left behind.
        /// </summary>
        public const string TagSeparator = "   ";

        /// <summary>
        /// How many annotations one line may carry (R29). Two, so the line cannot grow without
        /// limit on a panel whose budget is spent one row at a time -- and the two shown are the
        /// most blocking, because those are the ones a player has to act on.
        /// </summary>
        public const int MaxAnnotations = 2;

        /// <summary>
        /// The grey for <c>[empty]</c> (R4). A hex value rather than Unity's named <c>grey</c>
        /// because this is the one rung of the ramp that cannot be judged from its name: grey and
        /// no-colour differ by lightness rather than hue, so it has to be dark enough to read as
        /// deliberately coloured rather than as the panel's default text. Confirmed against that
        /// default text in a live pass before ship; the number lives here so tuning it is a
        /// one-line change.
        /// </summary>
        public const string EmptyColor = "#808080";

        /// <summary>
        /// The word every lifecycle status carries (R4). A word as WELL as a colour, so no state
        /// depends on colour vision to read -- and the only reading that survives the grey/default
        /// pair at all.
        /// </summary>
        public static string StatusWord(AreaLifecycleStatus status)
        {
            switch (status)
            {
                case AreaLifecycleStatus.Unsurveyed: return "[unsurveyed]";
                case AreaLifecycleStatus.Surveyed: return "[surveyed]";
                case AreaLifecycleStatus.Digging: return "[digging]";
                case AreaLifecycleStatus.Mined: return "[mined]";
                case AreaLifecycleStatus.Cleared: return "[cleared]";
                case AreaLifecycleStatus.Empty: return "[empty]";
                default: return $"[{status.ToString().ToLowerInvariant()}]";
            }
        }

        /// <summary>
        /// The ramp's colour for a status (R4), or null for unsurveyed, which is deliberately
        /// uncoloured. <c>[mined]</c> is YELLOW here: green now means <c>[surveyed]</c>, and the
        /// vocabulary this replaces used green for mined-out ground.
        /// </summary>
        public static string StatusColor(AreaLifecycleStatus status)
        {
            switch (status)
            {
                case AreaLifecycleStatus.Unsurveyed: return null;
                case AreaLifecycleStatus.Surveyed: return "green";
                case AreaLifecycleStatus.Digging: return "magenta";
                case AreaLifecycleStatus.Mined: return "yellow";
                case AreaLifecycleStatus.Cleared: return "red";
                case AreaLifecycleStatus.Empty: return EmptyColor;
                default: return null;
            }
        }

        /// <summary>
        /// Wraps a whole roster line in its lifecycle colour, the seam every coloured line goes
        /// through. The wrap covers the annotations too, which is what makes them INHERIT the
        /// line's colour rather than carry one (R9) -- the markers this replaces each wrapped
        /// themselves, so a yellow <c>[assigned]</c> sat inside a green line.
        /// </summary>
        public static string InStatusColor(string text, AreaLifecycleStatus status)
        {
            var colour = StatusColor(status);
            return colour == null ? text : $"<color={colour}>{text}</color>";
        }

        /// <summary>The uncoloured word for one annotation.</summary>
        public static string AnnotationWord(AreaAnnotation annotation)
        {
            switch (annotation)
            {
                case AreaAnnotation.Overlap: return "[overlap]";
                case AreaAnnotation.Unreachable: return "[unreachable]";
                case AreaAnnotation.Assigned: return "[assigned]";
                case AreaAnnotation.Farm: return "[farm]";
                case AreaAnnotation.Flat: return "[flat]";
                default: return $"[{annotation.ToString().ToLowerInvariant()}]";
            }
        }

        /// <summary>
        /// The one ordered, capped annotation channel (R29). Sorts by
        /// <see cref="AreaAnnotation"/>'s declaration order -- most blocking first -- drops
        /// duplicates, keeps at most <see cref="MaxAnnotations"/>, and prefixes each with
        /// <see cref="TagSeparator"/> so an empty set renders as an empty string and never as a
        /// dangling separator.
        ///
        /// <para>
        /// Everything that annotates an area goes through here, farming's <c>[farm]</c> and
        /// <c>[flat]</c> included. Appending a marker directly to a line is what let two of them
        /// ship with no ordering and no cap, and a per-caller append cannot honour a cap it
        /// cannot see.
        /// </para>
        /// </summary>
        public static string FormatAnnotations(IEnumerable<AreaAnnotation> annotations)
        {
            if (annotations == null) return string.Empty;

            var sb = new StringBuilder();
            foreach (var annotation in annotations.Distinct().OrderBy(a => (int)a).Take(MaxAnnotations))
                sb.Append(TagSeparator).Append(AnnotationWord(annotation));

            return sb.ToString();
        }

        /// <inheritdoc cref="FormatAnnotations(IEnumerable{AreaAnnotation})"/>
        public static string FormatAnnotations(params AreaAnnotation[] annotations) =>
            FormatAnnotations((IEnumerable<AreaAnnotation>)annotations);

        public const string DockedPhrase = "docked";
        public const string AtAreaPhrase = "at the area";

        /// <summary>
        /// Where the drone is, in words a player can act on. Both tabs render this rather than the
        /// status enum: "EnRoute" and "OnStation" name states in a state machine, and tell someone
        /// watching a drone nothing about what it is doing or whether to intervene.
        /// </summary>
        /// <param name="atAreaLabel">
        /// What being at the area means for this drone kind -- "surveying" reads better than "at
        /// the area" on a survey dock, where arriving and working are the same thing.
        /// </param>
        public static string FormatTravel(DroneStatus status, DroneTravelTarget target, string atAreaLabel = null)
        {
            switch (status)
            {
                case DroneStatus.Idle: return DockedPhrase;
                case DroneStatus.OnStation: return atAreaLabel ?? AtAreaPhrase;
                case DroneStatus.Unreachable: return "cannot reach the area";
                case DroneStatus.EnRoute:
                    return target == DroneTravelTarget.Dock ? "returning to dock" : "flying to the area";
                default: return string.Empty;
            }
        }

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
        /// The annotations one area currently warrants, in no particular order --
        /// <see cref="FormatAnnotations(IEnumerable{AreaAnnotation})"/> owns the ordering and the
        /// cap, so a caller that adds a condition here cannot get the priority wrong.
        /// </summary>
        private static IEnumerable<AreaAnnotation> AnnotationsFor(AreaSnapshot area)
        {
            if (area.HasOverlap) yield return AreaAnnotation.Overlap;
            if (area.IsUnreachable) yield return AreaAnnotation.Unreachable;
            if (area.IsAssigned) yield return AreaAnnotation.Assigned;
        }

        /// <summary>
        /// <b>The one roster line, rendered once for both tabs (R29).</b> Field order is fixed
        /// here and nowhere else: position and name, plot count, the coverage-and-material
        /// summary, the lifecycle status, then the uncoloured annotations. Both tabs call this,
        /// which is what makes the order impossible to drift apart -- two formatters agreeing by
        /// convention is exactly the arrangement that produced two different lines for one area.
        ///
        /// <para>
        /// <paramref name="owningDockName"/> is the Mining tab's prefix and the ONLY structural
        /// difference between the two renders. It exists because that tab lists areas from
        /// several docks at once, area names are not unique, and the selector commits by
        /// position -- so without it a player can assign the wrong area. The Survey tab lists one
        /// dock's own areas and passes null.
        /// </para>
        /// <para>
        /// The colour wraps the WHOLE line, annotations included, because R9 has them inherit the
        /// lifecycle colour rather than carry one of their own. The summary is coloured only as
        /// part of that wrap: <see cref="FormatAreaSummary"/> still returns plain text, so the
        /// compact control labels and the findings readout that share it inherit nothing the
        /// roster wanted.
        /// </para>
        /// <para>
        /// The summary is the one field convergence deliberately leaves different: it is narrowed
        /// by the READING dock's own material filter, because a filter is a display preference
        /// rather than a fact about the area. Everything else is the area speaking for itself.
        /// </para>
        /// </summary>
        public static string FormatRosterLine(AreaSnapshot area, string owningDockName = null)
        {
            var head = string.IsNullOrWhiteSpace(owningDockName)
                ? $"{area.Position}. {area.Name}"
                : $"{area.Position}. {owningDockName} -- {area.Name}";

            var line = $"{head} -- {area.PlotCount} plots, {FormatAreaSummary(area)}"
                       + TagSeparator + StatusWord(area.Status)
                       + FormatAnnotations(AnnotationsFor(area));

            return InStatusColor(line, area.Status);
        }

        /// <summary>The Survey tab's roster line: <see cref="FormatRosterLine"/> with no dock prefix.</summary>
        public static string FormatAreaLine(AreaSnapshot area) => FormatRosterLine(area);

        /// <summary>
        /// Names the area whose findings are on screen, and where it sits in the list.
        ///
        /// Annotated but not coloured: this row is a heading for the findings below it, not a row
        /// in the roster, and colouring it would say "this area is finished" about a panel
        /// section. The tags still come through the shared channel so their order and cap match
        /// the roster's.
        /// </summary>
        public static string FormatViewingLine(AreaSnapshot area, int totalAreas) =>
            $"Viewing: {area.Position} of {totalAreas} -- {area.Name}"
            + FormatAnnotations(AnnotationsFor(area));

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
