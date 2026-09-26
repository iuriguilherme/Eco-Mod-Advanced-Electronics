using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedElectronics.Navigation;
using Eco.Core.Utils;
using Eco.Shared.Serialization;
using Eco.Shared.Voxel;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// One serialized survey finding for one ore IN ONE PLOT of the area that produced it
    /// (KTD1): which plot, the dig target block, its depth, and the plot concentration
    /// (R5/R7). The persisted mirror of the Eco-free <see cref="SurveyFinding"/> — a plain
    /// <c>[Serialized]</c> class (parameterless ctor + settable props) so it survives a
    /// restart alongside the area, unlike the in-memory <see cref="SurveyRecord"/> it is
    /// derived from.
    ///
    /// Rows are per plot rather than per area so a change reaching one plot can be recorded
    /// against that plot alone, and so an edit can preserve the plots it retains, each
    /// without touching the rest. The area totals the readouts show are re-derived from
    /// these rows at read time by <see cref="SurveyRecord.AreaTotals"/>.
    ///
    /// Per-plot rows earn their place twice over now: a plot whose ground changed is marked
    /// for re-reading rather than having its rows deleted, so the rows have to be
    /// addressable per plot in order to be KEPT per plot as well as dropped per plot.
    ///
    /// The plot is two plain ints, matching how <see cref="SurveyAreaEntry.PlotCoords"/>
    /// already flattens: the class stays flat primitives, which is what makes its
    /// serializability not in question.
    /// </summary>
    [Serialized]
    public class OreFindingSnapshot
    {
        /// <summary>Plot x of the plot these counts were accumulated in.</summary>
        [Serialized] public int PlotX { get; set; }

        /// <summary>Plot z of the plot these counts were accumulated in.</summary>
        [Serialized] public int PlotZ { get; set; }

        [Serialized] public string OreType { get; set; }
        [Serialized] public int Count { get; set; }
        [Serialized] public int X { get; set; }
        [Serialized] public int Y { get; set; }
        [Serialized] public int Z { get; set; }
        [Serialized] public int DepthBelowSurface { get; set; }
        [Serialized] public int DepthMax { get; set; }
        [Serialized] public float Concentration { get; set; }

        public OreFindingSnapshot() { }

        public static OreFindingSnapshot From(SurveyFinding f) => new OreFindingSnapshot
        {
            PlotX = f.Plot.X,
            PlotZ = f.Plot.Z,
            OreType = f.OreType,
            Count = f.Count,
            X = f.Position.X,
            Y = f.Position.Y,
            Z = f.Position.Z,
            DepthBelowSurface = f.DepthBelowSurface,
            DepthMax = f.DepthMax,
            Concentration = f.Concentration,
        };

        /// <summary>The plot this row belongs to.</summary>
        public PlotCoord Plot => new PlotCoord(this.PlotX, this.PlotZ);

        /// <summary>Back to the Eco-free per-plot row shape the projection and readouts consume.</summary>
        public SurveyFinding ToSurveyFinding(int areaId) =>
            SurveyFinding.CreateInPlot(areaId, this.Plot, this.OreType, this.Count, new BlockPos(this.X, this.Y, this.Z), this.DepthBelowSurface, this.DepthMax, this.Concentration);
    }

    /// <summary>
    /// The Eco-side serialized record of one dock-owned survey area (U4, R1a/R2a/R3):
    /// a dock-local id, a player-facing name, and the drawn plots. Owned by the
    /// <see cref="DroneDockObject"/> that created it (KTD9) — there is no mod-wide
    /// registry — so it persists exactly because the dock does, and is discarded with the
    /// dock.
    ///
    /// Plots are stored as a FLATTENED <see cref="PlotCoords"/> list (x0, z0, x1, z1, ...)
    /// of plain ints rather than a list of a coordinate struct: Eco's <c>Vector2i</c> is
    /// not <c>[Serialized]</c> and nothing in the game source serializes a list of it, so
    /// per the U4 plan the plot set is stored in a form whose serializability is not in
    /// question. <see cref="ToSurveyArea"/> projects this into the Eco-free
    /// <see cref="SurveyArea"/> (U2) for membership tests and the plot cap.
    ///
    /// <para>
    /// <b>A farm is one of these too.</b> Since the cross-kind work there is one area type
    /// carrying a <see cref="Kind"/>, not a mining type and a farming type bridged by an
    /// adapter — an adapter would have left two collections to keep in step forever, and the
    /// blindness this class's claim machinery had to farms came from exactly that split. So the
    /// farm record lives here, beside the survey record, and a kind change preserves everything
    /// the area holds (R10) for the simple reason that nothing moves: the members it no longer
    /// needs merely stop being asked about.
    /// </para>
    /// <para>
    /// <b>Not unit-tested, by design, and it is not an oversight to be corrected.</b> This type
    /// holds Eco types, and <c>AdvancedElectronics.Navigation.Tests.csproj</c> references only
    /// the Eco-free navigation assembly — deliberately, so the decision logic is testable without
    /// a server. Every decision this class could be asked to make is therefore pushed across that
    /// boundary and proved there instead: the fold arithmetic in
    /// <see cref="AdvancedElectronics.Navigation.LegacyFarmAreas"/>, the claim rules in
    /// <c>AreaClaims</c>, the status ladder in <see cref="AreaLifecycle"/>. What is left here is
    /// storage and the shape of it, and it is proved in the batched live session
    /// (<c>docs/solutions/workflow-issues/eco-mod-batched-live-testing.md</c>). Adding a decision
    /// to this file moves it out of reach of the suite; put it in the navigation assembly.
    /// </para>
    /// </summary>
    [Serialized]
    public class SurveyAreaEntry
    {
        /// <summary>Dock-local id, assigned by the owning dock. Stable across renames; identifies the area for assignment.</summary>
        [Serialized] public int Id { get; set; }

        /// <summary>Player-facing name. Not unique — two areas may share a name and stay distinct by <see cref="Id"/>.</summary>
        [Serialized] public string Name { get; set; }

        /// <summary>
        /// Drawn plots, flattened as consecutive (x, z) pairs. Even length by construction.
        /// A <see cref="ThreadSafeList{T}"/>, not a plain <c>List</c>: Eco's serializer rejects a
        /// non-immutable <c>[Serialized]</c> member ("Attempting to serialize non-immutable
        /// member ... Either make immutable or add [ThreadSafe]") and fails server init.
        /// </summary>
        [Serialized] public ThreadSafeList<int> PlotCoords { get; set; } = new();

        /// <summary>
        /// Bumped every time this area's geometry is set or redrawn (U8, KTD2). A mining
        /// dock's <c>MiningAreaRef</c> stores the epoch observed at assignment time, so a
        /// later redraw of THIS area -- whether or not it is the source dock's own
        /// currently-assigned area -- tells that dock its reference describes ground that
        /// has moved, without the mining dock needing to compare geometry itself.
        ///
        /// <para>
        /// Since U9 the bump is a NOTIFICATION rather than a verdict. It used to end the
        /// referring job outright; now the reader asks what the edit actually did -- see
        /// <see cref="AreaResolutionPolicy.Resolve(AreaLookupSignal, string, string, bool)"/>
        /// -- and ends the job only when the edit removed plots that job still has to work
        /// (R21). An edit that only adds plots leaves the job running over the plots it
        /// retained, which is what makes R20's preservation something the player can see.
        /// </para>
        /// </summary>
        [Serialized] public int Epoch { get; set; }

        /// <summary>
        /// <see cref="AreaKind"/>'s ordinal — what this area is FOR (U11, R30, KTD11), persisted
        /// as a plain int the way <c>MiningExclusionEntry.CategoryValue</c> already persists
        /// <c>SkipCategory</c>. Every other member of this class is a flat primitive or a
        /// <see cref="ThreadSafeList{T}"/> of them, for the reason this class's header gives: the
        /// shape's serializability is not in question. An enum out of a mod assembly would be a
        /// new question, and the failure it risks is the silent one — a clean build and a server
        /// that never prints its load line (see
        /// <c>docs/solutions/conventions/serialized-needs-a-member-to-write-back-into.md</c>).
        ///
        /// <para>
        /// <b>0 is mining, and that is what makes the upgrade silent.</b> An existing save holds
        /// only mining areas and has no field for this at all, so it loads as 0 and every area
        /// reads mining without anything being written. Read and write it through
        /// <see cref="Kind"/>, never directly.
        /// </para>
        /// </summary>
        [Serialized] public int KindValue { get; set; }

        /// <summary>
        /// What this area is for (R30) — the enum face of <see cref="KindValue"/>.
        ///
        /// <para>
        /// <b>Kind is not a field the roster renders and not an annotation competing for an
        /// overlay slot.</b> It SELECTS which status is derived at all (R46): a mining area runs
        /// the lifecycle ladder and reads its rung, a farming area reads <c>[farm]</c> and the
        /// ladder is never consulted. <c>DroneDockObject.StatusOfArea</c> is where that choice is
        /// made, through <see cref="AreaLifecycle.StatusFor"/>, which takes the ladder as a
        /// deferred delegate precisely so there is no moment at which both values exist.
        /// </para>
        /// <para>
        /// That matters most for ground repurposed under R31: an exhausted mining area turned
        /// into farmland KEEPS its <see cref="MinedStamps"/> and its
        /// <see cref="BedrockPlotCoords"/> — every input the ladder reads is still sitting here
        /// and still true about this area's past. Only never asking the question keeps the answer
        /// out. Nothing in the change action clears any of it (R32).
        /// </para>
        /// <para>
        /// NOT <c>[Serialized]</c>: <see cref="KindValue"/> is the member the serializer writes
        /// back into, and a second serialized view of one value would be a second thing to keep
        /// in step.
        /// </para>
        /// </summary>
        public AreaKind Kind
        {
            get => (AreaKind)this.KindValue;
            set => this.KindValue = (int)value;
        }

        /// <summary>
        /// This area's survey findings as one row per (plot, ore) (KTD1), persisted with the area
        /// (KTD11 design change): available until the area is deleted or resurveyed. Reassigning
        /// the drone away and back does NOT clear them — they belong to the area, not the drone or
        /// the dock's current assignment. Nor does an EDIT: since U9 <see cref="SetPlots"/> drops
        /// only the rows of plots the edit removed and leaves every retained plot's rows standing
        /// (R20). They are cleared by the owning dock on delete, and by a
        /// NEWLY STARTED resurvey before the drone samples anything (R10) — which is what makes a
        /// resurvey report the ground as it is now rather than re-stating what the last pass
        /// found. A pass merely RESUMING one that stopped is not a new start and does not clear
        /// (R25).
        ///
        /// Read through <see cref="ReadFindings()"/> for the area totals or
        /// <see cref="ReadFindings(PlotCoord)"/> for one plot's rows, never directly: those are
        /// what apply the KTD1 upgrade before handing anything back.
        /// </summary>
        [Serialized] public ThreadSafeList<OreFindingSnapshot> Findings { get; set; } = new();

        /// <summary>
        /// Which shape <see cref="Findings"/> is stored in (KTD1). A save written before U1 has no
        /// value for this and loads as 0, which <see cref="FindingsVersion.IsStale"/> reads as a
        /// pre-U1 area whose rows are discarded on first read — a server that upgrades resurveys
        /// once. The marker is explicit rather than inferred from an absent plot, because a pre-U1
        /// row loads as plot (0,0) and (0,0) is a real plot near the world origin.
        /// </summary>
        [Serialized] public int FindingsShapeVersion { get; set; }

        /// <summary>Fraction of this area surveyed, 0-100 (R7a). Persisted with the findings.</summary>
        [Serialized] public float CoveragePercent { get; set; }

        /// <summary>How deep below the surface the survey scanned, in blocks (the drone sensor's reach).
        /// 0 until surveyed. Tells the player how far down was actually looked into.</summary>
        [Serialized] public int SurveyDepth { get; set; }

        /// <summary>Median surface height across the surveyed columns; meaningful when <see cref="SurveyDepth"/> > 0.</summary>
        [Serialized] public int MedianSurface { get; set; }

        /// <summary>
        /// Per-plot surveyed stamps (KTD12, R41), flattened as (x, z, stamp) triples --
        /// the persisted mirror of the live <see cref="PlotStampAccumulator"/> the sweep
        /// writes into. Compared against this area's <see cref="MinedStamps"/>
        /// (<see cref="PlotFreshness.IsMineable"/>) to decide which plots a mining job may
        /// work. Follows the same lifecycle as <see cref="Findings"/>: dropped on delete, and on
        /// an edit for the plots the edit REMOVED only (U9, R20) -- a retained plot's stamp still
        /// describes the very same ground, and dropping it would leave that plot reading
        /// unsurveyed and refusing to be mined.
        /// </summary>
        [Serialized] public ThreadSafeList<long> SurveyedStamps { get; set; } = new();

        /// <summary>
        /// Per-plot MINED stamps (U2, R1), in exactly the shape <see cref="SurveyedStamps"/>
        /// uses -- flattened (x, z, stamp) triples projected from and rehydrated into a
        /// <see cref="PlotStampAccumulator"/> (KTD2). These used to live on the mining dock, one
        /// list per dock, which meant two docks pointed at one area each held a private opinion
        /// about what had been dug and the second one re-dug ground the first had taken. They are
        /// the area's record now: a stamp is a fact about the GROUND, so every dock that can see
        /// the area reads the same one.
        ///
        /// Unlike the surveyed stamps this list is NOT cleared by <see cref="ClearFindings"/>
        /// (R13): a resurvey discards what the old survey claimed to find, but the digging
        /// actually happened and the record of when is what makes the resurveyed area mineable
        /// again rather than merely un-mined. What stays per dock is the mining JOB and its
        /// ledger (R2).
        ///
        /// An EDIT does reach them, for the plots it removed and no others (U9). R13's reason
        /// stops at the area's edge: digging having happened stays true, but a plot the area no
        /// longer holds has no reader left to tell it to.
        /// </summary>
        [Serialized] public ThreadSafeList<long> MinedStamps { get; set; } = new();

        /// <summary>
        /// The plots this area's last survey observed DOWN AT BEDROCK (U4, R7/R8/R26),
        /// flattened as consecutive (x, z) pairs exactly the way <see cref="PlotCoords"/>
        /// already flattens — no new persistence shape is invented for this. A plot is listed
        /// only when every column in it rested on the impenetrable world floor; the fold from
        /// columns to plots is <see cref="SurveyRecord.PlotRestsOnBedrock"/>'s, and only its
        /// result is stored.
        ///
        /// This is what `[cleared]` and `[empty]` are tested against, and it is an OBSERVATION
        /// rather than a flag (R8): it is written by a survey pass and cleared by
        /// <see cref="ClearFindings"/> alongside the findings (R10), so a player who fills a
        /// cleared area in has only to let it be resurveyed for it to rejoin the ramp. That is
        /// the opposite of <see cref="MinedStamps"/>, which survive a resurvey because digging
        /// having happened is not a claim a later survey can falsify.
        ///
        /// A <see cref="ThreadSafeList{T}"/> of plain ints, like every other serialized
        /// collection here: Eco's serializer rejects a non-immutable <c>[Serialized]</c> member
        /// and fails server init.
        /// </summary>
        [Serialized] public ThreadSafeList<int> BedrockPlotCoords { get; set; } = new();

        /// <summary>
        /// The lock that makes every read-modify-write on an area's stored lists atomic (R10).
        ///
        /// <para>
        /// Writing one of these lists is not one instantaneous operation. It is three steps: read
        /// the whole list, build a replacement from it, store the replacement back. Two of those
        /// sequences running at overlapping moments both read the same starting list, both build a
        /// replacement, and both store — and whichever stores second silently discards the other's
        /// work. <see cref="ThreadSafeList{T}"/> does not prevent that: it makes each individual
        /// operation safe, not the sequence.
        /// </para>
        /// <para>
        /// There is genuinely more than one writer. An area's stored data is written from the
        /// owning dock's tick, from its drone's tick, and from a mining dock that does not own the
        /// area and reached it by resolving the area by identifier. An earlier version of this work
        /// assumed a single writer and was wrong.
        /// </para>
        /// <para>
        /// <b>Why a second lock rather than the claim lock.</b> Reusing
        /// <c>DroneDockObject.AreaClaimLock</c> would serialise every per-plot survey write on the
        /// server behind every assignment, and a sweep records a plot at a time. This one is a
        /// leaf: nothing inside it calls back into the dock, so it can never be held while the
        /// claim lock is being acquired. The only possible order is claim lock then this one,
        /// which is an order and not a cycle, so the pair cannot deadlock. Keep it that way — do
        /// not call dock code from inside a method that holds this lock.
        /// </para>
        /// <para>
        /// Static rather than one lock per area. A per-area lock would be the finer instrument,
        /// but it would have to be a non-serialized instance field surviving deserialization, and
        /// a mistake there fails server initialisation with no log output at all. These operations
        /// are a few list copies each; the contention is not worth that risk.
        /// </para>
        /// </summary>
        internal static readonly object AreaDataLock = new object();

        /// <summary>
        /// The plots of this area whose ground changed after the survey read them, flattened as
        /// consecutive PAIRS of ints: x, z. A plot listed here is one whose recorded findings can
        /// no longer be trusted, so a survey drone should read it again (R3, R4).
        ///
        /// <para>
        /// Being listed here destroys nothing. The plot keeps its ore findings, its surveyed
        /// timestamp, its bedrock observation, its mined timestamp and its pass record. That is
        /// the whole point: a later survey can then confirm or replace what is recorded rather
        /// than rebuilding it from nothing, and a wrong judgement costs redundant work instead of
        /// lost data.
        /// </para>
        /// <para>
        /// This list changes what the DRONES do and never what the PLAYER sees. The area goes on
        /// displaying the status tag it last earned; being listed here never makes an area read
        /// `[unsurveyed]` (R6, R7).
        /// </para>
        /// <para>
        /// An area loading from a world that predates this list reads it as empty, which is the
        /// correct state rather than merely a tolerable one: nothing has yet been found to need
        /// re-reading. No migration is required for that reason, and none should be added.
        /// </para>
        /// <para>
        /// A <see cref="ThreadSafeList{T}"/> of plain ints, like every other serialized collection
        /// here: Eco's serializer rejects a non-immutable <c>[Serialized]</c> member and fails
        /// server init silently, printing not even the mod's own load line.
        /// </para>
        /// </summary>
        [Serialized] public ThreadSafeList<int> PlotsNeedingReReading { get; set; } = new();

        /// <summary>
        /// The live sample record of the pass currently running on this area (U7, R25), flattened
        /// as consecutive FIVE-int rows: x, z, surfaceY, sampledBlocks, bedrockState.
        ///
        /// One row per COLUMN, not per block. The sensor scans a column top-down in a single call,
        /// so the surface height plus the number of blocks taken names exactly the same block set
        /// that listing every block would, at roughly a fifteenth of the size — and this is by
        /// some distance the largest structure the mod persists, so the shape is the reason it is
        /// affordable at all. <see cref="SurveyRecord.RestorePassColumn"/> replays each row
        /// downward from its surface, rebuilding the sampled-block set, the per-plot sampled
        /// counts, and therefore the coverage the stopped pass had reached.
        ///
        /// Two sentinel encodings keep the row flat primitives, matching every other serialized
        /// collection here: <see cref="NoSurfaceRecorded"/> in the surfaceY slot means no surface
        /// was observed, and bedrockState is 0 for "not observed", 1 for "above bedrock", 2 for
        /// "at bedrock" — "not observed" and "observed false" are different answers to the fold
        /// in <see cref="SurveyRecord.PlotRestsOnBedrock"/>.
        ///
        /// Persisting this REVERSES the design decision <see cref="SurveyRecord"/> used to state
        /// in its own header — that the record was session-scoped and never serialized. The
        /// reversal is safe only because a newly started pass clears it first (R10): without that
        /// clear, the record's per-block idempotency would survive a restart and the intermittent
        /// stale-findings fault would become permanent.
        /// </summary>
        [Serialized] public ThreadSafeList<int> SweepColumns { get; set; } = new();

        /// <summary>Which plot of the running pass's raster order the sweep is on (U7, R25).</summary>
        [Serialized] public int SweepPlotIndex { get; set; }

        /// <summary>Which column of <see cref="SweepPlotIndex"/>'s plot the sweep is on (U7, R25).</summary>
        [Serialized] public int SweepColumnCursor { get; set; }

        /// <summary>
        /// True while a survey pass is part-way through this area. This is what tells a dispatch
        /// apart from a NEWLY STARTED resurvey (R10) and a RESUMING one (R25) — the one
        /// distinction U7 exists to draw. Set when a pass begins sampling, cleared when the sweep
        /// finishes or the area is cleared, and explicit rather than inferred from a non-zero
        /// cursor, because a pass stopped during its very first plot has a cursor of (0, 0) and
        /// must still resume rather than clear what it already sampled.
        /// </summary>
        [Serialized] public bool SweepInProgress { get; set; }

        // ---------------------------------------------------------------
        // U13: the claim (R37, R38, R39, KTD6). Recorded AT ASSIGNMENT rather than re-derived
        // while a drone works, which is the whole point: a drone works only plots its own dock
        // has claimed, so it never has to ask mid-pass what another dock is currently doing.
        //
        // Two flat primitives, for the reason this class's header gives — and both carry a
        // setter, because a [Serialized] member the serializer cannot write back into stops the
        // mod loading with a clean build and a completely silent log
        // (docs/solutions/conventions/serialized-needs-a-member-to-write-back-into.md).
        //
        // Read and write through the methods below, never the members: HasClaim is what every
        // reader wants, and the pair is meaningless apart.
        // ---------------------------------------------------------------

        /// <summary>
        /// Object id of the dock holding this area, stringified, or null/empty when unclaimed.
        /// Stringified for the same reason <c>MiningExclusionEntry.SourceDockId</c> is: a flat
        /// primitive is a shape this class already proves serializable.
        /// </summary>
        [Serialized] public string ClaimHolderDockId { get; set; }

        /// <summary>
        /// The holder's assignment epoch at the moment the claim was taken (KTD6). It is what
        /// makes a released-and-reassigned claim a NEW record rather than a resumed one: the same
        /// dock claiming the same area twice writes two different epochs, so nothing downstream
        /// can mistake the second for a continuation of the first.
        /// </summary>
        [Serialized] public int ClaimEpoch { get; set; }

        /// <summary>
        /// Whether any dock holds this area right now. NOT <c>[Serialized]</c>:
        /// <see cref="ClaimHolderDockId"/> is the member the serializer writes into, and a
        /// computed property carrying the attribute is exactly the silent load failure above.
        /// </summary>
        public bool HasClaim => !string.IsNullOrEmpty(this.ClaimHolderDockId);

        /// <summary>Whether <paramref name="dockId"/> is the dock holding this area.</summary>
        public bool IsClaimedBy(Guid dockId) =>
            this.HasClaim && this.ClaimHolderDockId == dockId.ToString();

        /// <summary>
        /// Records <paramref name="holdingDockId"/> as holding this area's plots, at the
        /// assignment epoch that produced the claim (R37). Overwrites any previous claim: the
        /// assignment paths refuse a contested claim BEFORE reaching here, under the one lock
        /// KTD6 defines, so an unrefused call is by construction the new holder.
        /// </summary>
        public void RecordClaim(Guid holdingDockId, int assignmentEpoch, bool forMining) =>
            this.RecordClaim(holdingDockId, assignmentEpoch, forMining ? ClaimWorkMining : ClaimWorkSurvey);

        /// <summary>
        /// As <see cref="RecordClaim(Guid, int, bool)"/>, naming the kind of work outright rather
        /// than as a two-way flag (U2, R17).
        ///
        /// <para>
        /// The bool overload predates farming, when survey and mining were the only two answers
        /// and a flag could carry the choice. A farm is now an area in this same collection and
        /// it is a THIRD answer: <see cref="ClaimWorkFarming"/>. Neither existing value stands in
        /// for it — <c>0</c> is "not recorded", which drops the claim so an assigned farm stops
        /// holding its plots against a neighbouring dock, and <see cref="ClaimWorkMining"/> would
        /// make <see cref="IsClaimedForMining"/> report a mining drone at work on a crop field.
        /// </para>
        /// <para>
        /// The bool overload is kept and delegates here. Its call sites are correct as they
        /// stand, and renaming a method that writes a persisted record buys nothing a reader of
        /// this file needs.
        /// </para>
        /// </summary>
        public void RecordClaim(Guid holdingDockId, int assignmentEpoch, int claimWorkValue)
        {
            lock (AreaDataLock)
            {
                this.ClaimHolderDockId = holdingDockId.ToString();
                this.ClaimEpoch = assignmentEpoch;
                this.ClaimWorkValue = claimWorkValue;
            }
        }

        /// <summary>Value of <see cref="ClaimWorkValue"/> meaning a survey drone holds this area.</summary>
        public const int ClaimWorkSurvey = 1;

        /// <summary>Value of <see cref="ClaimWorkValue"/> meaning a mining drone holds this area.</summary>
        public const int ClaimWorkMining = 2;

        /// <summary>
        /// Value of <see cref="ClaimWorkValue"/> meaning a farming drone holds this area (U2,
        /// R17). Numbered after the existing two rather than folded into either: a farm's
        /// assignment has to hold its plots exactly as a mine's does, while reading as a
        /// different kind of work to everyone who asks what is happening on the ground.
        /// </summary>
        public const int ClaimWorkFarming = 3;

        /// <summary>
        /// What kind of drone holds this area's claim: 0 for not recorded, 1 for a survey drone,
        /// 2 for a mining drone, 3 for a farming drone. This is how a mining dock tells a survey
        /// dock that a mining drone
        /// is working the area (R16), without a second channel between them — the fact rides on
        /// the claim the assignment already takes, and the survey dock reads the same area.
        ///
        /// <para>
        /// <b>Why the values start at 1 rather than mirroring an enum.</b> An existing save has no
        /// field for this at all, so it loads as 0, and 0 has to mean "not recorded" rather than
        /// naming a kind of work. Numbering from 1 is what makes that possible. Had this reused
        /// <see cref="AreaKind"/>'s ordinals, where mining is 0, every claim held in an existing
        /// save would have loaded as a MINING claim and every claimed area on an upgraded server
        /// would have started reading `[digging]` for work no drone was doing.
        /// </para>
        /// <para>
        /// The safe direction on an upgraded save is therefore the quiet one: a claim taken before
        /// this field existed reads as no kind of work, so it produces no `[digging]`, and the
        /// next assignment records the kind properly.
        /// </para>
        /// <para>
        /// A plain int, like every other serialized member here, for the reason
        /// <see cref="KindValue"/> gives at length: a mod-assembly enum is a question this class
        /// deliberately does not ask.
        /// </para>
        /// </summary>
        [Serialized] public int ClaimWorkValue { get; set; }

        /// <summary>
        /// Whether a mining drone is currently working this area (R14, R15). NOT
        /// <c>[Serialized]</c> — it is computed from the two members above, and a computed
        /// property carrying that attribute is the silent load failure this class warns about.
        /// </summary>
        public bool IsClaimedForMining => this.HasClaim && this.ClaimWorkValue == ClaimWorkMining;

        /// <summary>
        /// Whether a farming drone is currently working this area (U2, R17) — the read half of
        /// <see cref="ClaimWorkFarming"/>, so no caller has to compare the raw value. NOT
        /// <c>[Serialized]</c>, for the same reason <see cref="IsClaimedForMining"/> is not.
        ///
        /// <para>
        /// Note what this is not. R47 reserves farmland against a mining dock whether or not the
        /// farm is assigned, so the question "is this ground a farm" is
        /// <see cref="Kind"/>, not this. This answers the narrower one: is a drone on it now.
        /// </para>
        /// </summary>
        public bool IsClaimedForFarming => this.HasClaim && this.ClaimWorkValue == ClaimWorkFarming;

        /// <summary>
        /// Drops the claim and returns the plots it covered — what R38's message names. An area
        /// holding no claim releases nothing, which is why the return is a list rather than a
        /// bool: the caller states what was freed, and "nothing" is a real answer.
        ///
        /// <para>
        /// The plots are the area's CURRENT geometry, because a claim is keyed to the assignment
        /// rather than to the geometry (KTD6) — it simply stands over whatever plots the area
        /// holds now. Plots an EDIT removed left the claim at the moment of the edit, and
        /// <c>AreaEditPlan.ReleasedFromClaim</c> is what names those.
        /// </para>
        /// </summary>
        public IReadOnlyList<PlotCoord> ReleaseClaim()
        {
            if (!this.HasClaim) return Array.Empty<PlotCoord>();

            var released = this.Plots().ToList();
            this.ClaimHolderDockId = null;
            this.ClaimEpoch = 0;
            // The kind of work goes with the claim it described. Leaving it behind would make an
            // unclaimed area still answer that a mining drone is working it, which is exactly the
            // question R14 asks before it will show `[surveyed]` again.
            this.ClaimWorkValue = 0;
            return released;
        }

        /// <summary>
        /// As <see cref="ReleaseClaim"/>, but only when <paramref name="dockId"/> is the holder.
        /// A dock walking away from an area must not drop a claim some other dock took in the
        /// meantime — the reference it is releasing may be stale.
        /// </summary>
        public IReadOnlyList<PlotCoord> ReleaseClaimBy(Guid dockId) =>
            this.IsClaimedBy(dockId) ? this.ReleaseClaim() : Array.Empty<PlotCoord>();

        // ---------------------------------------------------------------
        // U6: the blocked reason reconciliation leaves behind (R15, KTD6).
        //
        // R13 unassigns an area that holds ground it is not entitled to, and R15 says the area
        // then READS blocked, with the reason where every other assignment failure reason is
        // already written. The farm side already had somewhere to put that — LastStallReason
        // carries FarmStallReason.HeldByOverlap and the Farming tab renders it — but the mining
        // side computed its blocked reason live from the job that had just ended and persisted
        // nothing per area. A job's end reason cannot answer this: the job ends reading "the
        // area was unassigned", which is true, was not the player's doing, and says nothing
        // about why.
        //
        // So the record lives here, on the area, for three reasons. The area is what R15 makes
        // the subject. The area outlives the job, the assignment and the drone, and the player
        // arrives after a restart with all three gone. And the area is the one thing both kinds
        // have, so one record serves the mining panel and the farm tab alike.
        //
        // WHO WRITES IT: reconciliation, and only reconciliation (U5), through
        // RecordReconciliationBlock below. This unit declares, persists and renders the reason
        // and deliberately does not produce it — the reason had no producer at all before this
        // work, and two units each believing the other writes it is how it would end up with
        // none again.
        // ---------------------------------------------------------------

        /// <summary>
        /// Why load-time reconciliation undid this area's assignment, as an
        /// <see cref="AreaClaimBlock"/> ordinal, or -1 for an area it left alone.
        ///
        /// <para>
        /// -1 rather than 0 for "none", because 0 is <see cref="AreaClaimBlock.HeldByAssignment"/>
        /// — a real member — and an absent field settling at the type default would have every
        /// area in every existing save asserting a block nobody recorded. The same trap the farm
        /// record's own -1 defaults are written against.
        /// </para>
        /// <para>
        /// Stored as an ordinal, matching <see cref="LastStallReason"/>: the enum is the
        /// navigation assembly's and is not <c>[Serialized]</c>.
        /// </para>
        /// </summary>
        [Serialized] public int ReconciliationBlockValue { get; set; } = -1;

        /// <summary>
        /// The plots the collision actually covered, flattened as consecutive (x, z) pairs — the
        /// same shape <see cref="PlotCoords"/> uses, and for the same reason: Eco's
        /// <c>Vector2i</c> is not <c>[Serialized]</c>.
        ///
        /// <para>
        /// The contested plots and not the whole area: R15 requires the reason to name the
        /// ground in dispute, which is what sends the player to the right place to look. They
        /// are stored rather than recomputed because the other area may have been redrawn,
        /// deleted or reassigned by the time anyone reads this — and a reason that silently
        /// re-derives from a world that has moved on is worse than one that says what happened.
        /// </para>
        /// </summary>
        [Serialized] public ThreadSafeList<int> ReconciliationBlockPlotCoords { get; set; } = new();

        /// <summary>
        /// The recorded block as its enum, or null for none. NOT <c>[Serialized]</c>:
        /// <see cref="ReconciliationBlockValue"/> is the member the serializer writes, and a
        /// second serialized view of one fact is a second thing to keep in step.
        /// </summary>
        public AreaClaimBlock? ReconciliationBlock =>
            this.ReconciliationBlockValue < 0 ? null : (AreaClaimBlock)this.ReconciliationBlockValue;

        /// <summary>The contested plots as <see cref="PlotCoord"/>s (unflattening the pairs).</summary>
        public IEnumerable<PlotCoord> ReconciliationBlockPlots()
        {
            foreach (var plot in UnflattenPlots(this.ReconciliationBlockPlotCoords)) yield return plot;
        }

        /// <summary>
        /// <b>The seam U5 writes through, and the only writer of the blocked reason (R15).</b>
        /// Records that reconciliation undid this area's assignment, over these plots, for this
        /// reason.
        ///
        /// <para>
        /// One seam for both kinds, because the area is one type carrying a kind and the two
        /// tabs read two different members. A farming area also gets the farm side's own stall
        /// written — <c>FarmStallReason.HeldByOverlap</c> with a REAL held-plot count — which is
        /// the reason the farming plan declared, rendered and unit-tested with nothing anywhere
        /// writing it. Writing both here rather than at the call site is what keeps the two
        /// surfaces from disagreeing about the same event.
        /// </para>
        /// <para>
        /// Called by reconciliation at load (U5) and by nothing else. It does not unassign
        /// anything: undoing the assignment and recording why are separate acts, and this is the
        /// record.
        /// </para>
        /// </summary>
        /// <param name="reason">Which rule refused the ground — the two are lifted by different acts.</param>
        /// <param name="contestedPlots">
        /// The plots the two areas shared. An empty list records nothing: a reason that cannot
        /// name the ground does not satisfy R15, and a half-written record reads as a real one.
        /// </param>
        public void RecordReconciliationBlock(AreaClaimBlock reason, IEnumerable<PlotCoord> contestedPlots)
        {
            var plots = (contestedPlots ?? Enumerable.Empty<PlotCoord>()).ToList();
            if (plots.Count == 0) return;

            this.ReconciliationBlockValue = (int)reason;
            this.ReconciliationBlockPlotCoords = FlattenPlots(plots);

            // The farm side's own reason, given the count it has never had a producer for. The
            // Farming tab reads LastStallReason, not this record, so a farming area whose block
            // was written only here would read as though nothing had happened to it.
            if (this.Kind == AreaKind.Farming)
            {
                this.LastStallReason = (int)FarmStallReason.HeldByOverlap;
                this.LastNextAction = -1;
                this.LastHeldPlotCount = plots.Count;
            }
        }

        /// <summary>
        /// Drops the record, for the act that makes it untrue: the area being assigned again, its
        /// kind changing, or a later load finding nothing in conflict (R16).
        ///
        /// <para>
        /// The farm stall is cleared alongside it, but only when it is still the stall this
        /// record wrote — a farm that has since stalled on a missing seed is reporting something
        /// newer and truer, and clearing that would hide it.
        /// </para>
        /// </summary>
        public void ClearReconciliationBlock()
        {
            this.ReconciliationBlockValue = -1;
            this.ReconciliationBlockPlotCoords = new ThreadSafeList<int>();

            if (this.LastStallReason != (int)FarmStallReason.HeldByOverlap) return;

            this.LastStallReason = -1;
            this.LastHeldPlotCount = 0;
        }

        // ---------------------------------------------------------------
        // U2: the farm record (R10, R17, R18). Everything the old FarmAreaEntry serialized that
        // this class had no equivalent for, so an area carrying AreaKind.Farming is a whole farm
        // rather than a mining area with a crop bolted to it.
        //
        // This is the shape half of the Key Decision that farms and mines are ONE area type
        // carrying a kind, rather than two types bridged by an adapter. The adapter would have
        // left two collections to keep in step forever; the cost of this side is that a mining
        // area carries a dozen members it never reads. That cost is paid once, here, and it is
        // what makes R10 true by construction: a kind change preserves what the area recorded
        // because there is nothing to move. The members simply stop being asked about.
        //
        // Flat primitives and ThreadSafeLists of them, with setters, for the reasons this class's
        // header and KindValue give at length.
        //
        // The defaults matter and are not decoration. LastStallReason, LastNextAction and
        // LastNextDueHours rest at -1 meaning "none"; 0 is a real ordinal in the first two and a
        // real duration in the third, so an absent field settling at 0 would have every upgraded
        // area asserting a stall reason and a next action nobody produced.
        // ---------------------------------------------------------------

        /// <summary>
        /// The one crop this area grows (R24), as the engine's own species name. Null until a
        /// citizen picks one, which is the state R26 reports as awaiting a crop and works not at
        /// all. Meaningless on a mining area and not read there.
        /// </summary>
        [Serialized] public string Crop { get; set; }

        /// <summary>
        /// The level-first toggle (R17): whether the drone levels this ground before it plants.
        ///
        /// <para>
        /// The one member here that is not a readout. It decides what the drone DOES, so a
        /// migration that drops it changes behaviour with nothing on screen to say so — which is
        /// why the fold that carries it is tested member by member rather than in the aggregate.
        /// </para>
        /// </summary>
        [Serialized] public bool LevelFirst { get; set; }

        /// <summary>Last reported stall as a <c>FarmStallReason</c> ordinal, or -1 for none.</summary>
        [Serialized] public int LastStallReason { get; set; } = -1;

        /// <summary>Last reported next action as a <c>FarmAction</c> ordinal, or -1 for none.</summary>
        [Serialized] public int LastNextAction { get; set; } = -1;

        /// <summary>Hours until this area's least-grown plant comes due (R31); negative for none. Display only.</summary>
        [Serialized] public double LastNextDueHours { get; set; } = -1;

        /// <summary>
        /// When that plant comes due, as an absolute world time. The scheduling value: a stored
        /// DURATION re-anchored to the clock on every settle walks the wake forward each time
        /// anyone touches a chest, and the crop never gets picked.
        /// </summary>
        [Serialized] public double LastDueAtWorldSeconds { get; set; }

        /// <summary>Plots held by an overlap with another dock's area (R37), as last reported.</summary>
        [Serialized] public int LastHeldPlotCount { get; set; }

        /// <summary>The engine's own word for why the ground refused the crop (R38).</summary>
        [Serialized] public string LastUnfitCondition { get; set; }

        /// <summary>What linked storage was short of (R28).</summary>
        [Serialized] public string LastMissingMaterial { get; set; }

        /// <summary>
        /// Whether the surface is flat enough to farm by hand (R8), as last derived. Re-derived
        /// from the ground rather than stored as an achievement (R9), so this is the last
        /// derivation rather than a claim that survives the ground changing.
        /// </summary>
        [Serialized] public bool LastFlat { get; set; }

        /// <summary>
        /// Whether a level pass is under way. Distinct from <see cref="LevelFirst"/>: the toggle
        /// is the request, this is the run.
        /// </summary>
        [Serialized] public bool LevelPassStarted { get; set; }

        /// <summary>
        /// The height the running pass is levelling to, pinned at pass entry. Pinned rather than
        /// re-derived, because re-deriving the median from the half-levelled surface would move
        /// the target under the pass and it would never converge (R19).
        /// </summary>
        [Serialized] public int LevelTargetHeight { get; set; }

        /// <summary>Blocks the running pass has removed and still counts as its own material, wherever they now sit (R20).</summary>
        [Serialized] public int LevelBankedSpoil { get; set; }

        /// <summary>
        /// Ends a level pass and forgets its state, whether it completed or was abandoned (R21).
        ///
        /// <para>
        /// The three members move together and always have. Carried over from the legacy farm row
        /// as a method rather than as three writes at the call site, because a pass that cleared
        /// its started flag and kept its pinned target height would resume levelling to a height
        /// nobody asked for the next time the toggle went on.
        /// </para>
        /// <para>
        /// <b>Seam.</b> Eco-coupled by residence rather than by content — this type is in the
        /// server project, which the test project cannot reference. The pass logic it ends is
        /// <c>LevelPlan</c>'s, unit-tested in the navigation assembly.
        /// </para>
        /// </summary>
        public void ClearLevelPass()
        {
            this.LevelPassStarted = false;
            this.LevelTargetHeight = 0;
            this.LevelBankedSpoil = 0;
        }

        /// <summary>
        /// The legacy farm id this area was folded from, or 0 for an area that was never a legacy
        /// farm — the fold marker (U1, U3, R18).
        ///
        /// <para>
        /// It is what makes the fold safe to run twice. The legacy farm collection and this one
        /// numbered from independent counters, so the fold renumbers every farm it moves; without
        /// a record of which legacy row produced which area, a second load cannot tell a farm
        /// already folded from a farm still to fold, and mints a fresh id for every row again —
        /// duplicating every farm a player owns.
        /// </para>
        /// <para>
        /// 0 means "folded from nothing", which is what every existing area in every existing
        /// save correctly reads as. That is the one place a legacy farm id could collide with the
        /// absent-field default, and the dock minted farm ids from 1, so it does not.
        /// </para>
        /// </summary>
        [Serialized] public int FoldedFromLegacyFarmId { get; set; }

        /// <summary>Sentinel in the surfaceY slot of a <see cref="SweepColumns"/> row: no surface was recorded for that column.</summary>
        public const int NoSurfaceRecorded = int.MinValue;

        /// <summary>Parameterless constructor required by the Eco serializer.</summary>
        public SurveyAreaEntry() { }

        public SurveyAreaEntry(int id, string name, IEnumerable<PlotCoord> plots)
        {
            this.Id = id;
            this.Name = name;
            this.SetPlots(plots);
        }

        /// <summary>Number of plots this area covers (the value R1b's tier cap is checked against).</summary>
        public int PlotCount => this.PlotCoords.Count / 2;

        /// <summary>
        /// Replaces the stored plots with <paramref name="plots"/>, flattening to (x, z) pairs,
        /// and returns the plots the edit REMOVED so the caller can drop the same plots from the
        /// live record.
        ///
        /// <para>
        /// An edit preserves what it retains (U9, R20). Every plot still in the area keeps its
        /// surveyed stamp, its findings rows, its mined stamp, its at-bedrock observation and its
        /// rows in the running pass; only the plots the edit removed lose theirs, and the plots
        /// it added are unsurveyed because nothing has ever looked at them (R3). This replaces
        /// the old behaviour, where any redraw called <see cref="ClearFindings"/> and discarded
        /// the whole area's survey — including the SURVEYED STAMPS, which is what makes a plot
        /// count as surveyed at all, so extending an area by one plot un-mined the other fifteen.
        /// </para>
        /// <para>
        /// <see cref="Epoch"/> is still bumped, and still invalidates a stale
        /// <c>MiningAreaRef</c> — but under R21 that invalidation is now narrowed at the point
        /// it is read: an edit ends an in-flight mining job only when it removed plots that job
        /// still has to work. The bump remains the signal that the geometry moved; what the
        /// reader does with it is no longer unconditionally "end the job".
        /// </para>
        /// <para>
        /// A pass in flight survives too (R22). Its cursor is REMAPPED onto the new plot list
        /// rather than cleared: the cursor is an index into the raster order, so an edit makes
        /// the same index name a different plot, and neither keeping it nor dropping the pass is
        /// the requirement. <see cref="AreaEdit.RemapSweep"/> carries it across by name.
        /// </para>
        /// </summary>
        public IReadOnlyList<PlotCoord> SetPlots(IEnumerable<PlotCoord> plots)
        {
            // Under the lock as one operation (R10). An edit is several read-modify-writes in a
            // row -- the removed plots' survey state, their mined stamps, their re-reading marks,
            // the geometry itself, the coverage figure and the sweep cursor -- and a concurrent
            // writer landing between any two of them leaves the area describing a geometry it no
            // longer has. The nested lock inside ResetPlotsToUnsurveyed below is the same lock on
            // the same thread, which C# allows.
            lock (AreaDataLock)
            {
                return this.SetPlotsUnderLock(plots);
            }
        }

        /// <summary>
        /// The body of <see cref="SetPlots"/>. Separated only so the lock is taken in one obvious
        /// place; every caller goes through the public method. Do not call this directly.
        /// </summary>
        private IReadOnlyList<PlotCoord> SetPlotsUnderLock(IEnumerable<PlotCoord> plots)
        {
            // Rows written in a pre-U1 shape carry no plot, so they cannot be sorted into
            // retained and removed — the whole of this method is per plot. The KTD1 upgrade runs
            // first for that reason: on such a save there is nothing to attribute and nothing to
            // migrate, so the area reads unsurveyed and is surveyed once more, exactly as it
            // would on the first ordinary read.
            this.UpgradeFindingsIfStale();

            var after = (plots ?? Enumerable.Empty<PlotCoord>()).ToList();
            var before = this.Plots().ToList();
            var plan = AreaEdit.Plan(before, after);

            // Where the sweep stands NOW, read before anything moves it. The drop below rewinds
            // the cursor for its own reasons (U8) and the remap must start from where the pass
            // actually was, not from that intermediate.
            var sweepPlotIndex = this.SweepPlotIndex;
            var sweepColumnCursor = this.SweepColumnCursor;
            var plotsBefore = this.PlotCount;

            // 1. Everything the removed plots claimed, dropped wholesale -- their findings rows,
            //    their surveyed stamps, their at-bedrock observations and their pass-record rows,
            //    with coverage falling by the removed plots' share rather than to zero. It reaches
            //    only plots the area still covers, so it runs BEFORE the geometry is replaced.
            //
            //    This is the reset's only caller now. It once shared the method with the
            //    ground-change listener, which ran it whenever ground changed under a surveyed
            //    plot and the mod had not made the change. Destroying a survey result is right
            //    HERE, on ground the area is giving up, and it is not right there: a change to
            //    ground the area still holds now marks the plot for re-reading and keeps what the
            //    survey found.
            this.ResetPlotsToUnsurveyed(plan.Removed);

            // 2. ...and their mined stamps, which that method deliberately keeps (R13). R13's
            //    reason is that digging having happened stays true — it does not extend to ground
            //    the area no longer holds, where the row names a plot no reader will ever ask
            //    about and no survey will ever refresh.
            this.DropMinedStamps(plan.Removed);

            // 2b. ...and their re-reading marks. A mark says "what is recorded for this plot can
            //     no longer be trusted", so on ground the area no longer holds it names a plot no
            //     reader will ever ask about and no survey will ever visit. Dropped for the same
            //     reason as the mined stamps above, and dropped here rather than in step 1 because
            //     marking and resetting are different things and step 1 is the reset.
            foreach (var removed in plan.Removed)
                this.ClearReReadingMark(removed);

            this.PlotCoords = FlattenPlots(after);
            this.Epoch++;

            // 3. Coverage against the new denominator. Step 1 took the removed plots' share out
            //    over the OLD plot count, so what is left only needs restating over the new one:
            //    ten surveyed plots read 100% of ten and 83% of twelve.
            this.CoveragePercent = AreaEdit.RescaleCoverage(this.CoveragePercent, plotsBefore, this.PlotCount);

            // 4. The pass in flight (R22). Untouched when none is running — SweepInProgress is
            //    the flag that tells a resuming pass from a newly started one, and an edit is
            //    neither.
            if (this.SweepInProgress)
            {
                var cursor = AreaEdit.RemapSweep(before, after, sweepPlotIndex, sweepColumnCursor);
                this.SweepPlotIndex = cursor.PlotIndex;
                this.SweepColumnCursor = cursor.ColumnCursor;
            }

            // The plots an edit removed are exactly the plots that leave the claim (U9, R38). Named
            // through ReleasedFromClaim rather than Removed because that is the question the
            // caller is asking of them: the claim ITSELF stands -- it is keyed to the assignment
            // rather than to the geometry (KTD6), so it simply stands over whatever plots the area
            // holds now, and only these fall out of it.
            return plan.ReleasedFromClaim;
        }

        /// <summary>
        /// Drops the mined stamps of plots this area no longer holds (U9). The one record
        /// <see cref="ResetPlotsToUnsurveyed"/> keeps and an edit does not, because the two are
        /// answering different questions: that method's plots are still the area's and their
        /// digging still happened, while these plots have left the area entirely.
        /// </summary>
        private void DropMinedStamps(IReadOnlyCollection<PlotCoord> plots)
        {
            if (plots == null || plots.Count == 0)
                return;

            var dropped = new HashSet<PlotCoord>(plots);
            var kept = new ThreadSafeList<long>();
            for (var i = 0; i + 2 < this.MinedStamps.Count; i += 3)
            {
                if (dropped.Contains(new PlotCoord((int)this.MinedStamps[i], (int)this.MinedStamps[i + 1])))
                    continue;
                kept.Add(this.MinedStamps[i]);
                kept.Add(this.MinedStamps[i + 1]);
                kept.Add(this.MinedStamps[i + 2]);
            }
            this.MinedStamps = kept;
        }

        /// <summary>
        /// Replaces this area's persisted findings from a fresh survey pass. <paramref name="findings"/>
        /// are the per-plot rows <see cref="SurveyRecord.Findings(int)"/> projects — one per
        /// (plot, ore) — and writing them stamps the current shape version, so the rows this area
        /// now holds are never mistaken for a pre-U1 save.
        /// </summary>
        public void SetFindings(IEnumerable<SurveyFinding> findings, float coveragePercent, int surveyDepth, int medianSurface)
        {
            var snapshot = new ThreadSafeList<OreFindingSnapshot>();
            foreach (var f in findings.Where(f => f.Found))
                snapshot.Add(OreFindingSnapshot.From(f));
            this.Findings = snapshot;
            this.FindingsShapeVersion = FindingsVersion.Current;
            this.CoveragePercent = coveragePercent;
            this.SurveyDepth = surveyDepth;
            this.MedianSurface = medianSurface;
        }

        /// <summary>
        /// Discards this area's findings, surveyed stamps and at-bedrock observations (delete, or
        /// a newly started resurvey).
        ///
        /// <b>An edit no longer comes here</b> (U9, R20). It used to, and that is what made
        /// extending an area by one plot discard the survey of every plot it kept — including
        /// their surveyed stamps, so the retained plots read unsurveyed and stopped being
        /// mineable. <see cref="SetPlots"/> now drops per plot instead, and only what the edit
        /// removed.
        ///
        /// The bedrock observations go with the findings, not with the mined stamps (R10): they
        /// are a claim about what the ground is like NOW, so a pass that has not yet re-observed
        /// a plot must not be able to answer for it. This is what makes AE5 work — a `[cleared]`
        /// area a player has filled in stops reading cleared as soon as it is resurveyed.
        ///
        /// <see cref="MinedStamps"/> is deliberately NOT cleared here (R13). The findings are a
        /// claim about what is in the ground and a resurvey replaces them; the mined stamps are a
        /// record that digging happened, which no later survey makes untrue. Keeping them is also
        /// what makes AE3 work: an area mined at 200 and resurveyed at 300 is mineable again
        /// because 300 postdates a 200 that is still there to be postdated.
        ///
        /// The running pass goes too (U7). A cleared area's sweep cursor points into a plot list
        /// that no longer describes it, and the sampled-block set it names is exactly what R10
        /// requires a newly started resurvey to discard.
        /// </summary>
        public void ClearFindings()
        {
            this.Findings = new ThreadSafeList<OreFindingSnapshot>();
            // An empty list is trivially in the current shape, so stamping here is what keeps the
            // KTD1 upgrade a one-shot: a cleared area is never re-cleared on every later read.
            this.FindingsShapeVersion = FindingsVersion.Current;
            this.CoveragePercent = 0f;
            this.SurveyDepth = 0;
            this.MedianSurface = 0;
            this.SurveyedStamps = new ThreadSafeList<long>();
            this.BedrockPlotCoords = new ThreadSafeList<int>();
            // The re-reading marks go with the findings they qualify. A mark says "what is
            // recorded for this plot can no longer be trusted", and once nothing is recorded for
            // it there is no longer anything for the mark to qualify.
            this.PlotsNeedingReReading = new ThreadSafeList<int>();
            this.ClearSweep();
        }

        /// <summary>True when this area covers <paramref name="plot"/>. Scans the flat pair list rather than building a set: this is on the world's block-write path (U8).</summary>
        public bool CoversPlot(PlotCoord plot)
        {
            for (var i = 0; i + 1 < this.PlotCoords.Count; i += 2)
                if (this.PlotCoords[i] == plot.X && this.PlotCoords[i + 1] == plot.Z)
                    return true;
            return false;
        }

        /// <summary>
        /// True when this area holds anything a reset could take. The cheap first gate on the
        /// block-write path (U8): an area no pass has ever touched has nothing to invalidate, so
        /// it never reaches the plot scan.
        /// </summary>
        public bool HasSurveyState =>
            this.SurveyedStamps.Count > 0
            || this.Findings.Count > 0
            || this.BedrockPlotCoords.Count > 0
            || this.SweepColumns.Count > 0;

        /// <summary>
        /// Returns the named plots to unsurveyed, destroying everything the survey claimed about
        /// them. Returns the plots it actually reset -- those this area covers -- so the caller
        /// can drop the same plots from the live record.
        ///
        /// <para>
        /// <b>Its one caller is now the area edit</b> (<see cref="SetPlots"/>), for plots an edit
        /// REMOVED from the area. It used to have a second caller: the ground-change listener,
        /// which ran this whenever ground changed under a surveyed plot and the mod had not made
        /// the change. That is no longer what happens. The mod does not monitor the world for
        /// changes it did not make, and a change it did make marks the plots for re-reading and
        /// keeps every survey result rather than destroying them.
        /// </para>
        /// <para>
        /// So the destructiveness described below is correct and is deliberately kept, but it now
        /// applies only to ground the area no longer holds. Do not reintroduce a caller that runs
        /// this on ground the area still covers without deciding, explicitly, that deleting a
        /// survey result is the right answer there.
        /// </para>
        ///
        /// <para>
        /// Everything a survey CLAIMED about those plots goes: their findings rows, their surveyed
        /// stamps (which is what makes them read unsurveyed on the ladder), their at-bedrock
        /// observations, and their rows in the persisted pass record. Everything the rest of the
        /// area claims stays -- that is the whole point of the rows being per plot (KTD1), and it
        /// is why coverage falls by the reset plots' share rather than to zero.
        /// </para>
        /// <para>
        /// <b>The pass record and the sweep cursor are the half that is easy to miss, and the
        /// only half that fails silently.</b> Sampling is idempotent per exact position and, since
        /// U7, the record survives a restart -- so a plot left in the record is skipped by every
        /// later pass, and a plot marked unsurveyed that the drone will never re-read is the
        /// stale-findings fault reintroduced and made durable. Dropping the rows is not enough on
        /// its own either: the cursor is a monotonic index into the raster-ordered plot list, so a
        /// plot reset BEHIND it would never be revisited by the pass now running. Both are
        /// handled here, together, because either alone is a plot that can never be re-read.
        /// </para>
        /// <para>
        /// <see cref="MinedStamps"/> is untouched, for R13's reason: digging having happened is
        /// not a claim a later event makes untrue. Nor is <see cref="Epoch"/> bumped -- the
        /// geometry did not change, and a bump would invalidate every mining job pointed at this
        /// area (KTD2) over ground that is still the same shape.
        /// </para>
        /// </summary>
        public IReadOnlyList<PlotCoord> ResetPlotsToUnsurveyed(IEnumerable<PlotCoord> plots)
        {
            if (plots == null)
                return Array.Empty<PlotCoord>();

            // Under the lock, and this is the sequence that made the lock necessary (R10). It
            // reads four stored lists, builds a replacement for each, and stores all four back.
            // Two of these interleaving lose one of the two resets outright, with no error and no
            // log entry -- exactly the silent fault this subsystem exists to remove.
            lock (AreaDataLock)
            {
                return this.ResetPlotsToUnsurveyedUnderLock(plots);
            }
        }

        /// <summary>
        /// The body of <see cref="ResetPlotsToUnsurveyed"/>. Separated only so the lock is taken in
        /// one obvious place; every caller goes through the public method and therefore through the
        /// lock. Do not call this directly.
        /// </summary>
        private IReadOnlyList<PlotCoord> ResetPlotsToUnsurveyedUnderLock(IEnumerable<PlotCoord> plots)
        {

            // Rows written before findings became per-plot carry no plot, so filtering by plot
            // keeps every one of them and the stamp below would then freeze that attribution as
            // current. The KTD1 upgrade runs first for the same reason SetPlots runs it: on such
            // a save there is nothing to attribute, so the area reads unsurveyed and is surveyed
            // once more. Without this the two reset paths disagree about the same old save.
            this.UpgradeFindingsIfStale();

            var targets = new HashSet<PlotCoord>(plots.Where(this.CoversPlot));
            if (targets.Count == 0)
                return Array.Empty<PlotCoord>();

            // 1. The findings rows for those plots (KTD1) -- one plot's rows, not the area's.
            var findings = new ThreadSafeList<OreFindingSnapshot>();
            foreach (var row in this.Findings)
                if (!targets.Contains(row.Plot))
                    findings.Add(row);
            this.Findings = findings;
            // An area whose rows have just been filtered is in the current shape by construction,
            // so stamping keeps the KTD1 upgrade a one-shot exactly as ClearFindings does.
            this.FindingsShapeVersion = FindingsVersion.Current;

            // 2. Their surveyed stamps. This is what the ladder's unsurveyed guard reads, and the
            //    count of stamps actually dropped is what coverage falls by -- a plot that was
            //    never surveyed contributed nothing to subtract.
            var surveyed = new ThreadSafeList<long>();
            var droppedSurveyed = 0;
            for (var i = 0; i + 2 < this.SurveyedStamps.Count; i += 3)
            {
                var plot = new PlotCoord((int)this.SurveyedStamps[i], (int)this.SurveyedStamps[i + 1]);
                if (targets.Contains(plot))
                {
                    if (this.SurveyedStamps[i + 2] > 0) droppedSurveyed++;
                    continue;
                }
                surveyed.Add(this.SurveyedStamps[i]);
                surveyed.Add(this.SurveyedStamps[i + 1]);
                surveyed.Add(this.SurveyedStamps[i + 2]);
            }
            this.SurveyedStamps = surveyed;

            // 3. Their at-bedrock observations (U4). A claim about what the ground is like NOW,
            //    and the ground has just changed, so a pass that has not re-walked the plot must
            //    not be able to answer for it -- the same reasoning ClearFindings applies.
            var bedrock = new ThreadSafeList<int>();
            for (var i = 0; i + 1 < this.BedrockPlotCoords.Count; i += 2)
                if (!targets.Contains(new PlotCoord(this.BedrockPlotCoords[i], this.BedrockPlotCoords[i + 1])))
                {
                    bedrock.Add(this.BedrockPlotCoords[i]);
                    bedrock.Add(this.BedrockPlotCoords[i + 1]);
                }
            this.BedrockPlotCoords = bedrock;

            // 4. Their rows in the persisted pass record, and the cursor that would sail past them.
            var sweep = new ThreadSafeList<int>();
            for (var i = 0; i + 4 < this.SweepColumns.Count; i += 5)
            {
                var plot = GroundChange.PlotOf(this.SweepColumns[i], this.SweepColumns[i + 1], PlotUtil.PropertyPlotLength);
                if (targets.Contains(plot)) continue;
                for (var k = 0; k < 5; k++)
                    sweep.Add(this.SweepColumns[i + k]);
            }
            this.SweepColumns = sweep;

            if (this.SweepInProgress)
            {
                // Ordered over ToSurveyArea().EnumeratePlots(), which is the exact projection the
                // sweep itself indexes into -- it is a set, so a duplicated pair in PlotCoords
                // collapses there and would otherwise shift every index past it by one.
                var rewound = SweepOrder.RewindIndex(
                    SweepOrder.RasterOrder(this.ToSurveyArea().EnumeratePlots()), targets, this.SweepPlotIndex);
                if (rewound != this.SweepPlotIndex)
                {
                    this.SweepPlotIndex = rewound;
                    this.SweepColumnCursor = 0; // the rewound plot starts from its first column again.
                }
            }

            // 5. Coverage, in proportion to what was actually dropped (R16) -- never to zero.
            if (this.PlotCount > 0 && droppedSurveyed > 0)
                this.CoveragePercent = Math.Max(0f, this.CoveragePercent - 100f * droppedSurveyed / this.PlotCount);

            return targets.ToList();
        }

        /// <summary>
        /// Forgets the pass in flight: its per-column sample record, its cursor, and the flag that
        /// says one is running. A dispatch after this reads as a NEWLY STARTED pass (R10) rather
        /// than a resuming one (R25).
        /// </summary>
        public void ClearSweep()
        {
            this.SweepColumns = new ThreadSafeList<int>();
            this.SweepPlotIndex = 0;
            this.SweepColumnCursor = 0;
            this.SweepInProgress = false;
        }

        /// <summary>
        /// Projects the live record's pass state for this area — its per-column samples and its
        /// sweep cursor — onto the persisted rows, so a pass that stops resumes where it stopped.
        /// The two are written together on purpose: a cursor saved without its samples resumes
        /// past ground whose coverage was lost, and samples saved without their cursor re-fly
        /// ground the record already treats as done.
        /// </summary>
        public void SetSweep(SurveyRecord record)
        {
            if (record == null)
                return;

            var flat = new ThreadSafeList<int>();
            foreach (var column in record.PassColumns(this.Id))
            {
                flat.Add(column.X);
                flat.Add(column.Z);
                flat.Add(column.SurfaceY ?? NoSurfaceRecorded);
                flat.Add(column.SampledBlocks);
                flat.Add(column.RestsOnBedrock.HasValue ? (column.RestsOnBedrock.Value ? 2 : 1) : 0);
            }
            this.SweepColumns = flat;

            var cursor = record.SweepCursorFor(this.Id);
            this.SweepPlotIndex = cursor.PlotIndex;
            this.SweepColumnCursor = cursor.ColumnCursor;
        }

        /// <summary>This area's persisted per-column pass record, unflattened.</summary>
        public IEnumerable<SurveyColumnState> ReadSweepColumns()
        {
            for (var i = 0; i + 4 < this.SweepColumns.Count; i += 5)
            {
                var surface = this.SweepColumns[i + 2];
                var bedrock = this.SweepColumns[i + 4];
                yield return new SurveyColumnState(
                    this.SweepColumns[i],
                    this.SweepColumns[i + 1],
                    surface == NoSurfaceRecorded ? (int?)null : surface,
                    this.SweepColumns[i + 3],
                    bedrock == 0 ? (bool?)null : bedrock == 2);
            }
        }

        /// <summary>
        /// Rebuilds <paramref name="record"/>'s view of this area from what is persisted — the
        /// pass's per-column samples, its findings rows, and its sweep cursor — so a resumed pass
        /// carries the coverage and the findings the stopped one reached (R25).
        ///
        /// Restoring the findings rows is not decoration. The dock projects the LIVE record back
        /// onto this area every readout tick, replacing what is stored; a record rehydrated with
        /// samples but no findings would report coverage without material and overwrite a
        /// perfectly good snapshot with an empty one on the first tick after a restart.
        /// </summary>
        public void RestoreInto(SurveyRecord record)
        {
            if (record == null)
                return;

            // Findings first: reading them applies the KTD1 shape upgrade, which on a pre-U1 save
            // clears this area — including the sweep rows read below.
            var rows = this.ReadFindingRows().ToList();
            var columns = this.ReadSweepColumns().ToList();

            record.ClearArea(this.Id);
            foreach (var column in columns)
                record.RestorePassColumn(this.Id, column);
            foreach (var row in rows)
                record.RestoreFinding(this.Id, row);
            record.SetSweepCursor(this.Id, this.SweepPlotIndex, this.SweepColumnCursor);
        }

        /// <summary>
        /// Discards findings written in a pre-U1 shape (KTD1). Called before every read, so the
        /// upgrade happens on the first read after a load and never again. Findings stored per
        /// area cannot be attributed to a plot, so there is nothing to migrate — the area reads as
        /// unsurveyed and is surveyed once more.
        /// </summary>
        private void UpgradeFindingsIfStale()
        {
            if (!FindingsVersion.IsStale(this.FindingsShapeVersion))
                return;

            this.ClearFindings();
        }

        /// <summary>
        /// Replaces this area's persisted surveyed stamps from the live accumulator's
        /// current snapshot. Skips the write when <paramref name="stamps"/> is empty, so a
        /// just-restarted, not-yet-repopulated accumulator never overwrites a populated
        /// persisted snapshot before the drone has re-surveyed.
        /// </summary>
        public void SetSurveyedStamps(PlotStampAccumulator stamps)
        {
            if (stamps == null || stamps.IsEmpty)
                return;

            var flat = new ThreadSafeList<long>();
            foreach (var entry in stamps.Snapshot())
            {
                flat.Add(entry.Key.X);
                flat.Add(entry.Key.Z);
                flat.Add(entry.Value);
            }
            this.SurveyedStamps = flat;
        }

        /// <summary>This area's persisted surveyed stamps, rehydrated into a live accumulator.</summary>
        public PlotStampAccumulator ReadSurveyedStamps()
        {
            var entries = new Dictionary<PlotCoord, long>();
            for (var i = 0; i + 2 < this.SurveyedStamps.Count; i += 3)
                entries[new PlotCoord((int)this.SurveyedStamps[i], (int)this.SurveyedStamps[i + 1])] = this.SurveyedStamps[i + 2];
            return PlotStampAccumulator.FromSnapshot(entries);
        }

        /// <summary>
        /// Records <paramref name="plot"/> surveyed at <paramref name="stampValue"/> and persists
        /// it immediately, mirroring <see cref="RecordMinedPlot"/>.
        ///
        /// <para>
        /// Clearing any re-reading mark on the plot happens here, in the same step (R9), and not
        /// as a separate call the caller has to remember. A mark says "what is recorded for this
        /// plot can no longer be trusted", and this method is the moment a new reading supersedes
        /// what was recorded — so the mark and the reading it qualified go together or the plot
        /// stays marked forever despite having just been read.
        /// </para>
        /// </summary>
        public void RecordSurveyedPlot(PlotCoord plot, long stampValue)
        {
            // Under the lock because reading the stamps into an accumulator, recording into it and
            // storing it back is a read-modify-write (R10), and because the mark must be cleared
            // in the same atomic step as the reading that supersedes it -- not in a second one a
            // concurrent writer could interleave with.
            lock (AreaDataLock)
            {
                var accumulator = this.ReadSurveyedStamps();
                accumulator.Record(plot, stampValue);
                this.SetSurveyedStamps(accumulator);
                this.ClearReReadingMark(plot);
            }
        }

        /// <summary>
        /// Replaces this area's persisted mined stamps from the live accumulator's current
        /// snapshot. The exact mirror of <see cref="SetSurveyedStamps"/>, empty guard included:
        /// an empty accumulator never overwrites a populated persisted snapshot.
        /// </summary>
        public void SetMinedStamps(PlotStampAccumulator stamps)
        {
            if (stamps == null || stamps.IsEmpty)
                return;

            var flat = new ThreadSafeList<long>();
            foreach (var entry in stamps.Snapshot())
            {
                flat.Add(entry.Key.X);
                flat.Add(entry.Key.Z);
                flat.Add(entry.Value);
            }
            this.MinedStamps = flat;
        }

        /// <summary>
        /// This area's persisted mined stamps, rehydrated into a live accumulator (U2, R1).
        /// Every dock reading this area gets the same answer, which is the whole point of the
        /// record having moved here: <see cref="PlotFreshness.IsMineable"/> now compares two
        /// stamps that both came off one object.
        /// </summary>
        public PlotStampAccumulator ReadMinedStamps()
        {
            var entries = new Dictionary<PlotCoord, long>();
            for (var i = 0; i + 2 < this.MinedStamps.Count; i += 3)
                entries[new PlotCoord((int)this.MinedStamps[i], (int)this.MinedStamps[i + 1])] = this.MinedStamps[i + 2];
            return PlotStampAccumulator.FromSnapshot(entries);
        }

        /// <summary>
        /// Replaces this area's persisted at-bedrock plots with what the pass just observed
        /// (U4). <paramref name="plots"/> are the ones <see cref="SurveyRecord.BedrockPlots"/>
        /// projects — every column in each of them walked down to the world floor.
        ///
        /// Deliberately UNGUARDED, unlike <see cref="SetSurveyedStamps"/> and
        /// <see cref="SetMinedStamps"/>: an empty result here is a real answer ("this pass found
        /// nothing at bedrock"), not an unpopulated accumulator. R8 requires a survey that finds
        /// ground standing above bedrock to stop restating `[cleared]`, and an empty-write guard
        /// would make a once-cleared area permanently cleared — exactly the stored-flag
        /// behaviour KTD3 rejects. The caller only reaches this once the pass has coverage.
        /// </summary>
        public void SetBedrockPlots(IEnumerable<PlotCoord> plots)
        {
            this.BedrockPlotCoords = FlattenPlots(plots);
        }

        /// <summary>This area's persisted at-bedrock plots (unflattening the pairs).</summary>
        public IEnumerable<PlotCoord> ReadBedrockPlots()
        {
            foreach (var plot in UnflattenPlots(this.BedrockPlotCoords)) yield return plot;
        }

        /// <summary>
        /// Records that <paramref name="plot"/> needs re-reading, if this area covers it and it is
        /// not already recorded. Returns true when the list actually changed, so a caller can tell
        /// whether this was new information.
        /// </summary>
        public bool MarkPlotForReReading(PlotCoord plot)
        {
            // Under the lock because this is a check followed by an act (R10): without it, two
            // callers can both find the plot unmarked and both append it, leaving a duplicate pair
            // that every later scan has to step over.
            lock (AreaDataLock)
            {
                if (!this.CoversPlot(plot)) return false;
                if (this.PlotNeedsReReading(plot)) return false;

                this.PlotsNeedingReReading.Add(plot.X);
                this.PlotsNeedingReReading.Add(plot.Z);
                return true;
            }
        }

        /// <summary>
        /// Clears the re-reading mark on <paramref name="plot"/>, if it carries one. Called when a
        /// survey pass reads the plot again, in the same step that records the new reading, so the
        /// mark goes away exactly when the reading that superseded it is stored (R9).
        /// </summary>
        public bool ClearReReadingMark(PlotCoord plot)
        {
            // Under the lock because this reads the whole list, builds a replacement, and stores
            // it back (R10). Without it, a mark recorded between the read and the store is lost.
            lock (AreaDataLock)
            {
                var kept = new ThreadSafeList<int>();
                var removed = false;

                for (var i = 0; i + 1 < this.PlotsNeedingReReading.Count; i += 2)
                {
                    if (this.PlotsNeedingReReading[i] == plot.X && this.PlotsNeedingReReading[i + 1] == plot.Z)
                    {
                        removed = true;
                        continue;
                    }

                    kept.Add(this.PlotsNeedingReReading[i]);
                    kept.Add(this.PlotsNeedingReReading[i + 1]);
                }

                if (removed) this.PlotsNeedingReReading = kept;
                return removed;
            }
        }

        /// <summary>True when <paramref name="plot"/> is recorded as needing re-reading.</summary>
        public bool PlotNeedsReReading(PlotCoord plot)
        {
            for (var i = 0; i + 1 < this.PlotsNeedingReReading.Count; i += 2)
                if (this.PlotsNeedingReReading[i] == plot.X && this.PlotsNeedingReReading[i + 1] == plot.Z)
                    return true;
            return false;
        }

        /// <summary>This area's plots recorded as needing re-reading (unflattening the pairs).</summary>
        public IEnumerable<PlotCoord> ReadPlotsNeedingReReading()
        {
            for (var i = 0; i + 1 < this.PlotsNeedingReReading.Count; i += 2)
                yield return new PlotCoord(this.PlotsNeedingReReading[i], this.PlotsNeedingReReading[i + 1]);
        }

        /// <summary>True when any plot of this area is recorded as needing re-reading.</summary>
        public bool AnyPlotNeedsReReading => this.PlotsNeedingReReading.Count > 0;

        /// <summary>True when the last survey observed <paramref name="plot"/> down at bedrock.</summary>
        public bool PlotRestsOnBedrock(PlotCoord plot)
        {
            for (var i = 0; i + 1 < this.BedrockPlotCoords.Count; i += 2)
                if (this.BedrockPlotCoords[i] == plot.X && this.BedrockPlotCoords[i + 1] == plot.Z)
                    return true;
            return false;
        }

        /// <summary>
        /// True when EVERY plot of this area was observed down at bedrock — the floor test R7
        /// and R26 put `[cleared]` and `[empty]` behind. False for an area with no plots, which
        /// is a degenerate area rather than an exhausted one.
        /// </summary>
        public bool RestsOnBedrock()
        {
            if (this.PlotCount == 0)
                return false;

            foreach (var plot in this.Plots())
                if (!this.PlotRestsOnBedrock(plot))
                    return false;
            return true;
        }

        /// <summary>
        /// Records <paramref name="plot"/> mined at <paramref name="stampValue"/> and persists it
        /// immediately, mirroring <see cref="RecordSurveyedPlot"/>. Unlike the survey side there
        /// is no live/throttled projection step, since a mined stamp is written once per plot
        /// rather than accumulated per column.
        /// </summary>
        public void RecordMinedPlot(PlotCoord plot, long stampValue)
        {
            // Under the lock for the same reason as the surveyed stamps (R10), and with more
            // reason: this one is reached from a mining dock that does not own the area, so two
            // different docks can be recording into the same area at the same moment.
            lock (AreaDataLock)
            {
                var accumulator = this.ReadMinedStamps();
                accumulator.Record(plot, stampValue);
                this.SetMinedStamps(accumulator);
            }
        }

        /// <summary>
        /// The area totals — one finding per ore across the whole area — re-derived from the
        /// per-plot rows at read time (KTD1), in the Eco-free shape the readout formatter
        /// consumes. This is what the survey tab, the roster line and the chat readouts show, and
        /// the figures are the same ones they showed before findings became per-plot.
        /// </summary>
        public IEnumerable<SurveyFinding> ReadFindings() => SurveyRecord.AreaTotals(this.ReadFindingRows());

        /// <summary>
        /// Every persisted per-plot row, unfolded — the shape the record itself projects (KTD1),
        /// before <see cref="SurveyRecord.AreaTotals"/> folds it for display. This is what
        /// <see cref="RestoreInto"/> hands back to the live record, and it is the read that
        /// applies the KTD1 shape upgrade for every other reader.
        /// </summary>
        public IEnumerable<SurveyFinding> ReadFindingRows()
        {
            this.UpgradeFindingsIfStale();
            return this.Findings.Select(s => s.ToSurveyFinding(this.Id)).ToList();
        }

        /// <summary>
        /// The rows for one plot — the per-plot read R16's invalidation and R20's preservation are
        /// decided against. Empty for a plot this area has no findings for.
        /// </summary>
        public IEnumerable<SurveyFinding> ReadFindings(PlotCoord plot)
        {
            this.UpgradeFindingsIfStale();
            return this.Findings
                .Where(s => s.PlotX == plot.X && s.PlotZ == plot.Z)
                .Select(s => s.ToSurveyFinding(this.Id))
                .ToList();
        }

        /// <summary>The stored plots as <see cref="PlotCoord"/>s (unflattening the pairs).</summary>
        public IEnumerable<PlotCoord> Plots()
        {
            foreach (var plot in UnflattenPlots(this.PlotCoords)) yield return plot;
        }

        // ---------------------------------------------------------------
        // The one flatten/unflatten pair. Three different lists on this class store plots as
        // consecutive (x, z) ints because Eco's Vector2i is not [Serialized] -- the area's own
        // geometry, its at-bedrock observations and the contested plots of a reconciliation
        // block -- and each had grown its own copy of the same two loops. One pair, so the
        // stride, the raster order and the treatment of a trailing unpaired int cannot differ
        // between them.
        // ---------------------------------------------------------------

        /// <summary>
        /// A flattened (x, z) list as <see cref="PlotCoord"/>s, in stored order. A trailing int
        /// with no partner is not half a plot and is passed over.
        /// </summary>
        private static IEnumerable<PlotCoord> UnflattenPlots(ThreadSafeList<int> coords)
        {
            for (var i = 0; i + 1 < coords.Count; i += 2)
                yield return new PlotCoord(coords[i], coords[i + 1]);
        }

        /// <summary>Plots flattened to the consecutive (x, z) pairs the serializer can carry.</summary>
        private static ThreadSafeList<int> FlattenPlots(IEnumerable<PlotCoord> plots)
        {
            var flat = new ThreadSafeList<int>();
            foreach (var p in plots)
            {
                flat.Add(p.X);
                flat.Add(p.Z);
            }
            return flat;
        }

        /// <summary>Projects this entry into the Eco-free <see cref="SurveyArea"/> for membership and cap logic (U2).</summary>
        public SurveyArea ToSurveyArea() => new SurveyArea(this.Id, this.Name, this.Plots());
    }
}
