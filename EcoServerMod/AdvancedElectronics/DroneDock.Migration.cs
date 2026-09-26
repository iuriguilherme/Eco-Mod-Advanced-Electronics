using System.Linq;
using AdvancedElectronics.Navigation;
using Eco.Core.Utils;
using Eco.Shared.Serialization;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// The save folds this release carries: 0.3.0's dock-level mined stamps onto the area-level
    /// record (U2), and the separate per-dock farm collection onto the one area collection that
    /// now holds mines and farms alike (U3, R17, R18).
    ///
    /// <para>
    /// This file exists for as long as saves written before those changes are still being carried
    /// forward. When no such save is plausibly still being updated, the legacy members and the
    /// folds both go, and the only cost of removing them is that a save older than that can no
    /// longer be carried forward. Nothing else in the mod reads them.
    /// </para>
    ///
    /// <para>
    /// <b>Uncovered by unit tests, by design.</b> Everything in this file holds Eco types, and the
    /// test project references only the Eco-free <c>AdvancedElectronics.Navigation</c> assembly
    /// (<c>AdvancedElectronics.Navigation.Tests.csproj</c>), so none of it can be unit-tested
    /// here. The arithmetic each fold performs is pulled out into that assembly precisely so it
    /// CAN be — <see cref="LegacyMinedStamps"/> and <see cref="LegacyFarmAreas"/> are covered
    /// there. What is left in this file is the read of the legacy member, the write of the new
    /// one, and the emptying, and that seam is proven in the batched live session
    /// (docs/solutions/workflow-issues/eco-mod-batched-live-testing.md) rather than by a test.
    /// </para>
    /// </summary>
    public partial class DroneDockObject
    {
        /// <summary>
        /// 0.3.0's mined-plot stamps, in its own flat <c>(x, z, stamp)</c> triple shape.
        ///
        /// <para>
        /// <b>Legacy. Nothing writes this.</b> It is declared solely so an old save's value has
        /// a member to load back into: <c>[Serialized]</c> means "save this and load it back",
        /// and loading it back is a write
        /// (docs/solutions/conventions/serialized-needs-a-member-to-write-back-into.md). A
        /// stored value whose member the class has dropped has nowhere to land.
        /// </para>
        ///
        /// <para>
        /// <b>The name is the binding.</b> Eco serializes to BSON, which is keyed by member
        /// name, so this has to keep 0.3.0's spelling — <c>MinedStamps</c> — or the stored
        /// field matches nothing and the value is dropped on load. It is the dock's namesake of
        /// <see cref="SurveyAreaEntry.MinedStamps"/>, which is where the record lives now.
        /// </para>
        ///
        /// <para>
        /// <see cref="MigrateLegacyMinedStamps"/> empties it once its contents have reached the
        /// area, so a dock that has been folded carries an empty list from then on and a fresh
        /// 0.4.0 world never populates it at all.
        /// </para>
        /// </summary>
        [Serialized] public ThreadSafeList<long> MinedStamps { get; set; } = new();

        /// <summary>
        /// The farms this dock held while farms were their own collection and their own type.
        ///
        /// <para>
        /// <b>Legacy. Nothing writes this any more.</b> Same reason as
        /// <see cref="MinedStamps"/> above, and same mechanics: a save written before U3 holds a
        /// <c>FarmAreas</c> field inside its serialized <c>DroneDockObject</c> document, keyed by
        /// that exact name, and a class that stopped declaring the member would give that field
        /// nowhere to land. Farms now live in <see cref="SurveyAreas"/> as
        /// <see cref="SurveyAreaEntry"/> values carrying <see cref="AreaKind.Farming"/> (R17), and
        /// <see cref="CreateFarmArea"/> writes them there.
        /// </para>
        ///
        /// <para>
        /// <see cref="FarmAreaEntry"/> stays declared for the same reason the member does — the
        /// stored documents are keyed to that type, so it has to exist for the rows inside this
        /// list to deserialize at all. Both it and this member can go once no pre-fold save is
        /// plausibly still being carried forward, and not before.
        /// </para>
        ///
        /// <para>
        /// <see cref="MigrateLegacyFarmAreas"/> removes each row as it merges, so a dock that has
        /// been folded carries an empty list from then on, and a dock holding a row the fold could
        /// not place keeps exactly that row (KTD4) rather than losing it to a wholesale clear.
        /// </para>
        /// </summary>
        [Serialized] public ThreadSafeList<FarmAreaEntry> FarmAreas { get; private set; } = new();

        // The legacy farm id counter, monotonic and separate from the survey side's while the two
        // were different collections. Legacy in exactly the sense FarmAreas above is: nothing
        // writes it, and it is declared only so an old save's field lands somewhere. Farm ids are
        // minted from nextAreaId now, and the fold renumbers every farm it moves (KTD3).
        //
        // INTERNAL rather than private, and that is not cosmetic: a private field with an
        // initializer and no reader is CS0414, and this one will never have a reader again. The
        // name and the type are what BSON matches on, so both are untouchable; the accessibility
        // is not, and widening it is the one degree of freedom that keeps a deliberately dead
        // landing pad from reading as an accident to the compiler.
        [Serialized] internal int nextFarmAreaId = 1;

        /// <summary>
        /// Folds this dock's 0.3.0 mined stamps onto the area it is assigned to, then empties
        /// the legacy list.
        ///
        /// <para>
        /// Without the fold, a dock carried across the update reads every shaft it had already
        /// finished as fresh ground: <see cref="PlotFreshness.IsMineable"/> compares the area's
        /// surveyed stamp against the area's MINED stamp, and in a 0.3.0 save the area has no
        /// mined stamps at all — they were on the dock. The drone would dig the same plots a
        /// second time.
        /// </para>
        ///
        /// <para>
        /// <b>Nothing is discarded when the area cannot be resolved.</b> This runs off the
        /// mining tab's refresh, which first happens while the world is still loading, and a
        /// dock whose survey dock has not loaded yet is indistinguishable from one whose area
        /// is genuinely gone. Keeping the list costs three numbers per mined plot and lets the
        /// next refresh try again; discarding it on a guess would silently re-dig a worked
        /// area. The list is emptied only on the refresh that actually writes it through.
        /// </para>
        ///
        /// <para>
        /// Idempotent, and safe to run after the area has recorded newer digging:
        /// <see cref="LegacyMinedStamps.MergeInto"/> keeps the higher stamp per plot, so no
        /// plot moves backwards in time.
        /// </para>
        /// </summary>
        public void MigrateLegacyMinedStamps()
        {
            if (this.MinedStamps == null || this.MinedStamps.Count == 0) return;
            if (this.AssignedMiningArea == null) return;
            if (this.AssignedMiningArea.Resolve(out _, out var area) != AreaLookupSignal.Found) return;

            // The area's own lock, for the reason every other mined-stamp write takes it: reading
            // the stamps into an accumulator, merging into it and storing it back is a
            // read-modify-write (R10), and the engine fires ground notifications from a parallel
            // loop.
            lock (SurveyAreaEntry.AreaDataLock)
            {
                var merged = LegacyMinedStamps.MergeInto(this.MinedStamps, area.ReadMinedStamps());
                area.SetMinedStamps(merged);
            }

            this.MinedStamps = new ThreadSafeList<long>();
        }

        /// <summary>
        /// Folds this dock's legacy farm rows into <see cref="SurveyAreas"/> as ordinary areas
        /// carrying <see cref="AreaKind.Farming"/>, removing each row as it merges (U3, R17, R18).
        ///
        /// <para>
        /// Without the fold, a farm carried across the update is invisible to everything this
        /// release builds: the claim system walks <see cref="SurveyAreas"/>, so an unfolded farm
        /// holds no ground, and a neighbouring mining dock may be pointed straight at a player's
        /// crop field with nothing objecting. Folding is what gives a farm the area identity the
        /// enforcement was always written against.
        /// </para>
        ///
        /// <para>
        /// <b>Every early return leaves the legacy list untouched</b>, the discipline
        /// <see cref="MigrateLegacyMinedStamps"/> follows above and for the same reason: a fold
        /// that decided nothing must be indistinguishable from one that never ran, so the next
        /// attempt still has its input.
        /// </para>
        ///
        /// <para>
        /// <b>Rows are removed one at a time, as each merges — the list is never cleared.</b>
        /// That is load-bearing in both directions. Clearing wholesale would discard a row the
        /// fold declined to place, which KTD4 keeps on purpose so the ground it covers is
        /// recoverable rather than silently gone. Not removing at all would re-fold every row on
        /// the next load under fresh ids and duplicate every farm the player owns — and the fold
        /// marker alone cannot prevent that, because a row whose legacy id is non-positive folds
        /// to an area whose <see cref="SurveyAreaEntry.FoldedFromLegacyFarmId"/> is honestly
        /// <c>0</c>, which means "folded from nothing" and marks no row as done. Individual
        /// removal is what closes that case.
        /// </para>
        ///
        /// <para>
        /// <b>Assignment crosses as a claim, not as a field.</b> The old type carried a per-row
        /// <c>Assigned</c> bool; an area carries its assignment as the claim triple, so an
        /// assigned farm is recorded through
        /// <see cref="SurveyAreaEntry.RecordClaim(System.Guid, int, int)"/> at
        /// <see cref="SurveyAreaEntry.ClaimWorkFarming"/>. Neither of the two pre-existing work
        /// values would do: <c>0</c> drops the claim, so a folded farm would stop holding its
        /// plots against a neighbouring dock the moment it was upgraded, and
        /// <see cref="SurveyAreaEntry.ClaimWorkMining"/> would report a mining drone at work on a
        /// crop field.
        /// </para>
        ///
        /// <para>
        /// <b>Seam.</b> This method takes no unit tests and will not gain any: it holds Eco types
        /// and the test project cannot reference this assembly. The decision half — which rows
        /// fold, what id each takes, which members cross, and what a second run does — is
        /// <see cref="LegacyFarmAreas.Fold"/> in the navigation assembly and is covered there
        /// member by member. What is untested is exactly this: the read of the legacy rows, the
        /// write of the areas, the claim, and the removal. Those are proven in the batched live
        /// session against a real pre-fold save.
        /// </para>
        /// </summary>
        public void MigrateLegacyFarmAreas()
        {
            if (this.FarmAreas == null || this.FarmAreas.Count == 0) return;

            // Snapshotted in order before anything is written, for two reasons: the fold's answer
            // must not be able to change underneath the writes, and the removal below walks this
            // same order to find each folded area's source row.
            var legacyRows = this.FarmAreas.ToList();

            // The area's own lock, taken for the reason every other multi-step write on the
            // area collection takes it (R10): appending the areas, recording their claims and
            // dropping the legacy rows is one operation, and a writer landing part-way through
            // would see a dock holding a farm twice or not at all.
            lock (SurveyAreaEntry.AreaDataLock)
            {
                var fold = LegacyFarmAreas.Fold(
                    legacyRows.Select(ToLegacyFarmRow).ToList(),
                    // Every existing area's marker, zeros included. A zero means "folded from
                    // nothing", which is what every area in every pre-fold save correctly reads
                    // as; the fold discards those itself rather than treating farm 0 as done.
                    this.SurveyAreas.Select(a => a.FoldedFromLegacyFarmId).ToList(),
                    this.nextAreaId);

                // Nothing to place. The rows that are still here are ones the fold recognised as
                // already folded, and they stay: this branch merged nothing, so it empties
                // nothing. A dock in this state costs one count check and one fold per load.
                if (fold.Areas.Count == 0) return;

                // One act, one epoch. The epoch exists so a released-and-reclaimed area reads as
                // a new record rather than a resumed one; a fold is a single event that restores
                // however many assignments the dock had, so every area it places shares the epoch
                // that event minted.
                var claimEpoch = ++this.farmAssignmentEpoch;

                // Walks forward through the snapshot alongside the folded areas. The fold returns
                // its areas in input order and only ever drops rows, so the row that produced a
                // folded area is the first one at or after the cursor carrying its consumed id.
                // Matching by id alone would be ambiguous for a row whose id is 0 or negative,
                // which is precisely the case individual removal exists to handle.
                var cursor = 0;

                foreach (var folded in fold.Areas)
                {
                    var entry = ToAreaEntry(folded);
                    this.SurveyAreas.Add(entry);

                    if (folded.Assigned)
                        entry.RecordClaim(this.ObjectID, claimEpoch, SurveyAreaEntry.ClaimWorkFarming);

                    // The null test is not defensive padding: the fold skips a null row rather
                    // than throwing on it, so the snapshot and the folded list stay aligned only
                    // if this scan skips one too.
                    while (cursor < legacyRows.Count
                           && legacyRows[cursor]?.Id != folded.FoldedFromLegacyFarmId)
                        cursor++;

                    if (cursor < legacyRows.Count)
                        this.FarmAreas.Remove(legacyRows[cursor++]);
                }

                // Never lower than it was: the fold hands back the counter it minted from, so a
                // later area cannot collide with one this fold placed.
                this.nextAreaId = fold.NextAreaId;
            }
        }

        /// <summary>
        /// Flattens one legacy farm row into the Eco-free shape the fold decides against.
        ///
        /// <para>
        /// Every argument is passed positionally and none is defaulted, which is deliberate on
        /// the other side of the boundary: <see cref="LegacyFarmRow"/>'s constructor defaults
        /// nothing, so a member added to <see cref="FarmAreaEntry"/> and mirrored there breaks
        /// this call at compile time. That is the only mechanical link available across the
        /// assembly boundary, and it is the whole reason the constructor is shaped that way.
        /// </para>
        /// <para>
        /// A null row maps to a null row rather than throwing. The fold skips nulls, and this
        /// runs at world load where a throw costs the load and leaves the player no way to reach
        /// the dock and repair it — the same reason the fold itself passes over malformed input.
        /// </para>
        /// </summary>
        private static LegacyFarmRow ToLegacyFarmRow(FarmAreaEntry row) => row == null ? null : new LegacyFarmRow(
            row.Id,
            row.Name,
            row.PlotCoords,
            row.Epoch,
            row.Crop,
            row.LevelFirst,
            row.Assigned,
            row.LastStallReason,
            row.LastNextAction,
            row.LastNextDueHours,
            row.LastDueAtWorldSeconds,
            row.LastHeldPlotCount,
            row.LastUnfitCondition,
            row.LastMissingMaterial,
            row.LastFlat,
            row.LevelPassStarted,
            row.LevelTargetHeight,
            row.LevelBankedSpoil);

        /// <summary>
        /// Writes one folded farm out as the area it becomes.
        ///
        /// <para>
        /// <see cref="SurveyAreaEntry.Kind"/> is taken from the fold rather than left alone, and
        /// that is the single highest-consequence line in this file:
        /// <see cref="AreaKind.Mining"/> is <c>0</c>, so an area whose kind is never written
        /// loads as a MINE — it would run the lifecycle ladder instead of reading as farmland, it
        /// would be offered to mining docks, and a neighbouring mining dock could claim the
        /// ground a player's crop is standing in. Nothing would say so.
        /// </para>
        /// <para>
        /// The plots are copied as the flat <c>(x, z)</c> pairs both sides already store, not
        /// rebuilt through <c>SetPlots</c>. Rebuilding would bump the epoch, which has to cross
        /// verbatim so a job built against the old shape can still tell, and would reshape a
        /// trailing half-pair the fold deliberately carries across untouched.
        /// </para>
        /// <para>
        /// <c>Assigned</c> has no counterpart here on purpose — the caller records it as a claim.
        /// </para>
        /// </summary>
        private static SurveyAreaEntry ToAreaEntry(FoldedFarmArea folded) => new SurveyAreaEntry
        {
            Id = folded.AreaId,
            Kind = folded.Kind,
            FoldedFromLegacyFarmId = folded.FoldedFromLegacyFarmId,

            Name = folded.Name,
            PlotCoords = new ThreadSafeList<int>(folded.PlotCoords),
            Epoch = folded.Epoch,
            Crop = folded.Crop,
            LevelFirst = folded.LevelFirst,
            LastStallReason = folded.LastStallReason,
            LastNextAction = folded.LastNextAction,
            LastNextDueHours = folded.LastNextDueHours,
            LastDueAtWorldSeconds = folded.LastDueAtWorldSeconds,
            LastHeldPlotCount = folded.LastHeldPlotCount,
            LastUnfitCondition = folded.LastUnfitCondition,
            LastMissingMaterial = folded.LastMissingMaterial,
            LastFlat = folded.LastFlat,
            LevelPassStarted = folded.LevelPassStarted,
            LevelTargetHeight = folded.LevelTargetHeight,
            LevelBankedSpoil = folded.LevelBankedSpoil,
        };
    }
}
