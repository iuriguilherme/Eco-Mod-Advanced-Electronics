using Eco.Core.Plugins.Interfaces;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// Registers the Advanced Electronics mod with the Eco server so its WorldObjects,
    /// items, and recipes (<see cref="DroneDockObject"/>, <see cref="SurveyDroneItem"/>) load.
    ///
    /// Mirrors the feasibility spike's registration pattern
    /// (EcoServerMod/AdvancedElectronics.Spike/ModRegistration.cs) but this is a clean
    /// sibling project, not a subclass of the spike (KTD1 -- the spike stays a
    /// reference, not a base class). The spike's `/spike` chat commands are throwaway
    /// diagnostics and are deliberately not carried over here.
    /// </summary>
    public class AdvancedElectronicsMod : IModInit
    {
        public static ModRegistration Register() => new() 
        {
            ModName = "AdvancedElectronics",
            ModDescription = "Advanced Electronics mod loaded",
            ModDisplayName = "Advanced Electronics",
        };

        public string GetCategory() => "Mods";

        public string GetStatus() => "Advanced Electronics mod loaded";
    }
}
