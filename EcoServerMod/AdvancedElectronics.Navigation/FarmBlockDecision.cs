using System;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// What the drone does to one block (R14). <see cref="LeaveAlone"/> is an outcome in
    /// its own right, not the absence of one: two different rules produce it -- a crop
    /// still growing and a ripe crop held back by its ceiling -- and both are decisions
    /// the readout reports rather than gaps in the rule set.
    /// </summary>
    public enum FarmAction
    {
        PlaceDirt,
        Plow,
        Sow,
        Harvest,
        LeaveAlone
    }

    /// <summary>
    /// Everything the R14 rules ask about one block, filled by the Eco adapter and read
    /// by nothing else. The drone derives the action from what the block currently is and
    /// never from the area's history or markers (R13), so this record is the whole input:
    /// there is no per-plot memory to consult and none to go stale (R7).
    ///
    /// A citizen working the same area by hand needs no special case here (R16) -- a
    /// hand-plowed or hand-harvested block simply arrives with different facts.
    /// </summary>
    /// <remarks>
    /// <see cref="PlantSpecies"/> is an opaque key: the core compares it for equality and
    /// never interprets it, so the adapter chooses what identifies a species without this
    /// project gaining an Eco reference (KTD6). Comparison is ordinal, so two engine
    /// species differing only in case stay distinct.
    /// </remarks>
    public readonly struct FarmBlockFacts
    {
        /// <summary>Whether the plow action accepts this surface at all. The first R14 clause.</summary>
        public bool SurfaceAcceptsPlow { get; }

        /// <summary>
        /// Whether the surface is already tilled. Distinct from <see cref="SurfaceAcceptsPlow"/>
        /// on purpose: tilled dirt still answers a bare tillable test in the engine, so a
        /// single predicate would re-plow a planted field forever (KTD3).
        /// </summary>
        public bool IsTilled { get; }

        public bool HasPlant { get; }

        /// <summary>The standing plant's opaque species key, or null when nothing is growing.</summary>
        public string PlantSpecies { get; }

        /// <summary>Whether the standing plant has died. A dead plant never ripens and never recovers.</summary>
        public bool PlantIsDead { get; }

        public bool PlantIsFullyGrown { get; }

        /// <summary>
        /// Whether the area's crop is below its harvest ceiling right now, answered by
        /// <see cref="CropCeilingLedger"/> against the dock's linked storage (R27). Read
        /// only by the harvest clause.
        /// </summary>
        public bool CropIsUnderCeiling { get; }

        private FarmBlockFacts(
            bool surfaceAcceptsPlow,
            bool isTilled,
            bool hasPlant,
            string plantSpecies,
            bool plantIsDead,
            bool plantIsFullyGrown,
            bool cropIsUnderCeiling)
        {
            SurfaceAcceptsPlow = surfaceAcceptsPlow;
            IsTilled = isTilled;
            HasPlant = hasPlant;
            PlantSpecies = plantSpecies;
            PlantIsDead = plantIsDead;
            PlantIsFullyGrown = plantIsFullyGrown;
            CropIsUnderCeiling = cropIsUnderCeiling;
        }

        /// <summary>A block with nothing growing on it.</summary>
        public static FarmBlockFacts Empty(bool surfaceAcceptsPlow, bool isTilled) =>
            new FarmBlockFacts(
                surfaceAcceptsPlow,
                isTilled,
                hasPlant: false,
                plantSpecies: null,
                plantIsDead: false,
                plantIsFullyGrown: false,
                cropIsUnderCeiling: false);

        /// <summary>A block with a plant standing on it.</summary>
        public static FarmBlockFacts Planted(
            bool surfaceAcceptsPlow,
            bool isTilled,
            string plantSpecies,
            bool plantIsDead,
            bool plantIsFullyGrown,
            bool cropIsUnderCeiling)
        {
            if (string.IsNullOrEmpty(plantSpecies))
                throw new ArgumentException("A standing plant always has a species key.", nameof(plantSpecies));

            return new FarmBlockFacts(
                surfaceAcceptsPlow,
                isTilled,
                hasPlant: true,
                plantSpecies,
                plantIsDead,
                plantIsFullyGrown,
                cropIsUnderCeiling);
        }
    }

    /// <summary>
    /// R14's rules as one ordered evaluation. The whole of the farming decision lives
    /// here, Eco-free and testable without a server (KTD6), so every clause is provable
    /// by a named test rather than by watching a drone.
    /// </summary>
    public static class FarmBlockDecision
    {
        /// <summary>
        /// The action for one block, given the area's crop.
        ///
        /// The clause order below is R14's own and is normative -- it is not arranged for
        /// readability and must not be reordered. Each clause is written as an early
        /// return precisely so that precedence is the file's structure rather than a
        /// property a reader has to reconstruct.
        /// </summary>
        /// <param name="areaCrop">
        /// The area's selected crop key. Required: an area with no crop is worked not at
        /// all (R26), so this seam is never reached for one, and the guard keeps that
        /// invariant loud instead of letting a null key compare unequal to every species
        /// and quietly plow the area under.
        /// </param>
        public static FarmAction Decide(FarmBlockFacts facts, string areaCrop)
        {
            if (string.IsNullOrEmpty(areaCrop))
                throw new ArgumentException("An area with no crop selected is not worked at all (R26).", nameof(areaCrop));

            // 1. A surface the plow action will not accept gets dirt placed on it.
            //    Outranks every plant rule: ground the drone cannot farm is fixed first.
            if (!facts.SurfaceAcceptsPlow) return FarmAction.PlaceDirt;

            // 2. A plowable, untilled surface is plowed. Anything standing on untilled
            //    ground goes under with it -- it grew somewhere the area does not farm.
            if (!facts.IsTilled) return FarmAction.Plow;

            // 3. Tilled ground with nothing growing is sown with the area's crop.
            if (!facts.HasPlant) return FarmAction.Sow;

            // 4. A dead plant is plowed under whatever it was. It will never ripen, and
            //    no clause below would ever clear it: a dead crop of the area's own
            //    species short of full growth would otherwise read as "still growing"
            //    at clause 7 and hold its plot forever.
            if (facts.PlantIsDead) return FarmAction.Plow;

            var isAreaCrop = string.Equals(facts.PlantSpecies, areaCrop, StringComparison.Ordinal);

            // 5. A fully grown plant of the area's crop is harvested while that crop is
            //    below its ceiling, and left standing at or above it. Standing crops keep
            //    indefinitely, so leaving it is storage rather than waste (R27).
            if (isAreaCrop && facts.PlantIsFullyGrown)
                return facts.CropIsUnderCeiling ? FarmAction.Harvest : FarmAction.LeaveAlone;

            // 6. Any plant that is not the area's crop is plowed under, at any maturity --
            //    including ripe, which is not harvested first.
            if (!isAreaCrop) return FarmAction.Plow;

            // 7. A plant of the area's crop that is still growing is left alone.
            return FarmAction.LeaveAlone;
        }
    }
}
