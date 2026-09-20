using System.Collections.Generic;
using AdvancedElectronics.Navigation;
using Eco.Gameplay.Auth;
using Eco.Gameplay.Civics.Laws;
using Eco.Gameplay.GameActions;
using Eco.Shared.IoC;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// The fail-closed assertions and the refusal classification every hand-built game
    /// action pack in this mod shares.
    ///
    /// Extracted when the farming work added a second and third pack-building service and
    /// the checks began drifting apart between copies. There is nothing farming-specific
    /// here: any service that assembles a pack rather than calling one of Eco's one-call
    /// helpers owes the world these same guarantees.
    /// </summary>
    /// <remarks>
    /// <see cref="MiningRemovalService"/> deliberately keeps its own copy. It is the
    /// oldest and most exercised of the three, no automated test covers any of them, and
    /// the farming work is required to leave the mining drone behaving exactly as before
    /// -- so a mechanical edit there would buy tidiness at the cost of the one path a slip
    /// would be hardest to notice in. Change both together, or neither.
    /// </remarks>
    internal static class GameActionPackGuards
    {
        /// <summary>
        /// The invariants that must hold before a pack is performed, or null when they
        /// all do. Each one closes a way the pack could authorize something it should not:
        /// flags that relax the pipeline, an action with no citizen to authorize against,
        /// and an action that waives authorization outright.
        /// </summary>
        public static string InvariantFailure(GameActionPack pack, IReadOnlyList<GameAction> actions)
        {
            if (pack.PackFlags != default) return "Pack flags were set.";

            foreach (var action in actions)
            {
                if (action is IUserGameAction { Citizen: null }) return "An action carried no citizen.";
                if (action.AuthIgnored) return "An action waived authorization.";
            }

            return null;
        }

        /// <summary>
        /// Best-effort classification of a refusal, run only after the pack has already
        /// been refused.
        ///
        /// Law before property, which is the pipeline's own evaluation order, so the answer
        /// matches what actually stopped the action.
        ///
        /// NOT read-only, contrary to what this comment claimed and what the mining
        /// service's copy still claims. <c>ILawManager.Perform</c> reaches
        /// <c>LawSection.CheckConditionsAndApplyEffects</c>, which sets
        /// <c>action.CurrentSettlement</c> and runs each law's if/then blocks -- so
        /// classifying a refusal re-evaluates every matched law a second time. The
        /// currency half lands in the pack's own <c>AccountChangeSet</c>, which a refused
        /// pack never commits, but any effect that does not travel through that change set
        /// is applied for real, once per refusal.
        ///
        /// Left in place rather than rewritten under review: the same call is the mining
        /// drone's most exercised world-write path, the blast radius depends on effects
        /// this mod cannot enumerate statically, and a live session is the only way to
        /// settle it. Recorded as a known defect for both paths rather than papered over.
        /// </summary>
        public static RemovalRefusalStage ClassifyRefusal(GameActionPack pack, IReadOnlyList<GameAction> actions)
        {
            var accountChangeSet = pack.GetAccountChangeSet();
            foreach (var action in actions)
                if (!ServiceHolder<ILawManager>.Obj.Perform(action, accountChangeSet))
                    return RemovalRefusalStage.SettlementLaw;

            foreach (var action in actions)
                if (!ServiceHolder<IAuthManager>.Obj.IsAuthorized(action, out _).Success)
                    return RemovalRefusalStage.Property;

            return RemovalRefusalStage.Pretest;
        }
    }
}
