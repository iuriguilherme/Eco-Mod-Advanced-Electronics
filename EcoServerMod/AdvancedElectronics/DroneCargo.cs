using AdvancedElectronics.Navigation;
using Eco.Gameplay.Components.Storage;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Shared.Localization;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// The mining drone's cargo hold: a shared factory for the hold's component
    /// installation, so the name is one constant in one place rather than a literal
    /// inside the drone's own file (KTD4, U6). U7's unloader matches on
    /// <see cref="HoldName"/>, and a future harvester hold reuses this factory.
    /// </summary>
    /// <remarks>
    /// The hold is NAMED, unlike the fuel pair -- the dock already carries an unnamed
    /// <c>PublicStorageComponent</c> for the drone slot itself, and component lookup
    /// matches on type and name together, so an unnamed hold would be indistinguishable
    /// from the drone bay. Naming the fuel components instead would hide them from the
    /// engine's own unnamed lookup (see MiningDroneItem/SurveyDroneItem's fuel
    /// installations) -- this mod has already been burned by that mistake once
    /// (docs/solutions/runtime-errors/naming-a-component-hides-it-from-its-vanilla-consumer.md).
    ///
    /// This factory produces a <c>ComponentInstallation</c>, not a WorldObjectComponent
    /// itself, so it needs no component attribute set of its own.
    /// </remarks>
    public static class DroneCargo
    {
        /// <summary>The hold's component name -- matched by name, not just type, everywhere the hold is looked up.</summary>
        public const string HoldName = "MiningCargo";

        /// <summary>
        /// Slot count (KTD5): at typical block stack sizes, roughly 400-500 items per
        /// trip, so a plot costs one to three round trips and a full area thirty to a
        /// hundred and twenty.
        /// </summary>
        private const int HoldSlots = 16;

        /// <summary>
        /// The hold's component installation: a named <see cref="PublicStorageComponent"/>,
        /// gated against uninstall while it still holds anything (R23) -- the same shape
        /// the fuel tank already uses, so a drone cannot be pulled from its dock carrying
        /// cargo the uninstall path would otherwise destroy.
        /// </summary>
        public static ComponentInstallation Installation() => ComponentInstallation.For<PublicStorageComponent>(
            name: HoldName,
            configure: Configure,
            canUninstall: c => c.Storage.IsEmpty,
            proxyInteractions: false);

        private static void Configure(PublicStorageComponent hold)
        {
            hold.Initialize(HoldSlots);

            // R23/R25: the hold is the flying drone's, not a chest on the pad. Without this the
            // dock's Storage tab let a player empty a drone that was kilometres away and halfway
            // down a shaft -- and, because the hold is a linked inventory, let one drone's cargo
            // be moved into another's.
            hold.Storage.AddInvRestriction(new DroneHoldRestriction(hold.Parent));
        }
    }

    /// <summary>
    /// Refuses to let anything be taken out of a drone's cargo hold unless the drone is home and
    /// idle (R23, R25).
    ///
    /// Only TAKING is refused, deliberately. The mining removal service delivers each layer's
    /// yield into this same inventory while the drone is out working, and a restriction on adding
    /// cannot tell that write apart from a player stuffing the hold -- both arrive as an inventory
    /// change carrying the stamped citizen. Blocking it would stop the drone mining at all, which
    /// is a far worse failure than a player being able to donate rocks to a drone.
    ///
    /// The unload path takes from the hold, but only ever at the dock, where the drone is Idle and
    /// this restriction already allows it.
    ///
    /// Mirrors <see cref="DroneDockedRestriction"/>, which guards the drone bay the same way and
    /// for the same reason; the two differ only in which inventory they sit on.
    /// </summary>
    public class DroneHoldRestriction : InventoryRestriction
    {
        private readonly WorldObject parent;

        public DroneHoldRestriction(WorldObject parent) => this.parent = parent;

        public override LocString Message =>
            Localizer.DoStr("The drone is still out. Wait for it to return before unloading it by hand.");

        public override int MaxPickup(RestrictionCheckData checkData, Item item, int currentQuantity)
        {
            if (this.parent is not DroneDockObject dock) return -1;

            var drone = dock.SpawnedDrone;
            if (drone == null || drone.IsDestroyed) return -1;  // nothing flying; the hold is just a box.

            return drone.TryGetComponent<DroneLifecycle>(out var lifecycle)
                   && lifecycle.Status != DroneStatus.Idle
                ? 0
                : -1;
        }
    }
}
