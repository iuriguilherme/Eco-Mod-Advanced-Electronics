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

    /// <summary>
    /// What one block in a column IS, for the single purpose of finding the ground
    /// under whatever is standing on it (U4, KTD4). Deliberately not
    /// <see cref="BlockClassification"/>: that enum answers "may the drone remove
    /// this", and collapses empty space, a built wall, a world object and the
    /// world floor into one <see cref="BlockClassification.NotRemovable"/> answer.
    /// The at-bedrock walk has to tell those apart, so it asks a different question.
    /// </summary>
    public enum ColumnBlock
    {
        /// <summary>Nothing there — air, or water standing in a shaft the drone dug out.</summary>
        Empty,

        /// <summary>
        /// Something put there rather than terrain: a built or form-bearing block, a
        /// placed world object's footprint, a ramp, tree debris, a plant. R7 calls these
        /// obstructions of a single column rather than of the ground, so the walk steps
        /// past them exactly as the mining pass does.
        /// </summary>
        Built,

        /// <summary>
        /// Natural terrain that is not the world floor — dirt, sand, stone, ore. Ground
        /// standing above bedrock, so the walk stops here with a negative answer. Note
        /// that dirt holding a plant is this, not <see cref="Built"/>: the plant is the
        /// obstruction, the dirt beneath it is still ground (R7).
        /// </summary>
        Terrain,

        /// <summary>
        /// The world floor itself: the block carrying the engine's impenetrable
        /// attribute, which nothing — player, drone or tool — can remove. Reaching it is
        /// what "at bedrock" means (R7, R26).
        /// </summary>
        Impenetrable
    }

    /// <summary>
    /// Reads one world position as a <see cref="ColumnBlock"/> so <see cref="BedrockWalk"/>
    /// can find the ground beneath a column without touching an Eco type (KTD12). Mirrors the
    /// established <see cref="IOreReader"/>/<see cref="IWorldSampler"/>/<see cref="IBlockClassifier"/>
    /// shape: the mod implements it against live block data
    /// (<c>EcoServerMod/AdvancedElectronics/EcoBlockClassifier.cs</c>), tests use a hand-rolled fake.
    ///
    /// Kept a separate interface from <see cref="IBlockClassifier"/> on purpose — that seam
    /// documents itself as answering exactly one question, and this is a second one.
    /// </summary>
    public interface IColumnProbe
    {
        ColumnBlock ProbeColumn(int x, int y, int z);
    }
}
