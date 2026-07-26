using System;
using System.Linq;
using Eco.Core.Plugins.Interfaces;
using Eco.Core.Utils;
using Eco.Gameplay.Items;
using Eco.World.Blocks;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// The single definition of "a material this drone can survey", shared by the sensor (which
    /// classifies blocks it scans) and the picker tagging below (which decides what the player may
    /// select). One source so the two can never drift: a material the drone reports is always
    /// selectable, and nothing selectable is undetectable.
    ///
    /// Classification is type-level so both callers can use it -- the sensor holds a Block instance
    /// and passes its type, the tagger walks item types. <c>Block.Get&lt;T&gt;(Type)</c> is the same
    /// type-level marker probe vanilla uses (PickaxeItem tests
    /// <c>Block.Get&lt;Minable&gt;(blockitem.OriginType)</c>).
    /// </summary>
    public static class SurveyMaterials
    {
        /// <summary>
        /// Custom tag the material picker is scoped by. Applied at startup to exactly the items whose
        /// block the drone can detect, which is why the picker offers no crafted/buildable blocks and
        /// misses no detectable material -- neither is expressible with a stock tag (see
        /// <see cref="SurveyMaterialTagger"/>).
        /// </summary>
        public const string TargetTag = "AdvancedElectronicsSurveyTarget";

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

    /// <summary>
    /// Tags every surveyable material's item with <see cref="SurveyMaterials.TargetTag"/> at startup,
    /// so the dock's material picker can be scoped to exactly what the drone detects.
    ///
    /// Why a custom tag: a GamePickerList's candidate set is scoped by a SINGLE stock tag
    /// (<c>RequiredTagAttribute(string)</c>), and no stock tag matches the drone's detection scope --
    /// "Minable" is a BLOCK tag so an item picker scoped to it is empty, "Diggable" yields
    /// compost/dirt/garbage/tailings while missing clay and peat, and crushed material sits under
    /// "Excavatable". Registering our own tag is the supported way out: TagManager.AddTypeToTag is
    /// public and documented "for tags populated programmatically rather than via [Tag] attributes",
    /// and GetOrMake creates the tag on demand. Mirrors HousingTags.Initialize, which tags items by
    /// housing category the same way.
    /// </summary>
    public class SurveyMaterialTagger : IModKitPlugin, IInitializablePlugin
    {
        private string status = "Survey material tags not initialized";

        public void Initialize(TimedTask timer)
        {
            // AddTypeToTag(string, Type) is the tidy wrapper for this, but it is not in the 0.13.0.4
            // reference assemblies -- so register through the public registry it wraps: GetOrMake
            // creates the tag on demand, then both directions of the index are updated exactly as
            // AddTagToType does internally (TypeToTags and TagToTypes are public static).
            var tag = TagManager.GetOrMake(SurveyMaterials.TargetTag);

            var tagged = 0;
            foreach (var item in Item.AllItemsIncludingHidden.OfType<BlockItem>())
            {
                if (!SurveyMaterials.IsSurveyMaterial(item.OriginType)) continue;

                AddToSet(TagManager.TypeToTags, item.Type, tag);
                AddToSet(TagManager.TagToTypes, tag, item.Type);
                tagged++;
            }

            this.status = $"Tagged {tagged} surveyable materials";
        }

        /// <summary>Eco's own AddToSet extension is not exposed by the reference assemblies; same behaviour.</summary>
        private static void AddToSet<TKey, TValue>(System.Collections.Generic.Dictionary<TKey, System.Collections.Generic.HashSet<TValue>> map, TKey key, TValue value)
        {
            if (!map.TryGetValue(key, out var set))
            {
                set = new System.Collections.Generic.HashSet<TValue>();
                map[key] = set;
            }
            set.Add(value);
        }

        public string GetCategory() => "Mods";

        public string GetStatus() => this.status;
    }
}
