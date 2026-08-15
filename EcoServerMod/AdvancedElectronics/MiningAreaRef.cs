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
            return AreaLookupSignal.Found;
        }

        /// <summary>The change-token comparison AreaResolutionPolicy needs (an area's epoch survives being re-fetched each tick; a redraw bumps it).</summary>
        public string StoredChangeToken => $"{this.AreaId}:{this.ObservedEpoch}";

        public static string CurrentChangeToken(SurveyAreaEntry area) => $"{area.Id}:{area.Epoch}";

        private AreaLookupSignal NotFound() => this.hasResolvedOnce ? AreaLookupSignal.ConfirmedGone : AreaLookupSignal.NotYetResolved;
    }
}
