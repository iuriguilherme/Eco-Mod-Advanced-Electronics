using System.Linq;
using AdvancedElectronics.Navigation;
using Eco.Shared.Math;
using Eco.Simulation;
using Eco.Simulation.Types;
using Eco.Simulation.WorldLayers.Layers;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// Live-Eco implementation of the navigation core's <see cref="IGroundFitness"/>
    /// (U8, R32, R38), following <see cref="EcoWorldSampler"/>'s shape: a translation
    /// layer with no farming logic of its own, so the block decision stays Eco-free.
    ///
    /// The crop key is the engine's own species name, which <c>EcoSim.GetSpecies</c>
    /// resolves case-insensitively. That is the whole of the opaque-key contract the core
    /// declares: the core compares keys, this side decides what one is.
    /// </summary>
    /// <remarks>
    /// Read-only, by construction rather than by discipline. R33 says the drone never
    /// remediates unfit ground, and this class has no write path at all -- the ground
    /// recovers on its own terms once the condition lifts, and there is nothing here that
    /// could try to help it along.
    /// </remarks>
    public sealed class EcoGroundFitness : IGroundFitness
    {
        /// <summary>
        /// Rates the crop's habitability at the column, using the engine's own reading of
        /// temperature, rainfall, ground pollution and salt water together (KTD5).
        ///
        /// The rating is returned rather than a verdict because the boundary matters: the
        /// engine's growth pass zeroes anything under
        /// <c>PlantSpecies.MinHabitability</c> and kills what grows there, so a plot rated
        /// 0.1 is nonzero and still lethal. The core's <see cref="GroundFitness"/> mirrors
        /// that constant and applies the same comparison.
        /// </summary>
        public GroundFitness Rate(string speciesKey, int x, int y, int z)
        {
            // A key naming nothing the world knows is unfit rather than an exception: a
            // saved area whose crop a mod update removed should stall that area with a
            // readable reason, not fault the drone's whole pass.
            if (!(EcoSim.GetSpecies(speciesKey) is PlantSpecies species))
                return GroundFitness.Rate(0f, "an unknown crop");

            var layerPos = LayerPosition.FromWorldPosition(new Vector2i(x, z), species.VoxelsPerEntry);
            var rating = species.CheckAreaForHabitability(
                layerPos, out var temperature, out var moisture, out var pollution, out var saltwater);

            // The limiting condition is only worth naming when something is actually
            // limiting: a fit plot has no reason to report.
            return rating >= GroundFitness.MinHabitability
                ? GroundFitness.Rate(rating, null)
                : GroundFitness.Rate(rating, DominantLimit(species, temperature, moisture, pollution, saltwater));
        }

        /// <summary>
        /// Which of the four factors is holding this crop back the most (R38).
        ///
        /// The engine multiplies the four per-factor matches together, so the worst one is
        /// what a citizen would act on. Naming it beats calling every refusal
        /// contamination: a plot refused for temperature sends someone to clean pollution
        /// that was never there.
        /// </summary>
        private static string DominantLimit(
            PlantSpecies species, float temperature, float moisture, float pollution, float saltwater)
        {
            // Pollution leads the list so it wins a tie. It is the condition a settlement
            // most often causes and most often can undo.
            var factors = new[]
            {
                ("ground pollution", PlantSpecies.RangesToModifier(
                    new Range(0f, species.MaxPollutionDensity),
                    new Range(0f, species.PollutionDensityTolerance),
                    pollution)),
                ("temperature", PlantSpecies.RangesToModifier(
                    species.TemperatureExtremes, species.IdealTemperatureRange, temperature)),
                ("rainfall", PlantSpecies.RangesToModifier(
                    species.MoistureExtremes, species.IdealMoistureRange, moisture)),
                ("salt water", PlantSpecies.RangesToModifier(
                    species.WaterExtremes, species.IdealWaterRange, saltwater))
            };

            return factors.OrderBy(factor => factor.Item2).First().Item1;
        }
    }
}
