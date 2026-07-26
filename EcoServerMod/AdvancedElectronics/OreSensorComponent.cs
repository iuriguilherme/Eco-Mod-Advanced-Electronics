using AdvancedElectronics.Navigation;
using Eco.Core.Controller;
using Eco.Gameplay.Objects;
using Eco.Shared.Serialization;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// The drone's ore/material sensor: a sampling PRIMITIVE, not a self-ticking scanner.
    /// <see cref="SampleColumn"/> scans one world column top-down and feeds every block into a
    /// per-area <see cref="SurveyRecord"/> (KTD11), which owns the aggregation/concentration math
    /// (U2). The record — not this component — owns the findings, so they persist with the area
    /// across a drone swap.
    ///
    /// <see cref="DroneLifecycle"/> drives sampling: on its park-and-sweep pass it parks the drone
    /// at each plot's center and calls <see cref="SampleColumn"/> for every column of that plot, so
    /// the whole plot's grid is surveyed rather than the handful of columns a roam path happened to
    /// cross. Centralizing the WHICH-columns sequencing in the lifecycle (the reliably-ticking
    /// component, KTD3) keeps this sensor stateless and independent of the drone's physical size —
    /// it scans whatever column it is handed.
    /// </summary>
    [Serialized]
    [NoIcon]
    public class OreSensorComponent : WorldObjectComponent
    {
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

        /// <summary>How deep below the surface this sensor scans, in blocks (its tier). Surfaced in the
        /// drone info and the survey readout so a player knows how far down the survey looked.</summary>
        public int SurveyReach => this.SurveyDepthBlocks;

        private IOreReader oreReader;
        private EcoWorldSampler worldSampler;

        public override void Initialize()
        {
            base.Initialize();
            this.oreReader = new EcoOreReader();
            // Reused only for GroundHeightAt (already-established, already-ASSUMPTION-documented
            // ground-column lookup) -- this component adds no new terrain-height API surface.
            this.worldSampler = new EcoWorldSampler();
        }

        /// <summary>
        /// Scans world column (<paramref name="x"/>, <paramref name="z"/>) from the surface down to
        /// the sensor's depth, recording every block into <paramref name="record"/> attributed to
        /// <paramref name="areaId"/>. Idempotent per exact block position (the record dedupes), so
        /// re-scanning a column the drone already covered does not inflate coverage or concentration.
        /// </summary>
        public void SampleColumn(int x, int z, SurveyRecord record, int areaId)
        {
            if (record == null)
                return;

            int surfaceY = (int)this.worldSampler.GroundHeightAt(x, z);
            record.RecordSurface(areaId, x, z, surfaceY); // for the area's median surface level

            // Scan DOWN from the surface: ore is underground, so reading only the surface block
            // reported "no ore" everywhere. Every block in the column counts toward the plot's
            // sampled total, so concentration stays "ore found / blocks looked at".
            for (int depth = 0; depth < this.SurveyDepthBlocks; depth++)
            {
                int y = surfaceY - depth;
                if (y < 0)
                    break;

                // TryGetOreType leaves oreType null for a non-ore block -- RecordSample treats that
                // as "sampled, no ore" (still counts toward the plot's coverage), exactly as intended.
                this.oreReader.TryGetOreType(x, y, z, out var oreType);
                record.RecordSample(x, y, z, oreType, depth, areaId);
            }
        }
    }
}
