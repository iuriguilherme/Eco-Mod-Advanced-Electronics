using Eco.Core.Plugins.Interfaces;

namespace AdvancedElectronics.Spike
{
    /// <summary>
    /// Registers the feasibility-spike mod with the Eco server so its chat
    /// commands load. The spike answers the three questions blocking the
    /// survey-drone plan (see docs/plans/2026-07-11-002-feat-drone-feasibility-spike-plan.md).
    /// </summary>
    public class SpikeMod : IModKitPlugin
    {
        public string GetCategory() => "Mods";

        public string GetStatus() => "Advanced Electronics feasibility spike loaded";
    }
}
