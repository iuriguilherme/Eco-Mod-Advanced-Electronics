using System;
using System.Linq;
using Eco.Core.Plugins.Interfaces;
using Eco.Core.Utils;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Gameplay.Settlements;
using Eco.Shared.IoC;
using Eco.Shared.Logging;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// Registers the Advanced Electronics mod with the Eco server so its WorldObjects,
    /// items, and recipes (<see cref="DroneDockObject"/>, <see cref="SurveyDroneItem"/>) load.
    ///
    /// Mirrors the feasibility spike's registration pattern
    /// (EcoServerMod/AdvancedElectronics.Spike/ModRegistration.cs) but this is a clean
    /// sibling project, not a subclass of the spike (KTD1 -- the spike stays a
    /// reference, not a base class). The spike's `/spike` chat commands are throwaway
    /// diagnostics and are deliberately not carried over here.
    ///
    /// <para>
    /// It also carries the mod's one load-time entry point (U4). Do NOT follow the spike for
    /// that half: the spike has no <c>Initialize</c> at all, so it does not show the part that
    /// matters.
    /// </para>
    /// </summary>
    public class AdvancedElectronicsMod : IModInit
    {
        public static ModRegistration Register() => new()
        {
            ModName = "AdvancedElectronics",
            ModDescription = "Advanced Electronics mod loaded",
            ModDisplayName = "Advanced Electronics",
        };

        public string GetCategory() => "Mods";

        public string GetStatus() => "Advanced Electronics mod loaded";

        /// <summary>
        /// The mod's load-time entry point: once per world load, after the world objects it has
        /// to walk are genuinely initialized (U4, R13, R16, KTD8).
        ///
        /// <para>
        /// <b>Not a plugin.</b> A prioritised plugin is the obvious answer and it is the wrong
        /// one — priority orders plugins against each other, and says nothing about whether
        /// world objects have been initialized, which is the only thing this work actually
        /// needs. Eco's own two save migrations reject that route in writing for exactly this
        /// shape, and both use the chain below:
        /// <c>Server/Mods/__core__/Migrations/TruckStorageToFlatbedMigration.cs</c> and
        /// <c>UpgradeModuleMigration.cs</c> in the 0.14.1.1 source checkout, each carrying
        /// <i>"Not a plugin. The serializer's migration hooks run before users load, so hang off
        /// IModInit and wait for the user list and world objects."</i> The settlement gate is
        /// the third link for the reason the truck migration gives: objects only become fully
        /// initialized then.
        /// </para>
        /// <para>
        /// <b>Not a tab refresh either.</b> The 0.3.0 mined-stamp fold is invoked lazily from
        /// <c>MiningComponent.RefreshAll()</c>, which is correct for a fold that has to resolve
        /// another dock's area and may need retrying. It cannot serve R16, which requires
        /// once-per-load: no lazy path can promise a dock is ever visited, and reconciliation
        /// that only runs when somebody opens a tab is reconciliation that leaves an illegal
        /// claim working the ground until they do.
        /// </para>
        /// <para>
        /// Reflection finds this by name (<c>Eco.ModKit/ModDataSync.InitMods</c> calls
        /// <c>GetMethod("Initialize")</c>), so the signature is fixed by
        /// <see cref="IModInit"/> and the name must not change.
        /// </para>
        /// </summary>
        public static void Initialize() =>
            UserManager.Initializer.RunIfOrWhenInitialized(() =>
            WorldObjectManager.Init.RunIfOrWhenInitialized(() =>
            SettlementCommon.Initializer.RunIfOrWhenInitialized(MigrateAndReconcileOnce)));

        /// <summary>
        /// Sequences the once-per-load work: fold each dock's legacy farms, then reconcile the
        /// claims that folding has finally made judgeable.
        ///
        /// <para>
        /// <b>It sequences; it does not decide.</b> Every question of what a fold produces or
        /// which assignment is illegal is answered in the Eco-free navigation assembly and, for
        /// the fold, tested there. Order is the one thing this method asserts, and it is not
        /// arbitrary: an unfolded farm is invisible to the claim test, so reconciling before the
        /// fold would judge a world in which the farms do not exist and leave every
        /// mining-versus-farming conflict standing.
        /// </para>
        /// <para>
        /// <b>Every dock is contained.</b> Moving this work off a tab refresh changes the blast
        /// radius of a failure, and that is the whole reason for the try/catch. On a tab refresh
        /// a dock that throws costs one broken refresh and a player who can walk to the dock and
        /// change something. Here an uncaught exception costs the world load — nobody reaches
        /// any dock, R16 guarantees the same throw on the next attempt, and the save is
        /// effectively bricked by a mod update. So a dock that throws keeps its own legacy rows
        /// untouched (<see cref="DroneDockObject.MigrateLegacyFarmAreas"/> writes nothing before
        /// it is sure), is logged with enough to find it in the world, and the walk carries on
        /// with the rest.
        /// </para>
        /// <para>
        /// <b>Seam.</b> No unit tests, and none are coming: this holds Eco types and the test
        /// project references only <c>AdvancedElectronics.Navigation</c>. There is also no
        /// decision here to test — its effects are covered by the fold's own tests in that
        /// assembly and, for the sequencing and the containment, by the batched live session
        /// (docs/solutions/workflow-issues/eco-mod-batched-live-testing.md): the server starts, a
        /// pre-fold save's farms arrive as areas, and a dock rigged to throw does not stop the
        /// others.
        /// </para>
        /// </summary>
        private static void MigrateAndReconcileOnce()
        {
            // Materialised before anything is written. The walk is over the live world-object
            // collection and the fold writes to the docks inside it; enumerating lazily while
            // mutating their contents is a hazard this method gains nothing by taking.
            var docks = ServiceHolder<IWorldObjectManager>.Obj.All
                .OfType<DroneDockObject>()
                .Where(d => !d.IsDestroyed)
                .ToList();

            foreach (var dock in docks)
            {
                try
                {
                    dock.MigrateLegacyFarmAreas();
                }
                catch (Exception e)
                {
                    // ONE interpolated string, not concatenated pieces: WriteErrorLineLoc takes a
                    // FormattableString, and `$"a" + $"b"` collapses to a plain string that does
                    // not convert to one.
                    Log.WriteErrorLineLoc(
                        $"Advanced Electronics: folding the legacy farm areas of '{dock.Name}' at {dock.Position3i} failed ({e.Message}). That dock keeps its legacy farm rows and the rest of the world carries on, but its farms hold no ground until the fold succeeds or the farms are drawn again.\n{e}");
                }
            }

            // ---------------------------------------------------------------
            // U5 SEAM -- reconciliation is called from here, and from nowhere else.
            //
            // It belongs at this point and no earlier: every dock above has now folded, so the
            // claim test can finally see a farm, and an assignment holding ground it is not
            // entitled to is judgeable for the first time (R13). U5 owns both halves -- the
            // decision, in AdvancedElectronics.Navigation/AreaReconciliation.cs, and the effects
            // (unassign, record the blocked reason, recall the drone) in DroneDock.Migration.cs.
            //
            // When it lands, the call goes here, wrapped the way the fold above is: reconciliation
            // as a whole is contained, so a throw inside it costs the reconciliation and not the
            // world load.
            // ---------------------------------------------------------------
        }
    }
}
