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

            // Specific type name minus the "Block" suffix (e.g. "IronOreBlock" -> "IronOre",
            // "LimestoneBlock" -> "Limestone", "SandBlock" -> "Sand"). This IS the specific-type
            // granularity the readout reports (KTD1); SurveyRecord treats it as an opaque key.
            var typeName = block.GetType().Name;
            const string blockSuffix = "Block";
            var name = typeName.EndsWith(blockSuffix, System.StringComparison.Ordinal)
                ? typeName.Substring(0, typeName.Length - blockSuffix.Length)
                : typeName;

            // Classify by mining marker (KTD1). Every raw MINABLE block is a survey material -- this
            // covers Rock (Limestone, Granite, Basalt, Sandstone...), Ore (IronOre, CopperOre, Coal --
            // Coal counts as ore), and Sulfur. Crushed variants are [Diggable]/[Crushed], NOT [Minable],
            // so they are excluded here for free (R5).
            //
            // ASSUMPTION -- verify against a live server: Block.Is<Minable>()/Is<Diggable>()'s stripped
            // bodies mean the exact pass/fail boundary could not be executed offline; the reflection
            // dump confirms [Minable] on raw ore/rock and [Diggable]/[Crushed] on crushed/dug blocks.
            if (block.Is<Minable>())
            {
                oreType = name;
                return true;
            }

            // DIGGABLE materials are broader than we want (Dirt, Grass, Gravel are diggable too), so
            // restrict to the curated set the player surveys for: Sand, Clay, Peat.
            // ASSUMPTION -- verify live: the Diggable marker and these exact stripped type names.
            if (block.Is<Diggable>() && IsSurveyedDiggable(name))
            {
                oreType = name;
                return true;
            }

            return false;
        }

        /// <summary>Diggable materials worth surveying (R1): Sand, Clay, Peat. Excludes Dirt/Grass/Gravel.</summary>
        private static bool IsSurveyedDiggable(string name) =>
            name.Equals("Sand", System.StringComparison.Ordinal)
            || name.Equals("Clay", System.StringComparison.Ordinal)
            || name.Equals("Peat", System.StringComparison.Ordinal);
    }
}
