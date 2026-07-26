using System.Collections.Generic;
using AdvancedElectronics.Navigation;
using Eco.Core.Controller;
using Eco.Gameplay.Objects;
using Eco.Shared.Serialization;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// Samples ore-bearing blocks under/around the drone as it roams and feeds each sample
    /// into the OWNING DOCK's per-area <see cref="SurveyRecord"/> (KTD11), attributed to the
    /// dock's currently-assigned survey area. The record — not this component — owns the
    /// findings, so they persist with the area across a drone swap and are not tied to this
    /// sensor instance. This component only decides WHICH blocks to sample each tick and where
    /// to attribute them; all aggregation/concentration math lives in the Eco-free
    /// <see cref="SurveyRecord"/> (U2, covered by its own tests).
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
    /// </summary>
    [Serialized]
    [NoIcon]
    public class OreSensorComponent : WorldObjectComponent
    {
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

        /// <summary>
        /// How far below the surface this sensor can see, in blocks. Ore sits
        /// underground, so a sensor reading only the surface found nothing no matter how
        /// far the drone roamed.
        ///
        /// This is the sensor's TIER, deliberately mirroring how vanilla prospecting
        /// tools differ: the iron rock drill reaches 15 blocks, the modern rock drill 30.
        /// v1 ships at the iron-drill tier; a deeper sensor is a natural higher-tier
        /// drone (harder to craft, sees more), the same progression axis as the
        /// climb-height limit on the mover. Virtual so a future subclass sets its own
        /// reach without touching sampling logic.
        /// </summary>
        protected virtual int SurveyDepthBlocks => 15;

        // One column is scanned per tick, cycling through SampleOffsets, so the
        // per-tick cost stays near the old single-block read (SurveyDepthBlocks lookups)
        // rather than multiplying by the whole footprint. The drone roams continuously,
        // so coverage accumulates as it moves.
        private int nextSampleOffset;

        private IOreReader oreReader;
        private EcoWorldSampler worldSampler;

        public override void Initialize()
        {
            base.Initialize();
            this.oreReader = new EcoOreReader();
            // Reused only for GroundHeightAt (U3's already-established, already-
            // ASSUMPTION-documented ground-column lookup) -- this component adds
            // no new terrain-height API surface of its own.
            this.worldSampler = new EcoWorldSampler();
        }

        public override void Tick()
        {
            base.Tick();

            // R6/KTD5: gate sampling on DroneLifecycle's Surveying status. No lifecycle,
            // no home dock, or no assigned area means there is no survey area to attribute
            // samples to -- nothing to do this tick (findings are per-area now, KTD11).
            if (!this.Parent.TryGetComponent<DroneLifecycle>(out var lifecycle) || !lifecycle.ShouldSample)
                return;
            if (lifecycle.HomeDock is not DroneDockObject dock)
                return;
            var areaId = dock.AssignedSurveyAreaId;
            if (areaId == 0)
                return;

            var position = this.Parent.Position;
            int centerX = (int)System.MathF.Round(position.X);
            int centerZ = (int)System.MathF.Round(position.Z);

            // Prospect ONE column per tick, cycling through the footprint.
            var offset = SampleOffsets[this.nextSampleOffset];
            this.nextSampleOffset = (this.nextSampleOffset + 1) % SampleOffsets.Length;

            int x = centerX + offset.Dx;
            int z = centerZ + offset.Dz;
            int surfaceY = (int)this.worldSampler.GroundHeightAt(x, z);

            // Scan DOWN from the surface: ore is underground, so reading only the
            // surface block reported "no ore" everywhere regardless of what the drone
            // was standing on. Every block in the column counts toward the plot's
            // sampled total, so concentration stays "ore found / blocks looked at".
            var record = dock.SurveyRecord;
            for (int depth = 0; depth < this.SurveyDepthBlocks; depth++)
            {
                int y = surfaceY - depth;
                if (y < 0)
                    break;

                // TryGetOreType leaves oreType null for a non-ore block --
                // RecordSample treats that as "sampled, no ore" (still counts
                // toward the plot's coverage), exactly as intended.
                this.oreReader.TryGetOreType(x, y, z, out var oreType);
                record.RecordSample(x, y, z, oreType, depth, areaId);
            }
        }
    }
}
