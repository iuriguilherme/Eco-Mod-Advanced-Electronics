using System;
using System.Linq;
using Eco.World.Blocks;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// The single definition of "a material this drone can survey", used by the sensor to classify
    /// the blocks it scans.
    ///
    /// Classification is type-level rather than instance-level so any caller can ask about a block
    /// type without holding a block. <c>Block.Get&lt;T&gt;(Type)</c> is the same type-level marker
    /// probe vanilla uses (PickaxeItem tests <c>Block.Get&lt;Minable&gt;(blockitem.OriginType)</c>).
    ///
    /// This deliberately does NOT drive the dock's material picker. Scoping that picker to exactly
    /// this set needs a tag the client already knows, and the mod's RUNTIME tag registration never
    /// reached it: the server registry was verifiably correct (30 of 113 block items tagged) while
    /// the picker stayed empty.
    ///
    /// CORRECTED ATTRIBUTION (2026-08-01). This comment used to blame the emptiness on a
    /// mod-registered tag never reaching the client, via TagManager.Initialize calling SetupDone()
    /// before mods can register. Both halves were wrong, and the wrong cause matters: it rules out
    /// the very route BatteryItem's "Electric Fuel" tag depends on.
    ///   - A [Tag("X")] ATTRIBUTE on a mod type -- or on a vanilla type replaced through a
    ///     .override file -- DOES reach the client. Mod DLLs load and InitMods() runs before
    ///     TagManager.Initialize, and the attribute pass enumerates every loaded assembly.
    ///   - What is frozen is the client's ViewClassInfo snapshot, built once while the
    ///     ControllerManager plugin is constructed. A type->tag association added at runtime
    ///     (TagManager.AddTypeToTag from an IModKitPlugin.Initialize) runs after that build and is
    ///     never in the snapshot. SetupDone() is only a WhenReady gate with no observed consumer.
    /// The symptom was reported accurately; the cause was not. See
    /// docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md.
    ///
    /// The picker therefore uses the closest stock tag, "Excavatable", which live diagnostics
    /// showed covers every material this classifier accepts. Attribute-tagging this exact set
    /// would mean carrying a .override of every vanilla block item it names, which is a much
    /// larger cost than the scoping buys.
    /// </summary>
    public static class SurveyMaterials
    {
        /// <summary>Diggable materials worth surveying. Excludes dirt/grass/gravel as noise.</summary>
        private static readonly string[] SurveyedDiggables = { "Sand", "Clay", "Peat" };

        /// <summary>The material name recorded for a block type ("LimestoneBlock" -> "Limestone").</summary>
        public static string MaterialName(Type blockType) => StripSuffix(blockType.Name, "Block");

        /// <summary>
        /// True when the drone detects and reports blocks of <paramref name="blockType"/>:
        /// every minable block (rock, ore, coal, sulfur), the crushed variants, and the curated
        /// diggables (sand, clay, peat).
        /// </summary>
        public static bool IsSurveyMaterial(Type blockType)
        {
            if (blockType == null) return false;

            var name = MaterialName(blockType);

            // Minable covers rock, ore (coal counts as ore) and sulfur.
            if (Block.Get<Minable>(blockType) != null) return true;

            // Crushed variants are [Diggable]/[Crushed], never [Minable], but are real in-world
            // materials worth reporting.
            if (name.StartsWith("Crushed", StringComparison.Ordinal)) return true;

            // Diggable is broader than we want (dirt, grass, gravel), so keep the curated set.
            return Block.Get<Diggable>(blockType) != null && IsSurveyedDiggable(name);
        }

        public static bool IsSurveyedDiggable(string materialName) =>
            SurveyedDiggables.Contains(materialName, StringComparer.Ordinal);

        public static string StripSuffix(string value, string suffix) =>
            value.EndsWith(suffix, StringComparison.Ordinal) && value.Length > suffix.Length
                ? value.Substring(0, value.Length - suffix.Length)
                : value;
    }
}
