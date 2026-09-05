using System;
using System.Collections.Generic;
using AdvancedElectronics.Navigation;
using Eco.Shared.Localization;
using Eco.Shared.Logging;
using Eco.Shared.Math;
using Eco.Shared.View;
using Eco.Shared.Voxel;
using EcoWorld = Eco.World.World;

namespace Eco.Mods.TechTree
{
    // Reacting to ground one of THIS MOD'S OWN drones changed (R1, R3, R4).
    //
    // The engine fires World.OnTopBlockChanged from its own block-write path, so every writer in
    // the world reaches it -- a player digging, an administrator command, a map-editor paste, and
    // the mod's own drones alike. It reports the changed COLUMN, which is the shape an area
    // already records.
    //
    // What it does not report is who wrote. That is why the mod marks its own writes as it makes
    // them (ModGroundWrite, opened in MiningStrategy and FarmingStrategy around their block
    // writes) and everything unmarked reads as not ours.
    //
    // Only a write the mod can attribute to one of its own drones causes any reaction at all. This
    // mod does not monitor the world for changes it did not make: the engine raises a signal only
    // when the topmost block of a column changes, so a player tunnelling underground is invisible
    // here, and reacting to the fraction of outside changes that happen to surface would produce
    // an arbitrary picture rather than a current one. An outside change is learned in situ
    // instead, by the mining drone that reaches the work site and finds the world does not match
    // what the survey reported.
    //
    // The safe default therefore runs the opposite way from what it once did. A write the mod
    // forgets to mark now reads as not ours and is ignored, which leaves the survey slightly out
    // of date until a drone discovers the discrepancy; previously the same slip deleted the
    // survey results for the plots it touched.
    public partial class DroneDockObject
    {
        /// <summary>
        /// This dock's handler on the engine's static top-block-changed event, held so it can be
        /// detached again (KTD7).
        ///
        /// <para>
        /// The event is STATIC, so a dock that subscribes and is then picked up stays reachable
        /// from it forever -- the dock, its areas, its findings and its live survey record with
        /// it -- and every later block write in the world keeps calling into a destroyed object.
        /// Holding the delegate is what makes the detach exact rather than best-effort: a fresh
        /// method group would still match by target and method, but nothing then proves the
        /// subscribe and the detach are talking about the same thing.
        /// </para>
        /// <para>
        /// Not <c>[Serialized]</c>, and it must not be: it is a live delegate, re-created by
        /// <see cref="SubscribeToGroundChanges"/> on every server start because
        /// <c>Initialize</c> runs per object on every start.
        /// </para>
        /// </summary>
        private Action<Vector2i, int> groundChangeHandler;

        /// <summary>
        /// Attaches this dock to the engine's block-write notification (U8 step 1) and registers
        /// the matching detach on the dock's destroy path (KTD7). Idempotent, so a second call
        /// cannot leave two handlers on a static event.
        ///
        /// <para>
        /// Called LAST in <c>Initialize</c> on purpose. A throw earlier in initialization aborts
        /// the rest of it and leaves a half-built object behind
        /// (docs/solutions/runtime-errors/initialize-exception-leaves-a-half-built-worldobject.md);
        /// subscribing last means such a dock is never on the event at all, which is the harmless
        /// direction.
        /// </para>
        /// <para>
        /// <b>Why the release rides on the dock's subscription list rather than an
        /// <c>OnDestroy</c> override.</b> <c>WorldObject.OnDestroy</c> is already overridden for
        /// this class, in the farming partial, to release its linked-storage watch -- and a
        /// partial class gets one override. <c>WorldObject.OnDestroy</c> opens by calling
        /// <c>UnsubscribeAll()</c> over exactly this list, and nothing else on a world object ever
        /// calls it, so a subscription registered here is released on the destroy path and only
        /// there. That is the same guarantee an override would give, taken from the engine's own
        /// destroy sequence instead of from a second override that cannot exist.
        /// </para>
        /// </summary>
        private void SubscribeToGroundChanges()
        {
            if (this.groundChangeHandler != null) return;

            this.groundChangeHandler = this.OnTopBlockChanged;
            EcoWorld.OnTopBlockChanged.Add(this.groundChangeHandler);
            this.AddSubscription(new GroundChangeSubscription(this));
        }

        /// <summary>
        /// Detaches this dock from the engine's block-write notification (KTD7). Reached from the
        /// dock's destroy path, and safe to call when nothing was ever attached.
        /// </summary>
        private void ReleaseGroundChangeSubscription()
        {
            if (this.groundChangeHandler == null) return;

            EcoWorld.OnTopBlockChanged.Remove(this.groundChangeHandler);
            this.groundChangeHandler = null;
        }

        /// <summary>
        /// The dock's ground-change watch expressed as an engine subscription, so
        /// <c>WorldObject.OnDestroy</c>'s own <c>UnsubscribeAll()</c> releases it (KTD7).
        /// </summary>
        private sealed class GroundChangeSubscription : ISubscription
        {
            private readonly DroneDockObject dock;

            public GroundChangeSubscription(DroneDockObject dock) => this.dock = dock;

            public void Unsubscribe() => this.dock.ReleaseGroundChangeSubscription();
        }

        /// <summary>
        /// One changed world column, from the engine's own block-write path.
        ///
        /// <para>
        /// This runs on every top-block change in the world, so the gates are ordered cheapest
        /// first: a destroyed dock, then a dock with no areas, then whether the write is even
        /// one of this mod's own (which decides almost every call and costs one field read),
        /// then per area the flat <see cref="SurveyAreaEntry.HasSurveyState"/> check (an area no
        /// pass has touched has nothing a mark could qualify), then the plot scan.
        /// </para>
        /// <para>
        /// The engine may fire this several times for one column -- digging a shaft down changes
        /// the top block at every layer -- so the reaction has to be idempotent rather than
        /// coalesced. It is: marking a plot that is already marked changes nothing and reports
        /// that it changed nothing, so the second and later firings do no work.
        /// </para>
        /// <para>
        /// Nothing may escape from here. An exception on the engine's block-write path would take
        /// out the write itself, so a fault in this mod would break ordinary digging for every
        /// player on the server.
        /// </para>
        /// </summary>
        /// <param name="column">The changed column (x, z). The engine's second argument is the previous top height, which nothing here needs.</param>
        private void OnTopBlockChanged(Vector2i column, int _)
        {
            try
            {
                this.ReactToGroundChange(column.X, column.Y);
            }
            catch (Exception e)
            {
                Log.WriteErrorLineLoc($"Drone Dock: failed to react to a ground change at ({column.X}, {column.Y}): {e}");
            }
        }

        private void ReactToGroundChange(int worldX, int worldZ)
        {
            if (this.IsDestroyed || this.SurveyAreas.Count == 0) return;

            var attribution = ModGroundWrite.Current;

            // R1. A write this mod cannot attribute to one of its own drones is ignored entirely,
            // and the check comes before anything else because it decides almost every call. A
            // player digging, an administrator command, a map-editor paste and a rebuild of the
            // engine's block caches all land here, and none of them causes this mod to read or
            // write a single byte of an area's stored data.
            //
            // This mod does not monitor the world for changes it did not make. The engine raises a
            // signal only when the topmost block of a column changes, so reacting to the fraction
            // of outside changes that happen to surface would produce an arbitrary picture rather
            // than a current one. Such a change is learned in situ instead: a mining drone reaching
            // the work site finds that the world does not match what the survey reported.
            if (!attribution.IsModsOwn) return;

            var plot = GroundChange.PlotOf(worldX, worldZ, PlotUtil.PropertyPlotLength);
            var ownerId = this.ObjectID.ToString();

            foreach (var entry in this.SurveyAreas)
            {
                if (entry == null || !entry.HasSurveyState || !entry.CoversPlot(plot)) continue;

                // R16 / R17 / R43: what this write means for THIS area. Its own drone's work is
                // recorded by the mined stamps and keeps its findings; a write serving an area of
                // another kind is not this area's business at all; everything else invalidates.
                // Reaching this point means the write is one of this mod's own, because an
                // unattributed write returned above.
                if (!GroundChange.RequiresReaction(
                        GroundChange.VerdictFor(attribution, ownerId, entry.Id, entry.Kind)))
                    continue;

                // R4. Mark the plot for re-reading rather than destroying what the survey found.
                // Every stored result on this area survives -- its ore findings, its surveyed
                // timestamp, its bedrock observation, its mined timestamp and its pass record --
                // so a later survey can confirm or replace them cheaply instead of rebuilding
                // them from nothing, and a wrong judgement here costs redundant work rather than
                // lost data.
                if (!entry.MarkPlotForReReading(plot)) continue;

                // R5. The live sampled set has to lose this plot at the same moment. That record
                // exists to stop the current pass re-reading ground it has already read, so a plot
                // left in it is skipped by every later pass -- the drone would fly back and record
                // nothing. Dropping it destroys no survey result: it is in-memory de-duplication
                // state, not stored findings.
                this.surveyRecord?.ForgetPlot(entry.Id, plot);
            }
        }
    }
}
