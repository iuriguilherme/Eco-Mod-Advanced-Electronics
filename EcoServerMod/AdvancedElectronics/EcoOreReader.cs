using AdvancedElectronics.Navigation;
using Eco.Shared.Math;
using Eco.World;
using Eco.World.Blocks;
using EcoWorld = Eco.World.World;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// Live-Eco-world-backed implementation of the Navigation library's
    /// <see cref="IOreReader"/> (U5), letting <see cref="OreSensorComponent"/>
    /// query real block data instead of a test suite's in-memory fake. This
    /// class has no survey-accumulation logic of its own - it is purely a
    /// translation layer from (x, y, z) block coordinates to Eco world-query
    /// calls, mirroring EcoWorldSampler.cs's role for IWorldSampler (U3).
    ///
    /// APIs below were found the same way EcoWorldSampler.cs's were: a
    /// reflection dump (System.Reflection.MetadataLoadContext) against the
    /// restored Eco.ReferenceAssemblies 0.13.0.4-beta-release-1024 NuGet
    /// package DLLs. Those reference assemblies ship with method BODIES
    /// stripped, so exact runtime semantics of the calls below (not just
    /// their signatures/attributes, which the dump does confirm) could not
    /// be executed/confirmed offline. Every place that rests on such an
    /// unverified semantic is flagged ASSUMPTION, matching EcoWorldSampler's
    /// established pattern - expected to need live-server confirmation, not
    /// a blocker to shipping this unit.
    /// </summary>
    public sealed class EcoOreReader : IOreReader
    {
        // Finding (confirmed by the reflection dump, cross-checked against
        // vanilla ore block declarations in Eco.Mods.dll): every raw,
        // in-ground ore block type (e.g. Eco.Mods.TechTree.IronOreBlock,
        // CopperOreBlock, GoldOreBlock) is a distinct Block subclass carrying
        // an [Eco.World.Blocks.Minable(hardness)] class attribute. Their
        // already-mined counterparts (e.g. CrushedIronOreBlock, and the
        // pickupable "*OreStacked*Block" rubble piles) do NOT carry
        // [Minable] - they carry [Diggable]/[Crushed] or derive from
        // PickupableBlock instead. This makes Block.Is<Minable>() (the same
        // BlockAttribute-marker-probe pattern EcoWorldSampler.cs uses for
        // Is<Empty>()) a generic, ore-agnostic way to recognize "a raw
        // in-ground ore deposit block" without hardcoding every ore's type
        // name here.
        public bool TryGetOreType(int x, int y, int z, out string oreType)
        {
            oreType = null;

            var block = EcoWorld.GetBlock(new Vector3i(x, y, z));
            if (block == null)
                return false;

            // ASSUMPTION -- verify against a live server: Block.Is<T>()'s
            // stripped body means the exact pass/fail boundary (e.g. whether
            // it checks the block's most-derived declared type or something
            // attribute-inheritance-aware) could not be executed offline;
            // the reflection dump only confirms the [Minable] attribute is
            // present at the type level on every raw ore block and absent
            // from crushed/pickupable variants.
            if (!block.Is<Minable>())
                return false;

            // ASSUMPTION -- verify against a live server: using the block's
            // own concrete type name (minus the "Block" suffix vanilla ore
            // types consistently use, e.g. "IronOreBlock" -> "IronOre") as
            // the ore-type identifier SurveyGrid accumulates against. This
            // was picked over Block.RepresentedItemType.Name (also present
            // on these types) because it needs no extra hop through the
            // represented-item type and reads directly off the block that
            // was just queried; either would work equally well as a stable
            // per-ore key, since SurveyGrid treats oreType as an opaque
            // string.
            var typeName = block.GetType().Name;
            const string blockSuffix = "Block";
            var name = typeName.EndsWith(blockSuffix, System.StringComparison.Ordinal)
                ? typeName.Substring(0, typeName.Length - blockSuffix.Length)
                : typeName;

            // [Minable] alone is too broad: vanilla tags plain rock with it too
            // (BasaltBlock is [Solid, Wall, Minable(5)], structurally identical to
            // CopperOreBlock's [Solid, Wall, Minable(3)]). Reporting every minable block
            // buries the actual find under stone, since rock is far more common than ore
            // and would dominate every cell's density.
            //
            // No attribute distinguishes them, so v1 discriminates by name: the deposits
            // a player prospects for. Deliberately narrow and easy to extend -- adding a
            // resource is one entry here.
            if (!IsProspectableDeposit(name))
                return false;

            oreType = name;
            return true;
        }

        /// <summary>
        /// Raw, in-ground deposits worth reporting (R7). Excludes plain rock, and
        /// excludes the "Crushed*" rubble variants that mining leaves behind -- those
        /// are the product of digging, not a reason to dig.
        /// </summary>
        private static bool IsProspectableDeposit(string name)
        {
            if (name.StartsWith("Crushed", System.StringComparison.Ordinal))
                return false;

            return name.EndsWith("Ore", System.StringComparison.Ordinal)
                || name.Equals("Coal", System.StringComparison.Ordinal);
        }
    }
}
