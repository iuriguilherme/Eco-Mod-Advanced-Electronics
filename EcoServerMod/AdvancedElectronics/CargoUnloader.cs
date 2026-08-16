using System.Collections.Generic;
using System.Linq;
using AdvancedElectronics.Navigation;
using Eco.Gameplay.Components;
using Eco.Gameplay.Items;
using Eco.Gameplay.Players;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// Attempts to push a mining drone's hold into its dock's linked storage (U7, R25,
    /// R27, R43). Never unit-tested (every collaborator is an Eco type) -- the pure
    /// arithmetic it reports through lives in <see cref="HoldLedger"/>, which is.
    /// </summary>
    public static class CargoUnloader
    {
        /// <summary>
        /// Moves as much of <paramref name="hold"/>'s contents as fit into the inventories
        /// <paramref name="link"/> resolves through <paramref name="stampedCitizen"/>'s alias
        /// (R43 -- the party accountable for a removal is the party who receives it). Never
        /// partial-fails a stack that could fit more: each stack is pushed at the largest
        /// quantity that succeeds, retried at half that quantity on refusal, down to a single
        /// item, so a nearly-full destination still receives what room it has.
        /// </summary>
        public static UnloadPlan TryUnload(Inventory hold, LinkComponent link, User stampedCitizen)
        {
            var holdQuantity = hold.NonEmptyStacks.Sum(s => s.Quantity);
            if (holdQuantity == 0)
                return HoldLedger.Plan(0, 0);

            if (stampedCitizen == null)
                return HoldLedger.Plan(holdQuantity, 0);

            // Another drone dock is not a warehouse. Its cargo hold is an ordinary
            // PublicStorageComponent, so the link network offered it as a destination like any
            // chest and drones filled each other's holds -- cargo that then never reaches storage,
            // because the receiving drone unloads from its own hold, not into it.
            //
            // Excluded by owning object rather than by component name: the drone bay and any
            // future dock storage are equally wrong targets, and matching on the hold's name would
            // silently stop covering them.
            var destinations = new InventoryCollection(
                link.GetSortedLinkedEnabledStorages(stampedCitizen)
                    .Where(storage => storage.Parent is not DroneDockObject)
                    .Select(storage => storage.Inventory));
            var moved = 0;

            // Snapshot: a successful push below removes from the hold, which would
            // otherwise invalidate NonEmptyStacks mid-enumeration.
            foreach (var stack in hold.NonEmptyStacks.ToList())
            {
                if (stack.Item == null) continue;

                var itemType = stack.Item.GetType();
                var placed = AddAsManyAsFit(destinations, itemType, stack.Quantity, stampedCitizen);
                if (placed <= 0) continue;

                hold.TryRemoveItems(itemType, placed, stampedCitizen);
                moved += placed;
            }

            return HoldLedger.Plan(holdQuantity, moved);
        }

        /// <summary>
        /// Pushes up to <paramref name="quantity"/> of <paramref name="itemType"/> into
        /// <paramref name="destination"/>, accepting a partial fill: each round tries the
        /// largest remaining amount, halving on refusal down to one, and stops once even
        /// one more item is refused (destinations are full for this item type). Returns
        /// how much actually moved.
        /// </summary>
        private static int AddAsManyAsFit(Inventory destination, System.Type itemType, int quantity, User user)
        {
            var movedTotal = 0;
            var remaining = quantity;

            while (remaining > 0)
            {
                var chunk = remaining;
                var placedThisRound = 0;

                while (chunk > 0)
                {
                    if (destination.TryAddItemsNonUnique(itemType, chunk, user).Success)
                    {
                        placedThisRound = chunk;
                        break;
                    }
                    chunk /= 2;
                }

                if (placedThisRound == 0)
                    break; // Not even one more fits -- destinations are full for this item.

                movedTotal += placedThisRound;
                remaining -= placedThisRound;
            }

            return movedTotal;
        }
    }
}
