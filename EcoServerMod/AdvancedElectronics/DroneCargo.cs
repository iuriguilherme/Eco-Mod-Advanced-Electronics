using Eco.Gameplay.Components.Storage;
using Eco.Gameplay.Items;

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
            configure: c => c.Initialize(HoldSlots),
            canUninstall: c => c.Storage.IsEmpty,
            proxyInteractions: false);
    }
}
