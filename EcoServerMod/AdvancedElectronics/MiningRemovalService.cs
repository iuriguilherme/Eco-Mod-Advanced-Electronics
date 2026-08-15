using System.Collections.Generic;
using AdvancedElectronics.Navigation;
using Eco.Gameplay.Auth;
using Eco.Gameplay.Civics.Laws;
using Eco.Gameplay.GameActions;
using Eco.Gameplay.Items;
using Eco.Gameplay.Players;
using Eco.Shared.IoC;
using Eco.Shared.Items;
using Eco.Shared.Math;
using Eco.Simulation;
using EcoWorld = Eco.World.World;

namespace Eco.Mods.TechTree
{
    public enum RemovalOutcome
    {
        Succeeded,
        Refused
    }

    /// <summary>One layer's removal attempt: succeeded, or refused with the stage that refused it and the engine's own message (R22).</summary>
    public sealed class RemovalResult
    {
        public RemovalOutcome Outcome { get; }
        public RemovalRefusalStage RefusalStage { get; }
        public string Message { get; }

        private RemovalResult(RemovalOutcome outcome, RemovalRefusalStage stage, string message)
        {
            this.Outcome = outcome;
            this.RefusalStage = stage;
            this.Message = message;
        }

        public static RemovalResult Success() => new(RemovalOutcome.Succeeded, default, null);

        public static RemovalResult Refusal(RemovalRefusalStage stage, string message) =>
            new(RemovalOutcome.Refused, stage, message);
    }

    /// <summary>
    /// Removes one shaft layer as the stamped citizen, granting yields to the hold, and
    /// reports why the engine refused when it does (U5, KTD1, KTD14, KTD15).
    ///
    /// Never unit-tested (every collaborator is an Eco type). Proven by the nine-item
    /// static-review checklist below (recorded per unit) plus the live pass. Static-review
    /// checklist -- confirm each by reading this file:
    /// 1. The action types are the engine's exactly (<see cref="DigOrMine"/>,
    ///    <see cref="DropOrPickupBlock"/>, <see cref="HarvestOrHunt"/>) -- never derived.
    /// 2. The citizen comes from the direct <c>Fill(User, ...)</c> overload, never the
    ///    <c>MultiblockActionContext</c> overload.
    /// 3. No access argument is assigned (<c>Fill</c> is always called with <c>access: null</c>).
    /// 4. Pack flags are unset (<see cref="Refuse"/>-before-perform assertion below).
    /// 5. No action waives authorization (<c>AuthIgnored</c> assertion below).
    /// 6. The pack is performed via <c>TryPerform</c>, never dry-run and never forced.
    /// 7. Every position is set before performing (assertion below).
    /// 8. The citizen assertion runs before performing and refuses on failure.
    /// 9. The dig-action-count assertion runs before performing and refuses on failure.
    /// </summary>
    public sealed class MiningRemovalService
    {
        /// <summary>
        /// Removes every position in <paramref name="positions"/> as one game-action pack
        /// (KTD14 -- one pack per shaft layer). The caller has already classified each
        /// position (R14, KD9) -- this service never classifies for its own decision, only
        /// to re-check a position has not changed since classification (R38).
        /// </summary>
        public RemovalResult Remove(
            IReadOnlyList<(BlockPos Position, BlockClassification Classification)> positions,
            User stampedCitizen,
            Item tool,
            Inventory hold,
            YieldTable yieldTable,
            IBlockClassifier classifier)
        {
            if (positions == null || positions.Count == 0)
                return RemovalResult.Success();

            // R36: fail closed on the one condition the engine itself fails open on.
            if (stampedCitizen == null)
                return RemovalResult.Refusal(RemovalRefusalStage.Unrecognised, "No stamped citizen -- refusing fail-closed.");

            var pack = new GameActionPack();
            var actions = new List<GameAction>();
            var digCount = 0;

            foreach (var (pos, classification) in positions)
            {
                // Caller invariant (R14, KD9): the service never classifies for its own
                // decision, but a not-removable position reaching here is a caller bug,
                // not a refusal to negotiate around.
                if (classification == BlockClassification.NotRemovable)
                    return RemovalResult.Refusal(RemovalRefusalStage.Unrecognised, "Caller passed a not-removable position.");

                if (pos.Equals(default(BlockPos)))
                    return RemovalResult.Refusal(RemovalRefusalStage.Unrecognised, "A position was left at its sentinel default (R36).");

                if (!WrappedWorldPosition3i.TryCreate(new Vector3i(pos.X, pos.Y, pos.Z), out var wrapped))
                    return RemovalResult.Refusal(RemovalRefusalStage.Pretest, "Position is outside world bounds.");

                // Never through the engine's one-call helper (KTD1) -- these exact action
                // types, filled with the direct citizen-taking overload, no access override.
                var dig = (GameAction)new DigOrMine().Fill(stampedCitizen, tool, wrapped, null);
                pack.AddGameAction(dig);
                actions.Add(dig);
                digCount++;

                // Reproduces the engine's own "always add a pickup for every removed block"
                // behaviour (Engine Reference) -- the block becoming an item, not the
                // built-block case, so a law against block pickup applies here too (R19).
                var itemUsed = (dig as IItemGameAction)?.ItemUsed;
                var pickup = (GameAction)new DropOrPickupBlock(DroppedOrPickedUp.PickedUp).Fill(stampedCitizen, tool, wrapped, null, itemUsed);
                pack.AddGameAction(pickup);
                actions.Add(pickup);

                // A plant standing above is destroyed and yields nothing (R34, KD18): the
                // action is raised so a law against it still applies, but no harvest stacks
                // are attached and no yield is calculated -- digging is not harvesting.
                if (wrapped.TryIncreaseY(1, out var above) && EcoSim.PlantSim.GetPlant(above) is { } plant)
                {
                    var harvest = new HarvestOrHunt
                    {
                        ActionLocation = (Vector3i)above,
                        Citizen = stampedCitizen,
                        ToolUsed = tool,
                        Species = plant.Species.GetType(),
                        DamagedOrDestroyed = DamagedOrDestroyed.DestroyingOrganism,
                        AccessNeeded = AccessType.ConsumerAccess,
                        DestroyedByBlock = false,
                    };
                    pack.AddGameAction(harvest);
                    actions.Add(harvest);

                    var destroyedPlant = plant;
                    pack.AddPostEffect(() => EcoSim.PlantSim.DestroyPlant(destroyedPlant, DeathType.Construction, true, stampedCitizen));
                }

                // The flat vanilla yield (KD11) -- deliberately not the engine's own
                // BlockItem-stack-state decrement logic (KD3: the drone keeps every block
                // it breaks, so there is no partial removal to express).
                if (itemUsed != null)
                {
                    var yield = yieldTable.YieldFor(classification);
                    if (yield > 0)
                        pack.AddToInventory(hold, itemUsed, yield, stampedCitizen);
                }

                var deletePos = wrapped;
                pack.AddPostEffect(() => EcoWorld.DeleteBlock(deletePos));
            }

            // R38: re-read and re-test every position immediately before performing.
            pack.AddChangeSet(new BlockUnchangedChangeSet(positions, classifier));

            var invariantFailure = CheckInvariants(pack, actions, digCount, positions.Count);
            if (invariantFailure != null)
                return invariantFailure;

            // Never dry-run, never forced (R35). No user to notify -- the drone works
            // unattended, and a per-layer chat toast for a citizen who is very likely
            // offline is not the UX this delivery wants.
            var result = pack.TryPerform(null);
            if (result)
                return RemovalResult.Success();

            return RemovalResult.Refusal(ClassifyRefusal(pack, actions), result.Message.ToString());
        }

        /// <summary>
        /// R36's fail-closed assertions, run immediately before performing. Returns null
        /// when every invariant holds; otherwise the refusal to return instead of performing.
        /// </summary>
        private static RemovalResult CheckInvariants(GameActionPack pack, List<GameAction> actions, int digCount, int positionCount)
        {
            if (pack.PackFlags != default)
                return RemovalResult.Refusal(RemovalRefusalStage.Unrecognised, "Pack flags were set.");

            if (digCount != positionCount)
                return RemovalResult.Refusal(RemovalRefusalStage.Unrecognised, "Dig-action count did not equal the deleted-position count.");

            foreach (var action in actions)
            {
                if (action is IUserGameAction { Citizen: null })
                    return RemovalResult.Refusal(RemovalRefusalStage.Unrecognised, "An action carried no citizen.");
                if (action.AuthIgnored)
                    return RemovalResult.Refusal(RemovalRefusalStage.Unrecognised, "An action waived authorization.");
            }

            return null;
        }

        /// <summary>
        /// Best-effort classification of a refusal into R31's skip categories, run only
        /// after the pack has already been refused. Re-runs the same read-only checks the
        /// pipeline itself performs (laws, then per-action authorization -- R21's
        /// evaluation order) to tell which stage was responsible; neither check mutates
        /// anything. A refusal that clears both is attributed to a pretest (R38's own
        /// block-changed re-check is the only pretest this service attaches).
        /// </summary>
        private static RemovalRefusalStage ClassifyRefusal(GameActionPack pack, List<GameAction> actions)
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

        /// <summary>R38's block-changed re-check, wired in through the only pretest hook a mod can reach (a mod cannot add to <c>GameActionPack.PreTests</c> directly -- it is internal).</summary>
        private sealed class BlockUnchangedChangeSet : IGameActionPackChangeSet
        {
            private readonly IReadOnlyList<(BlockPos Position, BlockClassification Classification)> positions;
            private readonly IBlockClassifier classifier;

            public BlockUnchangedChangeSet(
                IReadOnlyList<(BlockPos Position, BlockClassification Classification)> positions,
                IBlockClassifier classifier)
            {
                this.positions = positions;
                this.classifier = classifier;
            }

            public Eco.Shared.Localization.LocString GameActionPackPostEffect() => Eco.Shared.Localization.LocString.Empty;

            public Eco.Core.Utils.Result GameActionPackPretest()
            {
                foreach (var (pos, expected) in this.positions)
                    if (this.classifier.Classify(pos.X, pos.Y, pos.Z) != expected)
                        return Eco.Core.Utils.Result.FailLocStr("Ground changed after it was classified.");
                return Eco.Core.Utils.Result.Succeeded;
            }

            public void GameActionPackDispose()
            {
            }
        }
    }
}
