using System;
using System.Linq;
using AdvancedElectronics.Navigation;
using Eco.Gameplay.Objects;
using Eco.Shared.IoC;
using Eco.Shared.Serialization;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// A mining dock's reference to an area published by a survey dock (U8, KTD2): the
    /// owning dock's identifier, the area id, and the area's epoch observed at assignment
    /// time -- not a copy of the geometry. Resolved fresh at every dispatch rather than
    /// cached, because the owning dock's area list is the only source of truth for
    /// whether the area still exists.
    /// </summary>
    [Serialized]
    public class MiningAreaRef
    {
        [Serialized] public Guid OwningDockId { get; set; }
        [Serialized] public int AreaId { get; set; }
        [Serialized] public int ObservedEpoch { get; set; }

        /// <summary>
        /// Whether this reference has EVER resolved successfully. Not serialized -- it
        /// starts false every load, which is exactly what makes the first tick(s) after a
        /// restart tolerant of the owning dock not being registered yet (KTD2's
        /// load-ordering case), while a later disappearance, once it has been seen, is
        /// trusted as genuine (R6).
        /// </summary>
        private bool hasResolvedOnce;

        /// <summary>
        /// Consecutive failed lookups since the last success or load. Also not serialized, and it
        /// is what bounds the tolerance above: hasResolvedOnce alone made "not yet resolved"
        /// permanent for an area that was deleted before the restart, because it only ever flips
        /// true on a SUCCESSFUL resolve. Such a reference retried every tick forever, the job never
        /// ended, and the drone never came home -- live pass #1 left two of them parked in the pit
        /// of an area that no longer existed.
        ///
        /// Load ordering resolves within the first few ticks or not at all, so a reference that has
        /// never resolved is treated as genuinely gone once it has had clearly more chances than
        /// that. Deliberately generous: the cost of waiting too long is a drone idle for a few more
        /// seconds, while the cost of giving up too early is a job ended against an area that was
        /// about to appear.
        /// </summary>
        private int consecutiveFailures;

        private const int FailuresBeforeConfirmedGone = 20;

        public MiningAreaRef() { }

        public static MiningAreaRef For(WorldObject owningDock, SurveyAreaEntry area) => new MiningAreaRef
        {
            OwningDockId = owningDock.ObjectID,
            AreaId = area.Id,
            ObservedEpoch = area.Epoch,
        };

        /// <summary>
        /// Resolves this reference against the live world (KTD2's three-outcome policy).
        /// Returns the owning dock and the area entry when found; both are null otherwise.
        /// </summary>
        public AreaLookupSignal Resolve(out DroneDockObject owningDock, out SurveyAreaEntry area)
        {
            owningDock = null;
            area = null;

            var obj = ServiceHolder<IWorldObjectManager>.Obj.GetFromID(this.OwningDockId) as WorldObject;
            if (obj == null || obj.IsDestroyed || obj is not DroneDockObject dock)
                return this.NotFound();

            var entry = dock.SurveyAreas.FirstOrDefault(a => a.Id == this.AreaId);
            if (entry == null)
                return this.NotFound();

            owningDock = dock;
            area = entry;
            this.hasResolvedOnce = true;
            this.consecutiveFailures = 0;
            return AreaLookupSignal.Found;
        }

        /// <summary>The change-token comparison AreaResolutionPolicy needs (an area's epoch survives being re-fetched each tick; a redraw bumps it).</summary>
        public string StoredChangeToken => $"{this.AreaId}:{this.ObservedEpoch}";

        public static string CurrentChangeToken(SurveyAreaEntry area) => $"{area.Id}:{area.Epoch}";

        private AreaLookupSignal NotFound()
        {
            if (this.hasResolvedOnce) return AreaLookupSignal.ConfirmedGone;

            this.consecutiveFailures++;
            return this.consecutiveFailures >= FailuresBeforeConfirmedGone
                ? AreaLookupSignal.ConfirmedGone
                : AreaLookupSignal.NotYetResolved;
        }
    }
}
