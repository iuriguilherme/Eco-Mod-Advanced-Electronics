namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// How well a crop can live on one plot, and what is holding it back (R32, R38).
    /// </summary>
    /// <remarks>
    /// Fitness is a property of the plot AND the crop together, never of the plot alone:
    /// a waterlogged plot suits rice and refuses corn, so a single contamination flag on
    /// the ground could not answer the question.
    /// </remarks>
    public readonly struct GroundFitness
    {
        /// <summary>
        /// The rating below which the engine treats an environment as lethal, mirroring
        /// <c>PlantSpecies.MinHabitability</c> (0.4f). Mirrored rather than referenced
        /// because this project carries no Eco dependency (KTD6).
        ///
        /// This is the whole reason the probe reports a rating instead of a flag. The
        /// engine's growth pass reads <c>habitability &lt; MinHabitability</c> and zeroes
        /// it, killing whatever grows there, so a plot rated 0.1 is nonzero and still
        /// lethal. A test for zero alone sows a field that dies.
        /// </summary>
        public const float MinHabitability = 0.4f;

        /// <summary>The engine's habitability rating for this crop here, from 0 to 1.</summary>
        public float Rating { get; }

        /// <summary>
        /// The dominant limiting condition -- pollution, temperature, moisture, salt water
        /// -- or null when nothing is holding the crop back. Named rather than generic so
        /// the readout does not call a temperature refusal contamination (R38).
        /// </summary>
        public string Condition { get; }

        private GroundFitness(float rating, string condition)
        {
            Rating = rating;
            Condition = condition;
        }

        /// <summary>
        /// Whether the crop can live here. The engine's own comparison is strictly less
        /// than, so the constant itself is on the living side.
        /// </summary>
        public bool IsFit => Rating >= MinHabitability;

        public static GroundFitness Rate(float rating, string limitingCondition) =>
            new GroundFitness(rating, limitingCondition);
    }

    /// <summary>
    /// Asks the engine whether a crop can grow at a position (R32). Declared here so the
    /// block decision stays Eco-free (KTD6); the mod implements it against live world
    /// layers in <c>EcoServerMod/AdvancedElectronics/EcoGroundFitness.cs</c> and tests use
    /// a hand-rolled stub, the same shape <see cref="IWorldSampler"/> and
    /// <see cref="IOreReader"/> already follow.
    /// </summary>
    /// <remarks>
    /// Read-only by construction. R33's never-remediate rule is enforced by this seam
    /// having no write path at all rather than by every caller remembering not to take
    /// one: ground recovers on its own terms once the condition lifts.
    /// </remarks>
    public interface IGroundFitness
    {
        /// <param name="speciesKey">The same opaque crop key <see cref="FarmBlockDecision"/> compares.</param>
        GroundFitness Rate(string speciesKey, int x, int y, int z);
    }

    /// <summary>One plot's action, and the fitness refusal behind it when there is one.</summary>
    public readonly struct FarmPlotOutcome
    {
        public FarmAction Action { get; }

        /// <summary>Whether this plot would have been sown but for the ground.</summary>
        public bool WasRefusedForFitness { get; }

        /// <summary>The engine's condition for that refusal, when it named one.</summary>
        public string UnfitCondition { get; }

        /// <summary>
        /// The surface height this decision was made against.
        ///
        /// Carried on the outcome so the caller acts on the block it actually judged. Where
        /// the caller re-sampled the ground to find the position, a plant growing or a
        /// neighbour digging between the two reads would have it act one block off.
        /// </summary>
        public int SurfaceY { get; }

        internal FarmPlotOutcome(FarmAction action, bool refusedForFitness, string unfitCondition, int surfaceY)
        {
            Action = action;
            WasRefusedForFitness = refusedForFitness;
            UnfitCondition = unfitCondition;
            SurfaceY = surfaceY;
        }
    }

    /// <summary>
    /// R14's block decision with R32's fitness gate over it. Kept separate from
    /// <see cref="FarmBlockDecision"/> so the rule set stays exactly R14 and the gate
    /// stays exactly R32, each provable on its own.
    /// </summary>
    public static class FarmPlotDecision
    {
        /// <summary>
        /// The action for one plot. Fitness gates sowing and nothing else: the drone still
        /// plows, harvests and fills over ground a crop could not live on, because R33
        /// leaves the ground to recover on its own and R34 keeps one bad plot from
        /// stalling the area around it.
        /// </summary>
        public static FarmPlotOutcome Decide(
            FarmBlockFacts facts, string areaCrop, GroundFitness fitness, int surfaceY = 0)
        {
            var action = FarmBlockDecision.Decide(facts, areaCrop);

            if (action != FarmAction.Sow || fitness.IsFit)
                return new FarmPlotOutcome(action, refusedForFitness: false, unfitCondition: null, surfaceY);

            // Left alone rather than skipped-with-an-error: the plot is fine, the ground
            // is not, and it becomes sowable again by itself once the condition lifts.
            return new FarmPlotOutcome(FarmAction.LeaveAlone, refusedForFitness: true, fitness.Condition, surfaceY);
        }
    }
}
