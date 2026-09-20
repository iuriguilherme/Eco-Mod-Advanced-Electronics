using AdvancedElectronics.Navigation;
using Eco.Core.Utils;
using Eco.Shared.Serialization;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// The save fold from 0.3.0's dock-level mined stamps to 0.4.0's area-level record (U2).
    ///
    /// <para>
    /// This file exists for one release. When no 0.3.0 save is plausibly still being updated,
    /// the legacy member and the fold both go, and the only cost of removing them is that a
    /// save older than that can no longer be carried forward.
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
    }
}
