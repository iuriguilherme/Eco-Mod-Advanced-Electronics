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

            // Classification lives in SurveyMaterials, shared with the startup tagger that scopes the
            // dock's material picker -- ONE definition, so a material the drone reports is always
            // selectable in the picker and nothing selectable is undetectable.
            var blockType = block.GetType();
            if (!SurveyMaterials.IsSurveyMaterial(blockType))
                return false;

            // The specific type name minus "Block" ("IronOreBlock" -> "IronOre") IS the specific-type
            // granularity the readout reports; SurveyRecord treats it as an opaque key.
            oreType = SurveyMaterials.MaterialName(blockType);
            return true;
        }
    }
}
