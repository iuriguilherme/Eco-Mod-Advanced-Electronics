using System;
using System.Collections.Generic;
using System.Linq;
using Eco.Gameplay.Items;
using Eco.Simulation;
using Eco.Simulation.Types;

namespace Eco.Mods.TechTree
{
    /// <summary>One crop: the produce a citizen recognises, the species that grows it, and the seed that starts it.</summary>
    public sealed class CropEntry
    {
        /// <summary>The engine's species name -- the opaque key the navigation core compares and the dock stores.</summary>
        public string Key { get; }

        /// <summary>The harvested produce, which is what a picker shows and what a ceiling counts in storage.</summary>
        public Item Produce { get; }

        public PlantSpecies Species { get; }

        public Type SeedType { get; }

        public string DisplayName => this.Produce?.DisplayName ?? this.Species.DisplayName;

        /// <summary>
        /// A name that picks out this crop alone. The produce name when no other crop shares
        /// it, otherwise the plant's own name -- roughly twenty vanilla plants all yield plant
        /// fibers, so "Plant Fibers" names none of them in particular.
        /// </summary>
        public string UniqueName =>
            CropCatalog.All.Count(c => c.DisplayName == this.DisplayName) > 1
                ? (string)this.Species.DisplayName
                : this.DisplayName;

        internal CropEntry(PlantSpecies species, Item produce, Type seedType)
        {
            this.Key = species.Name;
            this.Species = species;
            this.Produce = produce;
            this.SeedType = seedType;
        }
    }

    /// <summary>
    /// Every crop the drone can actually farm, and the three-way link between the produce a
    /// citizen picks, the species that grows it, and the seed that has to be sown (KTD15).
    ///
    /// Built from seeds rather than from the "Crop" tag, because the question the drone
    /// needs answered is not "is this food a crop" but "can this be planted": a crop with
    /// no seed item is something the drone can never start, however it is tagged. The tag
    /// still does its job on the client, where it filters what the area's picker offers.
    /// </summary>
    /// <remarks>
    /// Resolved through the engine's own species links, never by matching names. A seed
    /// knows its species, and a species' resource list names what it yields, so corn seed
    /// reaches corn without this mod holding a table of its own to go stale.
    /// </remarks>
    public static class CropCatalog
    {
        private static IReadOnlyList<CropEntry> cached;

        /// <summary>
        /// Every plantable crop, ordered by display name. Built once: seeds and species are
        /// fixed for the life of the server, so rebuilding per tab refresh would scan every
        /// item in the game for nothing.
        /// </summary>
        public static IReadOnlyList<CropEntry> All => cached ??= Build();

        /// <summary>The crop whose species carries <paramref name="speciesKey"/>, or null.</summary>
        public static CropEntry ByKey(string speciesKey) =>
            string.IsNullOrEmpty(speciesKey)
                ? null
                : All.FirstOrDefault(c => string.Equals(c.Key, speciesKey, StringComparison.Ordinal));

        /// <summary>
        /// The crop a citizen's picked produce item stands for, or null when that item is
        /// not something any plantable species yields.
        /// </summary>
        public static CropEntry ByProduce(Item produce) =>
            produce == null ? null : All.FirstOrDefault(c => c.Produce?.Type == produce.Type);

        /// <summary>
        /// The crop whose plant can yield <paramref name="item"/> at all, main harvest or
        /// not. Bean sprouts and beet greens carry the same "Crop" tag as real harvests, so
        /// the pickers offer them, but they only drop by chance from beans and beets; this
        /// names the crop a citizen who picked one probably meant.
        /// </summary>
        public static CropEntry ByAnyYield(Item item) =>
            item == null
                ? null
                : All.FirstOrDefault(c => c.Species.ResourceList.Any(r => r.ResourceType == item.Type));

        /// <summary>A crop's name for display, falling back to the raw key so an unresolvable area still reads.</summary>
        public static string DisplayNameFor(string speciesKey) =>
            ByKey(speciesKey)?.DisplayName ?? speciesKey;

        private static IReadOnlyList<CropEntry> Build()
        {
            var entries = new List<CropEntry>();

            foreach (var seed in Item.AllItemsIncludingHidden.OfType<SeedItem>())
            {
                var species = seed.Species;
                if (species == null) continue;

                // Trees are plantable and are not farmland: the block rules sow into a
                // tilled plot and harvest what stands on it, which a tree does not fit.
                if (species is TreeSpecies) continue;

                // One entry per species. Several seed items can name the same species, and
                // the area stores a species rather than a seed, so the first one wins.
                if (entries.Any(e => e.Species == species)) continue;

                entries.Add(new CropEntry(species, FirstProduce(species), seed.GetType()));
            }

            return entries
                .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// What a species yields, taken as the first entry of its own resource list. A
        /// species yielding nothing still belongs in the catalog -- it can be sown, and the
        /// area's readout should name it -- so this may be null.
        /// </summary>
        private static Item FirstProduce(PlantSpecies species) =>
            species.ResourceList
                .Where(r => r.ResourceType != null)
                .Select(r => Item.Get(r.ResourceType))
                .FirstOrDefault(item => item != null);
    }
}
