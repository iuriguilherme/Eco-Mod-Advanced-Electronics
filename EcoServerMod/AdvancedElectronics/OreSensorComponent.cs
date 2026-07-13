using AdvancedElectronics.Navigation;
using Eco.Gameplay.Objects;

namespace AdvancedElectronics
{
    /// <summary>
    /// Samples ore-bearing blocks under/around the drone as it roams and
    /// accumulates per-cell per-ore density into a <see cref="SurveyGrid"/>
    /// (U5, R7/R8, KTD5). The densest cell per ore type is then queryable
    /// via <see cref="DensestCell"/>.
    ///
    /// Per KTD3 / DroneMoverComponent.cs's own class doc (and
    /// docs/solutions/best-practices/eco-013-server-driven-movement.md),
    /// this component's own <see cref="Tick"/> override is how it gets
    /// recurring server-side work done - the mod-facing
    /// IWorldObjectManager.AddToTick / ITickOnDemand surface fires exactly
    /// once and is not usable for anything recurring.
    ///
    /// Deliberately a DISCRETE sibling component to DroneMoverComponent, not
    /// folded into it (R9: no module/plugin abstraction in v1 - this unit's
    /// entire behavior is intentionally hardcoded). A WorldObject carrying
    /// both components moves and surveys independently each tick; neither
    /// knows about the other.
    ///
    /// Grid math itself is fully delegated to
    /// AdvancedElectronics.Navigation.SurveyGrid (U5, covered by
    /// SurveyGridTests.cs) via <see cref="EcoOreReader"/> (this unit's
    /// IOreReader implementation) - this component only decides WHICH
    /// blocks to sample each tick and feeds the results in; it does not
    /// reimplement any density/cell-mapping logic itself.
    /// </summary>
    public class OreSensorComponent : WorldObjectComponent
    {
        // Design constant (not an unverified live API -- a tunable choice,
        // like DroneMoverComponent's MoveSpeedMetersPerSecond/MaxStepHeight).
        // Per KTD5, cell size is a single tunable constant; 8 world units
        // was picked as a starting middle ground between a cell coarse
        // enough to accumulate meaningful sample counts per cell in a
        // reasonable roam time and fine enough to still localize a deposit
        // usefully within a district - unconfirmed against a live district's
        // actual scale, revisit once in-game verification (out of this
        // unit's scope) is run.
        private const float SurveyCellSize = 8f;

        // The drone's own ground column plus its four orthogonal neighbors,
        // mirroring GridPathfinder's 4-connected Neighbors() shape - a
        // modest, cheap-per-tick sampling footprint rather than a full-area
        // scan, matching R9's "intentionally hardcoded v1 behavior" framing.
        private static readonly (int Dx, int Dz)[] SampleOffsets =
        {
            (0, 0),
            (1, 0),
            (-1, 0),
            (0, 1),
            (0, -1),
        };

        private SurveyGrid surveyGrid;
        private IOreReader oreReader;
        private EcoWorldSampler worldSampler;

        public override void Initialize()
        {
            base.Initialize();
            this.surveyGrid = new SurveyGrid(SurveyCellSize);
            this.oreReader = new EcoOreReader();
            // Reused only for GroundHeightAt (U3's already-established, already-
            // ASSUMPTION-documented ground-column lookup) -- this component adds
            // no new terrain-height API surface of its own.
            this.worldSampler = new EcoWorldSampler();
        }

        /// <summary>
        /// The densest observed cell for <paramref name="oreType"/> so far
        /// (argmax of ore-count / sampled-count, per KTD5 - not raw count).
        /// See <see cref="DensestCellResult.Found"/> for the "no data yet"
        /// case.
        /// </summary>
        public DensestCellResult DensestCell(string oreType) => this.surveyGrid.DensestCell(oreType);

        public override void Tick()
        {
            base.Tick();

            // R6/KTD5: gate sampling on DroneLifecycle's Surveying status when a
            // lifecycle component is present (see DroneLifecycle.ShouldSample's own
            // doc comment, which names this component as its intended consumer).
            // No lifecycle attached (e.g. a drone not yet wired to a dock, or a
            // standalone test rig) falls back to always-sample rather than never --
            // this component's own responsibility is "which blocks to sample", not
            // "whether the drone is currently allowed to survey".
            if (this.Parent.TryGetComponent<DroneLifecycle>(out var lifecycle) && !lifecycle.ShouldSample)
                return;

            var position = this.Parent.Position;
            int centerX = (int)System.MathF.Round(position.X);
            int centerZ = (int)System.MathF.Round(position.Z);

            foreach (var offset in SampleOffsets)
            {
                int x = centerX + offset.Dx;
                int z = centerZ + offset.Dz;
                int y = (int)this.worldSampler.GroundHeightAt(x, z);

                // TryGetOreType leaves oreType null for a non-ore block --
                // RecordSample treats that as "sampled, no ore" (still counts
                // toward the cell's coverage), exactly as intended.
                this.oreReader.TryGetOreType(x, y, z, out var oreType);
                this.surveyGrid.RecordSample(x, y, z, oreType);
            }
        }
    }
}
