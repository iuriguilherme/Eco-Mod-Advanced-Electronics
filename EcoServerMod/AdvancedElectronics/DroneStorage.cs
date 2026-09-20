using System;
using System.Collections.Generic;
using System.Linq;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Storage;
using Eco.Gameplay.Items;
using Eco.Gameplay.Players;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// A drone's linked storage, read the way vanilla reads it. The storage tab's
    /// "Take From" is a link's Input and "Put Into" is its Output: a drone takes
    /// materials only from the first and delivers cargo only into the second, exactly as
    /// crafting tables and stores do (<c>LinkComponent.GetSortedLinkedComponents</c>).
    /// It used to treat every enabled link as both, and dropped cargo into chests the
    /// player had linked for taking only.
    /// </summary>
    /// <remarks>
    /// Another drone dock is never a storage for this purpose: its cargo hold is an
    /// ordinary storage component, and drones filled each other's holds with cargo that
    /// then never reached storage.
    /// </remarks>
    public static class DroneStorage
    {
        /// <summary>Linked storages the drone may take materials from ("Take From").</summary>
        public static List<Inventory> TakeFrom(LinkComponent link, User citizen) =>
            Linked(link, citizen, source: true, target: false);

        /// <summary>Linked storages the drone may deliver cargo into ("Put Into").</summary>
        public static List<Inventory> PutInto(LinkComponent link, User citizen) =>
            Linked(link, citizen, source: false, target: true);

        private static List<Inventory> Linked(LinkComponent link, User citizen, bool source, bool target)
        {
            if (link == null || citizen == null) return new List<Inventory>();

            return link.GetSortedLinkedComponents(citizen, source, target)
                .Where(storage => storage.Parent is not DroneDockObject)
                .Select(storage => storage.Inventory)
                .Where(inventory => inventory != null)
                .ToList();
        }

        /// <summary>How many of <paramref name="itemType"/> the given storages hold between them.</summary>
        public static int Count(IEnumerable<Inventory> storages, Type itemType) =>
            storages.SelectMany(inventory => inventory.NonEmptyStacks)
                .Where(stack => stack.Item?.Type == itemType)
                .Sum(stack => stack.Quantity);

        /// <summary>
        /// Moves up to <paramref name="quantity"/> of <paramref name="itemType"/> from the
        /// Take-From storages into <paramref name="hold"/>, as far as the hold has room.
        /// Called only while the drone is docked: materials change hands at the dock, never
        /// at a work site. Added to the hold first and then taken from storage, and put back
        /// out of the hold if storage refuses, so a failed take never creates items.
        /// Returns how many moved.
        /// </summary>
        public static int Load(Inventory hold, LinkComponent link, User citizen, Type itemType, int quantity)
        {
            if (quantity <= 0 || hold == null) return 0;

            var sources = TakeFrom(link, citizen);
            var available = Count(sources, itemType);
            var want = Math.Min(quantity, available);
            if (want <= 0) return 0;

            var from = new InventoryCollection(sources);
            var moved = 0;

            // Largest chunk that fits, halving on refusal, the unloader's own rule.
            while (want > 0)
            {
                var chunk = want;
                while (chunk > 0 && !hold.TryAddItemsNonUnique(itemType, chunk, citizen).Success)
                    chunk /= 2;
                if (chunk == 0) break;

                if (!from.TryRemoveItems(itemType, chunk, citizen).Success)
                {
                    hold.TryRemoveItems(itemType, chunk, citizen);
                    break;
                }

                moved += chunk;
                want -= chunk;
            }

            return moved;
        }
    }
}
