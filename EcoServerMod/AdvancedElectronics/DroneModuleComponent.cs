using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedElectronics.Navigation;
using Eco.Core.Controller;
using Eco.Core.Utils;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Storage;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Logging;
using Eco.Shared.Serialization;
using Eco.Shared.Services;
using Eco.Shared.Utils;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// Why a dock is not dispatching (R10, R12). Distinct reasons rather than a bare bool: a
    /// player who is out of fuel must be told that, not shown an area that looks like nobody
    /// asked for it.
    /// </summary>
    public enum DockStopReason
    {
        /// <summary>Serviceable.</summary>
        None,

        /// <summary>The drone's fuel tank is empty.</summary>
        NoFuel,

        /// <summary>One of the dock's own parts has worn out.</summary>
        BrokenParts,

        /// <summary>The docked drone itself has worn out.</summary>
        BrokenDrone,
    }

    /// <summary>
    /// Installs the slotted drone item's declared components onto this dock, and removes them
    /// again when the drone leaves (R5, R8, R19).
    ///
    /// The dock is the component host because a dock is an ordinary placed object with an item
    /// behind it and a drone is not. What the player is told is that the fuel belongs to the
    /// drone; what the engine sees is a dock that borrows the drone's components for as long as
    /// it holds one. Eco calls this a module, and the machinery is generic -- only
    /// <see cref="ModularVehicleComponent"/>, the vanilla driver, is vehicle-bound, which is why
    /// this component exists instead of reusing it.
    ///
    /// This is the unit where a mistake is least visible: a driver that silently fails to install
    /// produces a dock with no fuel tab, which reads as a rendering bug rather than an
    /// initialization failure. Install and uninstall therefore log at info level.
    /// </summary>
    [Serialized, NoIcon]
    public class DroneModuleComponent : WorldObjectComponent, IDeclaresMayHaveComponents
    {
        // The source whose components are currently attached to the dock. Not serialized: it is
        // re-derived from the storage slot on the first tick after load, and the components
        // themselves persist on their own (that is what IDeclaresMayHaveComponents buys).
        private IWorldObjectComponentSource installedSource;

        // Install/uninstall must not run under an inventory lock -- a component's Initialize can
        // take its own. Storage change events fire inside that lock, so they only raise this flag
        // and Tick does the work. Mirrors ModularVehicleComponent.SyncComponentInstallation.
        private bool syncPending;

        private User pendingNotifyUser;

        /// <summary>
        /// Every (Type, Name) the slotted drone declares. Component validation on world load uses
        /// this to know that a dynamically installed component is expected rather than stray --
        /// without it, the fuel tab and its contents are stripped on the first restart.
        /// </summary>
        public IEnumerable<(Type Type, string Name)> ExpectedComponents
        {
            get
            {
                if (this.SlottedSource is not { } source) yield break;
                foreach (var inst in source.ComponentsToInstall)
                    yield return (inst.ComponentType, inst.Name);
            }
        }

        /// <summary>
        /// A broken drone disables its dock, alongside an empty tank and broken parts (R10).
        ///
        /// This lives here rather than in a component of its own, which was the plan's open
        /// question: the driver already resolves the slotted item every tick, so a separate type
        /// would exist only to repeat that lookup. Enabled and Operating answer different
        /// questions -- Enabled is "can this dock work at all", Operating is "is it working right
        /// now" -- so nothing is being overloaded by putting this one here.
        /// </summary>
        public override bool Enabled => this.SlottedItem is not RepairableItem { Broken: true };

        /// <summary>The drone item currently in the dock's slot, or null.</summary>
        private Item SlottedItem =>
            this.Parent.TryGetComponent<PublicStorageComponent>(out var storage)
                ? storage.Storage.NonEmptyStacks.FirstOrDefault()?.Item
                : null;

        /// <summary>The drone item currently in the dock's slot, as a component source, or null.</summary>
        private IWorldObjectComponentSource SlottedSource => this.SlottedItem as IWorldObjectComponentSource;

        /// <summary>
        /// Wires this driver to the dock's storage slot. Called from DroneDockObject.Initialize
        /// after the storage component has been initialized, rather than from this component's own
        /// Initialize, because component initialization order is not guaranteed and the
        /// restrictions below have to attach to a storage that already exists.
        /// </summary>
        public void AttachTo(PublicStorageComponent storage)
        {
            // R8: vanilla restriction, honours each installation's canUninstall -- refuses to let
            // the drone out while the fuel it installed still holds fuel. RemoveComponent destroys
            // a component without capturing its state, so this is what keeps uninstall from
            // eating items.
            storage.Storage.AddInvRestriction(new ComponentSourceRestriction(this.Parent));

            // R19: refuse removal unless the drone is home. Per KTD8 this is also what makes the
            // uninstall path safe -- no live drone world object can exist while its components are
            // being torn off, so the despawn/uninstall race cannot occur rather than being ordered.
            storage.Storage.AddInvRestriction(new DroneDockedRestriction(this.Parent));

            storage.Storage.OnChanged.Add(this.OnSlotChanged);

            // Placement and world load both land here with a slot that may already hold a drone.
            this.syncPending = true;
        }

        private void OnSlotChanged(User user)
        {
            this.pendingNotifyUser = user;
            this.syncPending       = true;
        }

        public override void Tick()
        {
            if (!this.syncPending) return;
            this.syncPending = false;
            this.Sync();
        }

        private void Sync()
        {
            // Belt and braces: Tick should never run under an inventory lock, but if it somehow
            // does, defer rather than deadlock inside a component's Initialize.
            if (InventoryLock.IsHeldByCurrentThread) { this.syncPending = true; return; }

            var newSource = this.SlottedSource;
            if (ReferenceEquals(this.installedSource, newSource)) return;

            if (this.installedSource != null) this.Uninstall(this.installedSource);
            if (newSource != null && !this.TryInstall(newSource)) return;

            this.installedSource = newSource;

            if (this.pendingNotifyUser?.Player != null)
                this.Parent.SendUIComponents(this.pendingNotifyUser.Player);
            this.pendingNotifyUser = null;
        }

        /// <summary>
        /// Attaches every component the source declares, all or nothing. Configure calls each
        /// component's Initialize, and a throw partway through the list would leave the dock with
        /// some components attached and some not -- an initialization failure wearing the costume
        /// of a rendering bug, which this mod has already paid for once
        /// (docs/solutions/runtime-errors/initialize-exception-leaves-a-half-built-worldobject.md).
        /// So track what was attached, unwind it on failure, and tell the player.
        /// </summary>
        private bool TryInstall(IWorldObjectComponentSource source)
        {
            var attached = new List<WorldObjectComponent>();
            try
            {
                foreach (var inst in source.ComponentsToInstall)
                {
                    // A component that survived a save/load is already here. Reapply Configure to
                    // restore state that does not serialize, but do not count it as newly attached
                    // -- unwinding it would destroy data this install did not create.
                    if (this.Parent.GetComponent(inst.ComponentType, inst.Name) is WorldObjectComponent existing)
                    {
                        inst.Configure?.Invoke(existing);
                        continue;
                    }

                    attached.Add(this.Parent.GetOrCreateComponent(inst.ComponentType, inst.Name, inst.Configure));
                }
            }
            catch (Exception e)
            {
                foreach (var component in Enumerable.Reverse(attached))
                    this.Parent.RemoveComponent(component);

                Log.WriteErrorLineLoc(
                    $"Drone Dock: installing {source.GetType().Name}'s components failed, rolled back {attached.Count} component(s): {e}");
                this.pendingNotifyUser?.MsgLocStr(
                    "This drone could not be installed. The dock is unchanged; check the server log.",
                    NotificationStyle.Error);
                return false;
            }

            Log.WriteLineLoc($"Drone Dock: installed {source.GetType().Name}'s components.");
            return true;
        }

        private void Uninstall(IWorldObjectComponentSource source)
        {
            // Reverse order, so a component that depends on an earlier one is gone before it.
            foreach (var inst in source.ComponentsToInstall.Reverse())
                this.Parent.RemoveComponent(this.Parent.GetComponent(inst.ComponentType, inst.Name) as WorldObjectComponent);

            Log.WriteLineLoc($"Drone Dock: uninstalled {source.GetType().Name}'s components.");
        }
    }

    /// <summary>
    /// Refuses to hand a drone item back until its drone world object is home and idle (R19).
    ///
    /// This reverses the dock's earlier rule that removal was always allowed. That rule existed
    /// because a roaming drone could strand, so removal doubled as an escape hatch; the return-leg
    /// escalation (relax climb height, then hover, then clip, then teleport) means a return can no
    /// longer fail, which makes the escape hatch obsolete rather than merely inconvenient.
    ///
    /// Unreachable deliberately does not count as docked. It is the state the escalation exists to
    /// resolve, and treating it as removable would reopen the tear-off-while-live hazard for the
    /// exact case most likely to hit it.
    /// </summary>
    public class DroneDockedRestriction : InventoryRestriction
    {
        private readonly WorldObject parent;

        public DroneDockedRestriction(WorldObject parent) => this.parent = parent;

        public override LocString Message =>
            Localizer.DoStr("The drone is still out. Unassign its survey and wait for it to return.");

        public override int MaxPickup(RestrictionCheckData checkData, Item item, int currentQuantity)
        {
            if (item is not SurveyDroneItem)               return -1;
            if (this.parent is not DroneDockObject dock)   return -1;

            var drone = dock.SpawnedDrone;
            if (drone == null || drone.IsDestroyed)        return -1;   // nothing out there to wait for.

            return drone.TryGetComponent<DroneLifecycle>(out var lifecycle)
                   && lifecycle.Status != DroneStatus.Idle
                ? 0
                : -1;
        }
    }
}
