namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// What the v1 drone tier can do (KD4). One place, because these numbers constrain each other:
    /// how deep a shaft goes decides how far the drone must be able to descend to re-enter it, and
    /// the two drifting apart is not a compile error -- it is a drone that cannot get back into the
    /// hole it just dug.
    ///
    /// Here rather than beside the mining code because <see cref="ReturnEscalation"/> needs it and
    /// this assembly cannot see any Eco type (KTD6). A tier is gameplay arithmetic, not Eco glue.
    /// </summary>
    public static class DroneTier
    {
        /// <summary>
        /// Layers one mining pass removes below each column's own surface (R12, KD4). A tier
        /// property, not a caller-chosen value.
        /// </summary>
        public const int MiningShaftDepth = 15;
    }
}
