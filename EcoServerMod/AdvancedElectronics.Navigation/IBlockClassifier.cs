namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// What a block at a position may be removed as, or that it may not be removed at
    /// all (R14). The category only -- yield is a separate concern (<see cref="YieldTable"/>),
    /// so this seam answers exactly one question and a caller cannot ask it for a
    /// count it was never designed to give.
    /// </summary>
    public enum BlockClassification
    {
        Minable,
        Excavatable,
        NotRemovable
    }

    /// <summary>
    /// Answers "may the drone remove the block at this position, and as what" without
    /// letting the pure caller (U13's strategy) touch any Eco type (KTD6). Mirrors the
    /// established <see cref="IOreReader"/>/<see cref="IWorldSampler"/> shape: a real
    /// mod implements this against live Eco block data
    /// (<c>EcoServerMod/AdvancedElectronics/EcoBlockClassifier.cs</c>), tests use a
    /// hand-rolled fake.
    /// </summary>
    public interface IBlockClassifier
    {
        BlockClassification Classify(int x, int y, int z);
    }
}
