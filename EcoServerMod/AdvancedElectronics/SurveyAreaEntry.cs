using System.Collections.Generic;
using System.Linq;
using AdvancedElectronics.Navigation;
using Eco.Core.Utils;
using Eco.Shared.Serialization;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// One serialized per-ore survey finding, stored on the area that produced it: the
    /// dig target block, its depth, and the plot concentration (R5/R7). The persisted
    /// mirror of the Eco-free <see cref="SurveyFinding"/> — a plain <c>[Serialized]</c>
    /// class (parameterless ctor + settable props) so it survives a restart alongside the
    /// area, unlike the in-memory <see cref="SurveyRecord"/> it is derived from.
    /// </summary>
    [Serialized]
    public class OreFindingSnapshot
    {
        [Serialized] public string OreType { get; set; }
        [Serialized] public int X { get; set; }
        [Serialized] public int Y { get; set; }
        [Serialized] public int Z { get; set; }
        [Serialized] public int DepthBelowSurface { get; set; }
        [Serialized] public float Concentration { get; set; }

        public OreFindingSnapshot() { }

        public static OreFindingSnapshot From(SurveyFinding f) => new OreFindingSnapshot
        {
            OreType = f.OreType,
            X = f.Position.X,
            Y = f.Position.Y,
            Z = f.Position.Z,
            DepthBelowSurface = f.DepthBelowSurface,
            Concentration = f.Concentration,
        };

        /// <summary>Back to the Eco-free finding shape the readout formatter consumes.</summary>
        public SurveyFinding ToSurveyFinding(int areaId) =>
            SurveyFinding.Create(areaId, this.OreType, new BlockPos(this.X, this.Y, this.Z), this.DepthBelowSurface, this.Concentration);
    }

    /// <summary>
    /// The Eco-side serialized record of one dock-owned survey area (U4, R1a/R2a/R3):
    /// a dock-local id, a player-facing name, and the drawn plots. Owned by the
    /// <see cref="DroneDockObject"/> that created it (KTD9) — there is no mod-wide
    /// registry — so it persists exactly because the dock does, and is discarded with the
    /// dock.
    ///
    /// Plots are stored as a FLATTENED <see cref="PlotCoords"/> list (x0, z0, x1, z1, ...)
    /// of plain ints rather than a list of a coordinate struct: Eco's <c>Vector2i</c> is
    /// not <c>[Serialized]</c> and nothing in the game source serializes a list of it, so
    /// per the U4 plan the plot set is stored in a form whose serializability is not in
    /// question. <see cref="ToSurveyArea"/> projects this into the Eco-free
    /// <see cref="SurveyArea"/> (U2) for membership tests and the plot cap.
    /// </summary>
    [Serialized]
    public class SurveyAreaEntry
    {
        /// <summary>Dock-local id, assigned by the owning dock. Stable across renames; identifies the area for assignment.</summary>
        [Serialized] public int Id { get; set; }

        /// <summary>Player-facing name. Not unique — two areas may share a name and stay distinct by <see cref="Id"/>.</summary>
        [Serialized] public string Name { get; set; }

        /// <summary>
        /// Drawn plots, flattened as consecutive (x, z) pairs. Even length by construction.
        /// A <see cref="ThreadSafeList{T}"/>, not a plain <c>List</c>: Eco's serializer rejects a
        /// non-immutable <c>[Serialized]</c> member ("Attempting to serialize non-immutable
        /// member ... Either make immutable or add [ThreadSafe]") and fails server init.
        /// </summary>
        [Serialized] public ThreadSafeList<int> PlotCoords { get; set; } = new();

        /// <summary>
        /// This area's survey findings, persisted with the area (KTD11 design change): available
        /// until the area is deleted or edited. Reassigning the drone away and back does NOT clear
        /// them — they belong to the area, not the drone or the dock's current assignment. Cleared
        /// by <see cref="SetPlots"/> (an edit redraws the geometry, so it is effectively a new area)
        /// and by the owning dock on delete.
        /// </summary>
        [Serialized] public ThreadSafeList<OreFindingSnapshot> Findings { get; set; } = new();

        /// <summary>Fraction of this area surveyed, 0-100 (R7a). Persisted with the findings.</summary>
        [Serialized] public float CoveragePercent { get; set; }

        /// <summary>Parameterless constructor required by the Eco serializer.</summary>
        public SurveyAreaEntry() { }

        public SurveyAreaEntry(int id, string name, IEnumerable<PlotCoord> plots)
        {
            this.Id = id;
            this.Name = name;
            this.SetPlots(plots);
        }

        /// <summary>Number of plots this area covers (the value R1b's tier cap is checked against).</summary>
        public int PlotCount => this.PlotCoords.Count / 2;

        /// <summary>
        /// Replaces the stored plots with <paramref name="plots"/>, flattening to (x, z) pairs.
        /// Also clears any findings: a redraw changes the area's geometry, so the old survey no
        /// longer describes it — the drone re-surveys the new shape from scratch (KTD11).
        /// </summary>
        public void SetPlots(IEnumerable<PlotCoord> plots)
        {
            this.PlotCoords = new ThreadSafeList<int>();
            foreach (var p in plots)
            {
                this.PlotCoords.Add(p.X);
                this.PlotCoords.Add(p.Z);
            }
            this.ClearFindings();
        }

        /// <summary>Replaces this area's persisted findings from a fresh survey pass.</summary>
        public void SetFindings(IEnumerable<SurveyFinding> findings, float coveragePercent)
        {
            var snapshot = new ThreadSafeList<OreFindingSnapshot>();
            foreach (var f in findings.Where(f => f.Found))
                snapshot.Add(OreFindingSnapshot.From(f));
            this.Findings = snapshot;
            this.CoveragePercent = coveragePercent;
        }

        /// <summary>Discards this area's findings (delete, or an edit that redraws the geometry).</summary>
        public void ClearFindings()
        {
            this.Findings = new ThreadSafeList<OreFindingSnapshot>();
            this.CoveragePercent = 0f;
        }

        /// <summary>The persisted findings back in the Eco-free shape the readout formatter consumes.</summary>
        public IEnumerable<SurveyFinding> ReadFindings() =>
            this.Findings.Select(s => s.ToSurveyFinding(this.Id));

        /// <summary>The stored plots as <see cref="PlotCoord"/>s (unflattening the pairs).</summary>
        public IEnumerable<PlotCoord> Plots()
        {
            for (var i = 0; i + 1 < this.PlotCoords.Count; i += 2)
                yield return new PlotCoord(this.PlotCoords[i], this.PlotCoords[i + 1]);
        }

        /// <summary>Projects this entry into the Eco-free <see cref="SurveyArea"/> for membership and cap logic (U2).</summary>
        public SurveyArea ToSurveyArea() => new SurveyArea(this.Id, this.Name, this.Plots());
    }
}
