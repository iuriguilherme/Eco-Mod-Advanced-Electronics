using System;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// The flat vanilla yield rate per <see cref="BlockClassification"/> (KD11, R15):
    /// four items for a minable block, one for an excavatable block, unaffected by
    /// tool, vehicle tool, or talent. Both constants are injected from the Eco side
    /// rather than hard-coded here, so nothing in this mod carries its own guess at
    /// the engine's rubble-per-block constant (U3's approach).
    /// </summary>
    public sealed class YieldTable
    {
        private readonly int _minableYield;
        private readonly int _excavatableYield;

        public YieldTable(int minableYield, int excavatableYield)
        {
            if (minableYield < 0)
                throw new ArgumentOutOfRangeException(nameof(minableYield));
            if (excavatableYield < 0)
                throw new ArgumentOutOfRangeException(nameof(excavatableYield));

            _minableYield = minableYield;
            _excavatableYield = excavatableYield;
        }

        /// <summary>Yield for <paramref name="classification"/>. Zero for <see cref="BlockClassification.NotRemovable"/> -- it is never a valid input to a removal.</summary>
        public int YieldFor(BlockClassification classification) => classification switch
        {
            BlockClassification.Minable => _minableYield,
            BlockClassification.Excavatable => _excavatableYield,
            _ => 0
        };
    }
}
