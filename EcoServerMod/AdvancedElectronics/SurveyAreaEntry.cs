using System.Collections.Generic;
using System.Linq;
using AdvancedElectronics.Navigation;
using Eco.Shared.Serialization;

namespace Eco.Mods.TechTree
{
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

        /// <summary>Drawn plots, flattened as consecutive (x, z) pairs. Even length by construction.</summary>
        [Serialized] public List<int> PlotCoords { get; set; } = new List<int>();

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

        /// <summary>Replaces the stored plots with <paramref name="plots"/>, flattening to (x, z) pairs.</summary>
        public void SetPlots(IEnumerable<PlotCoord> plots)
        {
            this.PlotCoords = new List<int>();
            foreach (var p in plots)
            {
                this.PlotCoords.Add(p.X);
                this.PlotCoords.Add(p.Z);
            }
        }

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
