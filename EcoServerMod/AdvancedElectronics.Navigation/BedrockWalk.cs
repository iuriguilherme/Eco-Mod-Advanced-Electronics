namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// Decides whether one world column rests on the world floor (U4, KTD4) — the
    /// observation `[cleared]` and `[empty]` are tested against (R7, R8, R26).
    ///
    /// <para>
    /// <b>Why this is a walk and not a single read.</b> The survey sensor takes a
    /// column's surface from <see cref="IWorldSampler.GroundHeightAt"/>, which resolves
    /// to the engine's top-<i>solid</i>-block query. A player-built block is solid, so on
    /// a capped column the recorded "surface" IS the cap, and a single read directly
    /// beneath it finds the dug-out air the mining drone left behind — never the
    /// impenetrable block under it. AE9 requires such a column to still reach bedrock, so
    /// the test walks DOWN from the recorded surface, steps past built blocks and empty
    /// space, and tests the first natural terrain block it meets.
    /// </para>
    /// <para>
    /// <b>Why it is bounded.</b> A player-built tower makes the recorded surface
    /// arbitrarily high, and every block of it is one the walk would step past. The walk
    /// therefore stops after <see cref="MaxColumnReads"/> probes with a negative answer —
    /// an unproven column never claims bedrock.
    /// </para>
    /// <para>
    /// Pure and decidable, so it lives here rather than in the sensor (KTD12): every
    /// termination rule below is unit-tested against a fake probe.
    /// </para>
    /// </summary>
    public static class BedrockWalk
    {
        /// <summary>
        /// The most probes one at-bedrock test may make: <b>160</b>.
        ///
        /// That is Eco's default world height in blocks
        /// (<c>WorldPosition3i.MaxWorldHeightForCurrentWorldSize</c>, 160), so the walk can
        /// cross a full-height column — a cap at the world ceiling over a shaft dug all the
        /// way to the world floor still reaches bedrock, which is what AE9 asks for — while
        /// never reading more positions than a single column contains. The cost is paid only
        /// by columns that are actually capped or dug out: undisturbed ground answers on the
        /// first read, because its surface is terrain, and an uncapped dug-out shaft answers
        /// on the first read too, because air is not solid so the recorded surface is already
        /// the floor.
        /// </summary>
        public const int MaxColumnReads = 160;

        /// <summary>
        /// True when the column at (<paramref name="x"/>, <paramref name="z"/>), walked down
        /// from <paramref name="surfaceY"/>, rests on the impenetrable world floor.
        ///
        /// Termination rules, in the order they are tested:
        /// <list type="bullet">
        /// <item><description><see cref="ColumnBlock.Impenetrable"/> — true, the floor is reached.</description></item>
        /// <item><description><see cref="ColumnBlock.Terrain"/> — false, ground still stands above the floor.</description></item>
        /// <item><description><see cref="ColumnBlock.Built"/> or <see cref="ColumnBlock.Empty"/> — keep walking.</description></item>
        /// <item><description>y below 0 — false, the walk ran out of world without proving anything.</description></item>
        /// <item><description><see cref="MaxColumnReads"/> probes made — false, the bound above.</description></item>
        /// </list>
        /// Every non-affirmative ending is false rather than unknown: a column that cannot be
        /// proven at bedrock must not let an area read `[cleared]`.
        /// </summary>
        public static bool ColumnRestsOnBedrock(IColumnProbe probe, int x, int surfaceY, int z)
        {
            if (probe == null)
                return false;

            var reads = 0;
            for (var y = surfaceY; y >= 0 && reads < MaxColumnReads; y--, reads++)
            {
                switch (probe.ProbeColumn(x, y, z))
                {
                    case ColumnBlock.Impenetrable:
                        return true;
                    case ColumnBlock.Terrain:
                        return false;
                    default:
                        continue;   // Built or Empty: an obstruction or a void, not the ground.
                }
            }

            return false;
        }
    }
}
