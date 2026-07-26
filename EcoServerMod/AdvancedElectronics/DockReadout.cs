using AdvancedElectronics.Navigation;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// Pure formatting for the dock's survey readout. Zero dependency on any Eco.* namespace — only
    /// <see cref="AdvancedElectronics.Navigation"/> types (<see cref="SurveyFinding"/>) — so it stays
    /// testable without a running server. The Survey tab (<c>SurveyAreasComponent</c>) and the chat
    /// commands both format their per-material lines through <see cref="FormatOreLine"/>.
    /// </summary>
    public static class DockReadout
    {
        /// <summary>
        /// Formats one per-material line (R2), quantity-led: how much of the material was found, the
        /// shallowest block to dig, and the depth range. Quantity is the headline because it is
        /// meaningful for common bulk materials (rock) as well as rare ore, where a concentration
        /// ratio read as noise (KTD2/R3).
        /// </summary>
        public static string FormatOreLine(SurveyFinding finding)
        {
            if (!finding.Found)
                return $"{finding.OreType}: no data yet";

            var depth = finding.DepthMax > finding.DepthBelowSurface
                ? $"depth {finding.DepthBelowSurface}-{finding.DepthMax}"
                : $"{finding.DepthBelowSurface} blocks deep";
            return $"{finding.OreType}: ~{finding.Count} blocks, shallowest at {finding.Position}, {depth}";
        }
    }
}
