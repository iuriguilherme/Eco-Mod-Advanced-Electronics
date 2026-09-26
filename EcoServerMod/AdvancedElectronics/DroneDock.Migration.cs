using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedElectronics.Navigation;
using Eco.Core.Utils;
using Eco.Shared.Logging;
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

                    // Advance the counter with each area, not once when the loop finishes. The
                    // per-dock catch in ModRegistration lets the world load after a fold throws,
                    // so a half-finished fold PERSISTS: the areas already added are on disk, the
                    // rows already consumed are gone, and a counter bumped only at the end would
                    // still point at an id this loop has already handed out. The next load skips
                    // the folded rows correctly -- their markers are set -- and then mints the
                    // remaining ones from that stale counter, straight onto an existing area.
                    // Two areas would share an id, and every id-keyed lookup on the dock resolves
                    // ambiguously from then on. That is the collision KTD3 exists to prevent,
                    // arriving by a different door.
                    this.nextAreaId = Math.Max(this.nextAreaId, folded.AreaId + 1);

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
                // later area cannot collide with one this fold placed. The per-area bump above
                // already covers every area that landed; this closes the gap for rows the fold
                // counted but declined to place.
                this.nextAreaId = Math.Max(this.nextAreaId, fold.NextAreaId);
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

        // ---------------------------------------------------------------
        // U5: reconciling a save that already conflicts (R13, R14, R15, R16).
        //
        // The fold above is what makes this judgeable at all -- an unfolded farm is invisible to
        // the claim system, so a pass run before it would find a world with no farms in it and
        // leave every mining-versus-farming conflict standing. ModRegistration sequences the two
        // and is the only caller.
        //
        // It sits in this file rather than beside the assignment paths because it is the same
        // kind of thing as the folds: a once-per-load correction to saves written before a rule
        // existed. Unlike them it does not go away -- the rule it enforces is permanent, and a
        // world can be brought into conflict by a mod update at any time.
        // ---------------------------------------------------------------

        /// <summary>
        /// Undoes every assignment this world is not entitled to keep, records why on the area,
        /// and lets the drone that was working it come home (R13, R14, R15).
        ///
        /// <para>
        /// <b>It decides nothing.</b> Which assignments are illegal, which side of a collision
        /// gives way, and which plots the record names are all
        /// <see cref="AreaReconciliation.AssignmentsToUndo"/>'s, in the Eco-free navigation
        /// assembly where they are tested. What is here is the three effects that answer cannot
        /// perform: dropping the claim, writing the reason, and telling the player.
        /// </para>
        /// <para>
        /// <b>Nothing is written when nothing conflicts (R16).</b> The pass costs one world walk
        /// and one overlap scan per held area, and an ordinary load returns an empty list and
        /// stops here. In particular a block recorded by an EARLIER load is not cleared: the
        /// record is untrue only once the area is assigned again or its kind changes, and
        /// <see cref="SurveyAreaEntry.ClearReconciliationBlock"/> belongs to those acts rather
        /// than to this one.
        /// </para>
        /// <para>
        /// <b>The drone is recalled by the assignment it no longer has (R14).</b> There is no
        /// separate recall call and there must not be: <c>DroneLifecycle</c> re-reads its dock's
        /// assignment token every tick and flies a drone home the moment that token goes empty,
        /// which is the same path the Unassign button takes. Each of the three releases below
        /// changes that token. A second, direct recall would be a second way to stop a drone,
        /// and the two would drift.
        /// </para>
        /// <para>
        /// <b>Seam.</b> No unit tests, and none are coming: this holds Eco types and the test
        /// project references only <c>AdvancedElectronics.Navigation</c>. The decision half is
        /// covered there case by case in <c>AreaReconciliationTests</c>; what is untested is
        /// exactly this -- the lookup from a projection's identity back to the entry, the three
        /// release paths, and the record. Those are proven in the batched live session
        /// (docs/solutions/workflow-issues/eco-mod-batched-live-testing.md) against a save built
        /// to hold a known conflict.
        /// </para>
        /// </summary>
        /// <param name="docks">
        /// Every live dock in the world, materialised by the caller -- the same list the fold
        /// walked. It is what turns a projection's (dock, area) identity back into the entry to
        /// write, and R41 is why the identity is all that crosses.
        /// </param>
        public static void ReconcileAreaClaims(IReadOnlyList<DroneDockObject> docks)
        {
            if (docks == null || docks.Count == 0) return;

            // The RAW projection set (KTD8), unfiltered by owner and by radius: two areas collide
            // however far apart their docks sit and whoever owns them.
            //
            // Built from the list this method was handed rather than from a second world walk:
            // the caller materialised exactly this set to run the fold over, and the projection
            // reads each dock's areas live, so the fold's writes are in it either way.
            var undone = AreaReconciliation.AssignmentsToUndo(MiningComponent.AllAreaProjections(docks));
            if (undone.Count == 0) return;

            foreach (var assignment in undone)
            {
                var owner = docks.FirstOrDefault(d => d.ObjectID == assignment.Area.OwningDockId);
                var area = owner?.SurveyAreas.FirstOrDefault(a => a.Id == assignment.Area.AreaId);

                // The world moved between the walk and here, which nothing in this pass can
                // prevent and nothing needs to: an area that is gone holds no ground.
                if (area == null) continue;

                // The same lock every other claim release takes (KTD6), and reentrant, so the
                // paths below may take it again. Releasing the claim and recording why it went
                // is one operation: an area that has been released but not yet recorded reads as
                // an assignment the player dropped themselves.
                lock (AreaClaimLock)
                {
                    ReleaseReconciledClaim(area, owner, docks.FirstOrDefault(d => area.IsClaimedBy(d.ObjectID)));
                    area.RecordReconciliationBlock(assignment.Reason, assignment.ContestedPlots);
                }

                // Named in the log because the player is not here to be told: the tab says it on
                // their next visit (R15), and until then the server log is the only account of a
                // load having taken an assignment away. It names this side only -- the holder is
                // another player's area, and R41 keeps it out of a channel it does not have to
                // cross.
                Log.WriteLineLoc(
                    $"Advanced Electronics: unassigned '{owner.Name} -- {area.Name}' at world load. It held {assignment.ContestedPlots.Count} plot(s) another area is entitled to ({assignment.Reason}). The dock's drone returns home and the area reads blocked until it is assigned somewhere clear.");
            }
        }

        /// <summary>
        /// Drops the claim <paramref name="area"/> is not entitled to, through whichever
        /// assignment took it.
        ///
        /// <para>
        /// Three paths because a claim is taken three ways, and each has state beside the claim
        /// that has to go with it. A mining assignment lives on the HOLDING dock as a cross-dock
        /// reference and carries a job; a survey assignment lives on the owning dock as an id; a
        /// farming assignment is the claim itself, with only the dock's epoch beside it. Dropping
        /// the claim alone would leave the first two docks pointed at an area they no longer
        /// hold, which is the one state R37 does not allow -- a drone works only plots its own
        /// dock has claimed.
        /// </para>
        /// <para>
        /// The last path is also the fallback for a claim whose dock-side assignment cannot be
        /// found: the holder may have been destroyed, or reassigned since the walk. The claim is
        /// the thing that holds ground, so the claim is what must go regardless.
        /// </para>
        /// </summary>
        private static void ReleaseReconciledClaim(
            SurveyAreaEntry area, DroneDockObject owner, DroneDockObject holder)
        {
            // Mining. Goes through the dock's own unassign so the job ends and the epoch moves
            // exactly as they do when a player presses the button -- a job outlives its
            // assignment otherwise and the panel keeps reporting work on ground the dock lost.
            if (holder?.AssignedMiningArea is { } mining
                && mining.OwningDockId == owner.ObjectID
                && mining.AreaId == area.Id)
            {
                holder.UnassignMiningArea();
                return;
            }

            // Survey. A survey dock only ever assigns its OWN areas, so the holder and the owner
            // are the same dock here; id 0 is that path's unassign.
            if (holder != null && holder.ObjectID == owner.ObjectID && holder.AssignedSurveyAreaId == area.Id)
            {
                holder.AssignSurveyArea(0, out _, out _);
                return;
            }

            // Farming, and every claim whose dock-side assignment no longer resolves.
            area.ReleaseClaim();

            // The farm side's assignment token folds this epoch in, so moving it is what tells
            // the lifecycle the assignment changed (R14). Reachable from here because this file
            // is the same partial class the field is declared in.
            if (holder != null) holder.farmAssignmentEpoch++;
        }
    }
}
