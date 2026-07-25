using System.Linq;
using System.Text;
using Eco.Core.Controller;
using Eco.Gameplay.Objects;
using Eco.Shared.IoC;
using Eco.Shared.Serialization;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// The dock's "Survey Results" tab (U9, R7/R7a/R3a): the drone's findings rendered where
    /// the player acts on them — in the dock window, off chat. Read-only text via the settled
    /// StringDisplay pattern (a settable string, assigned + Changed-notified; see
    /// <see cref="SurveyAreasComponent"/> and
    /// docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md).
    ///
    /// Reads the drone's EXISTING <see cref="OreSensorComponent"/> results (the same
    /// DensestCell/SampledOreTypes surface <c>/drone survey</c> uses) and composes them as one
    /// text block — additive, so the old density model and chat readout keep working until the
    /// end-of-plan cleanup migrates the sensor onto <c>SurveyRecord</c> (U3) and retires them.
    /// A throttled <see cref="Tick"/> refresh keeps an open tab live as the drone roams.
    /// </summary>
    [Serialized, CreateComponentTabLoc("Survey Results", true), HasIcon]
    public class SurveyResultsComponent : WorldObjectComponent
    {
        private const float RefreshIntervalSeconds = 1f;
        private const float FallbackTickDeltaSeconds = 0.05f;
        private float secondsSinceRefresh;

        public override WorldObjectComponentClientAvailability Availability =>
            WorldObjectComponentClientAvailability.UI;

        [SyncToView, Autogen, UITypeName("StringDisplay")]
        public string ResultsDisplay { get; private set; } = "No survey yet.";

        public override void Initialize()
        {
            base.Initialize();
            this.RefreshResults();
        }

        public override void Tick()
        {
            base.Tick();

            var manager = ServiceHolder<IWorldObjectManager>.Obj;
            var deltaTime = manager != null && manager.TickDeltaTime > 0f
                ? manager.TickDeltaTime
                : FallbackTickDeltaSeconds;

            this.secondsSinceRefresh += deltaTime;
            if (this.secondsSinceRefresh < RefreshIntervalSeconds)
                return;

            this.secondsSinceRefresh = 0f;
            this.RefreshResults();
        }

        private void RefreshResults()
        {
            this.ResultsDisplay = this.BuildResultsText();
            this.Changed(nameof(this.ResultsDisplay));
        }

        private string BuildResultsText()
        {
            if (this.Parent is not DroneDockObject dock)
                return string.Empty;

            var sb = new StringBuilder();
            var area = dock.AssignedSurveyArea;
            sb.Append("Assigned area: ").Append(area?.Name ?? "(none)").Append('\n');

            var drone = dock.SpawnedDrone;
            if (drone == null || drone.IsDestroyed || !drone.TryGetComponent<OreSensorComponent>(out var sensor))
            {
                sb.Append("No drone is out surveying. Insert a Survey Drone and assign an area.");
                return sb.ToString();
            }

            if (drone.TryGetComponent<DroneLifecycle>(out var lifecycle))
                sb.Append("Drone: ").Append(lifecycle.Status).Append('\n');

            var results = sensor.SampledOreTypes
                .Select(oreType => (OreType: oreType, Result: sensor.DensestCell(oreType)))
                .Where(entry => entry.Result.Found)
                .OrderByDescending(entry => entry.Result.Ratio)
                .ToList();

            if (results.Count == 0)
            {
                sb.Append("Nothing found yet. The drone reports as it roams -- give it time to cover ground.");
                return sb.ToString();
            }

            foreach (var entry in results)
                sb.Append(DockReadout.FormatOreLine(entry.OreType, entry.Result)).Append('\n');

            var coverage = DockReadout.ComputeCoveragePercent(results.Select(e => (e.OreType, e.Result)).ToList());
            sb.Append("Coverage: ").Append(coverage.ToString("F0")).Append('%');
            return sb.ToString();
        }
    }
}
