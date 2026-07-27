using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Threading.Tasks;
using Eco.Core.Controller;
using Eco.Gameplay.Civics.GameValues;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Networking;
using Eco.Shared.SharedTypes;
using Eco.Shared.Serialization;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// The dock's "Areas" tab: manage survey areas and choose which one the drone works on. Reading
    /// findings lives on the sibling <see cref="SurveyResultsComponent"/> tab.
    ///
    /// Members are declared in the order they render, because with only stacked full-width elements
    /// available declaration order IS reading order:
    ///
    ///   1. the map manager button -- FIRST so it never moves; the text below it changes length as
    ///      areas are added, surveyed and renamed, and an anchor that drifts is one you have to hunt for
    ///   2. the assignment line, the drone's status, then the numbered area list
    ///   3. one assign button per existing area (position-labelled; the list supplies the names)
    ///
    /// Assign buttons come after the list because their labels are static position numbers and are
    /// meaningless until the list that names them has been read.
    /// </summary>
    [Serialized, CreateComponentTabLoc("Areas", true), HasIcon]
    public class SurveyAreasComponent : WorldObjectComponent
    {
        private const int MaxAreaPlots = 40; // v1 tier cap (R1b); drone-tier-owned later.

        /// <summary>
        /// Size of the compile-time assign-button pool. RPCs are methods, so the ceiling is however
        /// many are declared -- there is no runtime cost to a larger pool, and how far the player is
        /// willing to scroll is their call. Areas past it are assigned with /drone assignarea.
        /// </summary>
        public const int AssignButtonPool = 20;

        public override WorldObjectComponentClientAvailability Availability =>
            WorldObjectComponentClientAvailability.UI;

        // ---------------------------------------------------------------
        // 1. Manage -- the map is the area manager. Declared FIRST so it holds a fixed position
        //    regardless of how long the text below it grows.
        // ---------------------------------------------------------------

        [RPC(AccessType.ConsumerAccess), Autogen, UITypeName("BigButton"), Description("Manage Areas on Map")]
        public async Task ManageAreasOnMap(Player player)
        {
            if (this.Parent is not DroneDockObject dock) return;
            await SurveyAreaPicker.ManageAreas(player, dock, MaxAreaPlots);
            this.RefreshAll();
        }

        // ---------------------------------------------------------------
        // 2. Areas -- assignment line, drone status, numbered list
        // ---------------------------------------------------------------

        /// <summary>The assignment line, the drone's status, then the dock's numbered area list.</summary>
        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string AreasDisplay { get; private set; } = string.Empty;

        // ---------------------------------------------------------------
        // 2. Assign -- one button per existing area
        //
        // RPCs are declared at compile time, so N buttons cannot be generated; a fixed pool is gated
        // per position by a [SyncToView] bool, which the client re-evaluates when RefreshAreas pushes
        // Changed() for it. Same shape as the vanilla AreaBonusComponent (a WorldObjectComponent
        // combining [SyncToView] bool + VisibilityParam on a BigButton RPC).
        //
        // Clicking the button of the already-assigned area unassigns, so there is no Unassign button.
        // ---------------------------------------------------------------

        [SyncToView] public bool AreaExists1() => this.AreaCount() >= 1;
        [SyncToView] public bool AreaExists2() => this.AreaCount() >= 2;
        [SyncToView] public bool AreaExists3() => this.AreaCount() >= 3;
        [SyncToView] public bool AreaExists4() => this.AreaCount() >= 4;
        [SyncToView] public bool AreaExists5() => this.AreaCount() >= 5;
        [SyncToView] public bool AreaExists6() => this.AreaCount() >= 6;
        [SyncToView] public bool AreaExists7() => this.AreaCount() >= 7;
        [SyncToView] public bool AreaExists8() => this.AreaCount() >= 8;
        [SyncToView] public bool AreaExists9() => this.AreaCount() >= 9;
        [SyncToView] public bool AreaExists10() => this.AreaCount() >= 10;
        [SyncToView] public bool AreaExists11() => this.AreaCount() >= 11;
        [SyncToView] public bool AreaExists12() => this.AreaCount() >= 12;
        [SyncToView] public bool AreaExists13() => this.AreaCount() >= 13;
        [SyncToView] public bool AreaExists14() => this.AreaCount() >= 14;
        [SyncToView] public bool AreaExists15() => this.AreaCount() >= 15;
        [SyncToView] public bool AreaExists16() => this.AreaCount() >= 16;
        [SyncToView] public bool AreaExists17() => this.AreaCount() >= 17;
        [SyncToView] public bool AreaExists18() => this.AreaCount() >= 18;
        [SyncToView] public bool AreaExists19() => this.AreaCount() >= 19;
        [SyncToView] public bool AreaExists20() => this.AreaCount() >= 20;

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists1)), UITypeName("BigButton"), Description("Assign Area 1")]
        public void AssignArea1(Player player) => this.ToggleAssign(1);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists2)), UITypeName("BigButton"), Description("Assign Area 2")]
        public void AssignArea2(Player player) => this.ToggleAssign(2);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists3)), UITypeName("BigButton"), Description("Assign Area 3")]
        public void AssignArea3(Player player) => this.ToggleAssign(3);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists4)), UITypeName("BigButton"), Description("Assign Area 4")]
        public void AssignArea4(Player player) => this.ToggleAssign(4);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists5)), UITypeName("BigButton"), Description("Assign Area 5")]
        public void AssignArea5(Player player) => this.ToggleAssign(5);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists6)), UITypeName("BigButton"), Description("Assign Area 6")]
        public void AssignArea6(Player player) => this.ToggleAssign(6);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists7)), UITypeName("BigButton"), Description("Assign Area 7")]
        public void AssignArea7(Player player) => this.ToggleAssign(7);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists8)), UITypeName("BigButton"), Description("Assign Area 8")]
        public void AssignArea8(Player player) => this.ToggleAssign(8);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists9)), UITypeName("BigButton"), Description("Assign Area 9")]
        public void AssignArea9(Player player) => this.ToggleAssign(9);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists10)), UITypeName("BigButton"), Description("Assign Area 10")]
        public void AssignArea10(Player player) => this.ToggleAssign(10);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists11)), UITypeName("BigButton"), Description("Assign Area 11")]
        public void AssignArea11(Player player) => this.ToggleAssign(11);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists12)), UITypeName("BigButton"), Description("Assign Area 12")]
        public void AssignArea12(Player player) => this.ToggleAssign(12);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists13)), UITypeName("BigButton"), Description("Assign Area 13")]
        public void AssignArea13(Player player) => this.ToggleAssign(13);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists14)), UITypeName("BigButton"), Description("Assign Area 14")]
        public void AssignArea14(Player player) => this.ToggleAssign(14);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists15)), UITypeName("BigButton"), Description("Assign Area 15")]
        public void AssignArea15(Player player) => this.ToggleAssign(15);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists16)), UITypeName("BigButton"), Description("Assign Area 16")]
        public void AssignArea16(Player player) => this.ToggleAssign(16);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists17)), UITypeName("BigButton"), Description("Assign Area 17")]
        public void AssignArea17(Player player) => this.ToggleAssign(17);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists18)), UITypeName("BigButton"), Description("Assign Area 18")]
        public void AssignArea18(Player player) => this.ToggleAssign(18);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists19)), UITypeName("BigButton"), Description("Assign Area 19")]
        public void AssignArea19(Player player) => this.ToggleAssign(19);

        [RPC(AccessType.ConsumerAccess), Autogen, VisibilityParam(nameof(AreaExists20)), UITypeName("BigButton"), Description("Assign Area 20")]
        public void AssignArea20(Player player) => this.ToggleAssign(20);

        // --- Assignment ---

        private int AreaCount() => this.Parent is DroneDockObject dock ? dock.SurveyAreas.Count : 0;

        /// <summary>
        /// Assigns the area at 1-based <paramref name="position"/>, or unassigns when it is already the
        /// assigned one -- so the same button both starts and stops work on that area.
        /// </summary>
        private void ToggleAssign(int position)
        {
            if (this.Parent is not DroneDockObject dock) return;
            if (position < 1 || position > dock.SurveyAreas.Count) return;

            var area = dock.SurveyAreas[position - 1];
            dock.AssignSurveyArea(dock.AssignedSurveyAreaId == area.Id ? 0 : area.Id);
            this.RefreshAll();
        }

        // --- Refresh ---

        /// <summary>
        /// Refreshes this tab and the sibling Results tab, which shows the assigned marker and the
        /// viewed area's findings and so must follow assignment and area changes made here.
        /// </summary>
        public void RefreshAll()
        {
            this.RefreshAreas();

            if (this.Parent != null && this.Parent.TryGetComponent<SurveyResultsComponent>(out var results))
                results.RefreshAll();
        }

        public void RefreshAreas()
        {
            this.AreasDisplay = this.BuildAreasText();
            this.Changed(nameof(this.AreasDisplay));

            // Without this push the client never re-evaluates button visibility, so a newly created
            // area gains no button and a deleted one keeps its own until the dock is reopened.
            this.Changed(nameof(this.AreaExists1));
            this.Changed(nameof(this.AreaExists2));
            this.Changed(nameof(this.AreaExists3));
            this.Changed(nameof(this.AreaExists4));
            this.Changed(nameof(this.AreaExists5));
            this.Changed(nameof(this.AreaExists6));
            this.Changed(nameof(this.AreaExists7));
            this.Changed(nameof(this.AreaExists8));
            this.Changed(nameof(this.AreaExists9));
            this.Changed(nameof(this.AreaExists10));
            this.Changed(nameof(this.AreaExists11));
            this.Changed(nameof(this.AreaExists12));
            this.Changed(nameof(this.AreaExists13));
            this.Changed(nameof(this.AreaExists14));
            this.Changed(nameof(this.AreaExists15));
            this.Changed(nameof(this.AreaExists16));
            this.Changed(nameof(this.AreaExists17));
            this.Changed(nameof(this.AreaExists18));
            this.Changed(nameof(this.AreaExists19));
            this.Changed(nameof(this.AreaExists20));
        }

        // --- Text ---

        private string BuildAreasText()
        {
            if (this.Parent is not DroneDockObject dock)
                return string.Empty;

            var sb = new StringBuilder();
            sb.Append(this.BuildAssignmentLine(dock)).Append('\n');
            sb.Append(BuildDroneStatusLine(dock)).Append("\n\n");

            if (dock.SurveyAreas.Count == 0)
            {
                sb.Append("No survey areas yet. Use Manage Areas on Map to draw your first one.");
                return sb.ToString();
            }

            var position = 1;
            foreach (var area in dock.SurveyAreas)
            {
                var assigned = area.Id == dock.AssignedSurveyAreaId ? "   [assigned]" : string.Empty;
                sb.Append(position++).Append(". ").Append(area.Name)
                  .Append(" -- ").Append(area.PlotCount).Append(" plots, ")
                  .Append(FormatAreaSummary(area, dock))
                  .Append(assigned).Append('\n');
            }

            if (dock.SurveyAreas.Count > AssignButtonPool)
                sb.Append("\nAreas past ").Append(AssignButtonPool)
                  .Append(" have no button -- assign them with /drone assignarea <id>.\n");

            return sb.ToString();
        }

        /// <summary>
        /// The authoritative "what is the drone working on" line. Three states, not two: assignment
        /// does not require a drone to exist, so without the no-drone variant the tab would report
        /// success while nothing happens in the world.
        /// </summary>
        private string BuildAssignmentLine(DroneDockObject dock)
        {
            var area = dock.AssignedSurveyArea;
            if (area == null)
                return "Assigned: none -- pick an area below to start surveying.";

            var position = dock.SurveyAreas.IndexOf(area) + 1;
            var drone = dock.SpawnedDrone;
            var hasDrone = drone != null && !drone.IsDestroyed;

            return hasDrone
                ? $"Assigned: {position} -- {area.Name}"
                : $"Assigned: {position} -- {area.Name} (no drone -- build and dock one to start surveying)";
        }

        /// <summary>
        /// What the drone is DOING, as its own line under the assignment. Lives on this tab rather
        /// than with the findings: it belongs with assignment, which is what this tab controls.
        /// </summary>
        private static string BuildDroneStatusLine(DroneDockObject dock)
        {
            var drone = dock.SpawnedDrone;
            if (drone == null || drone.IsDestroyed)
                return "Drone: none docked.";

            return drone.TryGetComponent<DroneLifecycle>(out var lifecycle)
                ? $"Drone: {lifecycle.Status}"
                : "Drone: docked.";
        }

        /// <summary>
        /// Compact "coverage%, top find" summary for an area's list line, from its snapshot. Honours the
        /// dock's material filter so the highlighted find is the one the player is targeting.
        /// </summary>
        private static string FormatAreaSummary(SurveyAreaEntry area, DroneDockObject dock)
        {
            var top = area.ReadFindings()
                .Where(f => f.Found && dock.IsMaterialShown(f.OreType))
                .OrderByDescending(f => f.Count)
                .FirstOrDefault();

            if (top.Found)
                return $"{area.CoveragePercent:F0}% surveyed, most {top.OreType} (~{top.Count} blocks)";
            if (area.CoveragePercent > 0f)
                return $"{area.CoveragePercent:F0}% surveyed, nothing matching";
            return "not surveyed yet";
        }

    }
}
