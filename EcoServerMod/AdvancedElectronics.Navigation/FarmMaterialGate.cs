namespace AdvancedElectronics.Navigation
{
    /// <summary>A material a farm action needs from the hold or linked storage.</summary>
    public enum FarmMaterial
    {
        None,
        Seed,
        Dirt
    }

    /// <summary>
    /// Whether the drone may take a block action with the materials it can reach.
    /// </summary>
    /// <remarks>
    /// Everything short of a harvest prepares ground for sowing. Without the seed to
    /// sow, preparing is worse than waiting: tilled ground left bare is taken over by
    /// wild plants and has to be plowed again. So with no seed the drone does nothing but
    /// harvest, and an area whose only work is preparation stops as short of seed --
    /// which is also what keeps the drone from being offered the same plot forever and
    /// standing at it with nothing it can do.
    /// </remarks>
    public static class FarmMaterialGate
    {
        /// <summary>
        /// The material <paramref name="action"/> lacks, or <see cref="FarmMaterial.None"/>
        /// when it can go ahead. Seed is reported before dirt: without seed the dirt would
        /// not be placed anyway.
        /// </summary>
        public static FarmMaterial Missing(FarmAction action, bool hasSeed, bool hasDirt)
        {
            switch (action)
            {
                case FarmAction.Plow:
                case FarmAction.Relay:
                case FarmAction.Sow:
                    return hasSeed ? FarmMaterial.None : FarmMaterial.Seed;

                case FarmAction.PlaceDirt:
                    if (!hasSeed) return FarmMaterial.Seed;
                    return hasDirt ? FarmMaterial.None : FarmMaterial.Dirt;

                default:
                    return FarmMaterial.None;
            }
        }
    }
}
