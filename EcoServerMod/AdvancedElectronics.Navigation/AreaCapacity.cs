using System;

namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// <b>How many areas one dock may hold, and how far its tab cursors may reach (U10, R19,
    /// KTD7).</b> One limit over the dock's ONE area collection, replacing the separate farm
    /// and survey limits that existed only because farms and mines were two types.
    ///
    /// <para>
    /// Pure arithmetic, in the Eco-free assembly, because it is the one decidable part of U10:
    /// everything else in that unit is picker and tab glue that the test project cannot
    /// reference. Both tabs and both pickers read these, so the cap a player meets is the same
    /// number wherever they meet it.
    /// </para>
    /// </summary>
    public static class AreaCapacity
    {
        /// <summary>
        /// The one per-dock area limit (R19, KTD7). Ten rather than eight: ten is what the
        /// survey side already allowed, and lowering it would put existing docks over a limit
        /// for a reason the player never chose.
        /// </summary>
        public const int MaxAreasPerDock = 10;

        /// <summary>
        /// The legacy farm limit, kept as a NUMBER rather than as a rule. Nothing enforces it
        /// any more — it is here only because it is half of how far over
        /// <see cref="MaxAreasPerDock"/> a pre-fold save can legitimately arrive, which is what
        /// <see cref="MaxAddressablePositions"/> has to cover.
        /// </summary>
        public const int MaxLegacyFarmAreas = 8;

        /// <summary>
        /// How far a tab's selection cursor must be able to reach: every area a dock can be
        /// holding, not every area it may ADD (U10 step 3).
        ///
        /// <para>
        /// A dock carried forward from before the fold could hold ten survey areas AND eight
        /// farms, and KTD7 keeps every one of them. A cursor bounded by
        /// <see cref="MaxAreasPerDock"/> would leave the areas past the tenth unreachable — the
        /// player could see them in the list and never select, assign or delete one, which is
        /// the state KTD7 exists to avoid. The kind change makes the worst case a single tab's
        /// too: <c>/drone areakind</c> can turn all eighteen into farms.
        /// </para>
        /// <para>
        /// It is a ceiling on the CONTROL, not a second cap. The live clamp is
        /// <see cref="ClampToKindCount"/>, against the count of the kind that tab shows.
        /// </para>
        /// </summary>
        public const int MaxAddressablePositions = MaxAreasPerDock + MaxLegacyFarmAreas;

        /// <summary>
        /// Whether a dock holding <paramref name="currentAreaCount"/> areas may be given
        /// another one (R19, KTD7).
        ///
        /// <para>
        /// "At or over", not "equal to". A dock the fold left above the limit keeps every area
        /// it has and may add none until deletions bring it back under, which is the same shape
        /// as the cap refusal that was already there — one rule rather than two.
        /// </para>
        /// </summary>
        public static bool MayAdd(int currentAreaCount) => currentAreaCount < MaxAreasPerDock;

        /// <summary>
        /// How far above the limit a dock is, or zero when it is not. What a diagnostic prints;
        /// nothing branches on it, because <see cref="MayAdd"/> is the whole of the rule.
        /// </summary>
        public static int OverLimitBy(int currentAreaCount) =>
            Math.Max(0, currentAreaCount - MaxAreasPerDock);

        /// <summary>
        /// A tab cursor clamped against the number of areas THAT TAB shows
        /// (<paramref name="kindCount"/>), never against the dock's whole collection.
        ///
        /// <para>
        /// The two tabs are separate views onto one collection (KTD7), so a Farming tab cursor
        /// clamped against the total would let a player select past the last farm and land on
        /// nothing, and the reverse on the Survey tab. Returns 0 for an empty list, which is
        /// what every caller renders as "no areas yet".
        /// </para>
        /// </summary>
        public static int ClampToKindCount(int index, int kindCount)
        {
            if (kindCount <= 0) return 0;
            if (index < 0) return 0;
            return index >= kindCount ? kindCount - 1 : index;
        }
    }
}
