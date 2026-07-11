using System;
using System.Linq;
using System.Numerics;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Gameplay.Systems.Messaging.Chat.Commands;
using Eco.Shared.IoC;
using Eco.Shared.Items;
using Eco.Simulation;
using Eco.Simulation.Agents;
using Eco.Simulation.Types;

namespace AdvancedElectronics.Spike
{
    /// <summary>
    /// Q1 probe: how much animal lifecycle does Eco require before its pathfinding
    /// will navigate to a target? Escalation rungs per the spike plan:
    ///
    ///   Rung (a) — no lifecycle: RESOLVED AT COMPILE TIME. Eco.Simulation.Agents.Animal
    ///   is abstract, its navigation members (GetPathTo, RequestPathAndUpdateState) are
    ///   instance methods, and no standalone/static pathfinder type exists in the public
    ///   API surface (verified against Eco.ReferenceAssemblies 0.13.0.4). Pathfinding
    ///   without an Animal instance is not reachable; the rung's evidence is this file
    ///   compiling only via rung (b)'s spawned-instance path.
    ///
    ///   Rung (b) — spawned vanilla animal, externally commanded: implemented below.
    ///   Spawn success is instrumented SEPARATELY from pathing success so a spawn-harness
    ///   failure cannot masquerade as a pathfinding verdict.
    ///
    /// Obstacle sub-check: run /spike path with a player-built wall between the animal
    /// and the target; the position trace shows whether the path routes around it.
    /// </summary>
    [ChatCommandHandler]
    public static class SpikePathCommand
    {
        private static SpikeAnimalWatcher watcher;

        /// <param name="speciesName">Species to spawn (default Hare).</param>
        /// <param name="distance">How far in front of the player to aim the path target (default 15 blocks).</param>
        [ChatSubCommand("Spike", "Q1: spawn a vanilla animal and command a path to a target; reports spawn and pathing outcomes separately.", ChatAuthorizationLevel.Admin)]
        public static void Path(User user, string speciesName = "Hare", float distance = 15f)
        {
            user.MsgLocStr("[Q1 rung a] Compile-time evidence: Animal is abstract, navigation is instance-only, no standalone pathfinder exists in 0.13.0.4 — lifecycle-free pathfinding is unreachable.");

            // --- Spawn instrumentation (separate from pathing) ---
            Animal animal;
            try
            {
                var species = EcoSim.GetSpecies(speciesName) as AnimalSpecies;
                if (species == null)
                {
                    user.MsgLocStr($"[Q1 spawn] FAIL: species '{speciesName}' not found or not an AnimalSpecies. Try /spike path Tortoise");
                    return;
                }

                var spawnPos = user.Position + new Vector3(3f, 0f, 0f);
                animal = EcoSim.AnimalSim.SpawnAnimal(species, spawnPos, 0, null);
                if (animal == null)
                {
                    user.MsgLocStr("[Q1 spawn] FAIL: SpawnAnimal returned null (ecosystem preconditions? population cap?). This is a harness failure, NOT a pathfinding verdict.");
                    return;
                }
                user.MsgLocStr($"[Q1 spawn] OK: {speciesName} spawned at {Fmt(animal.Position)}.");
            }
            catch (Exception e)
            {
                user.MsgLocStr($"[Q1 spawn] FAIL: {e.GetType().Name}: {e.Message}. Harness failure, NOT a pathfinding verdict.");
                return;
            }

            // --- Pathing instrumentation ---
            var target = user.Position + new Vector3(distance, 0f, 0f);
            try
            {
                var dir = Vector3.Normalize(target - animal.Position);
                animal.GetPathTo("Wander", 0, animal.Position, dir, target, (PathfindFlags)0);
                user.MsgLocStr($"[Q1 path] GetPathTo accepted toward {Fmt(target)} — watch the animal; position trace follows for 60s.");
            }
            catch (Exception e)
            {
                user.MsgLocStr($"[Q1 path] GetPathTo threw {e.GetType().Name}: {e.Message} — exception text is the rung-(b) evidence.");
            }

            watcher?.Stop();
            watcher = new SpikeAnimalWatcher(animal, target, user);
            ServiceHolder<IWorldObjectManager>.Obj.AddToTick(watcher);
        }

        private static string Fmt(Vector3 v) => $"{v.X:F1},{v.Y:F1},{v.Z:F1}";
    }

    /// <summary>Reports the probe animal's position once per second for 60s so the chat
    /// log records whether it actually walks the commanded path (and around obstacles).</summary>
    internal sealed class SpikeAnimalWatcher : ITickOnDemand
    {
        private readonly Animal animal;
        private readonly Vector3 target;
        private readonly User reporter;
        private readonly double endAt;
        private double lastReport;
        private bool stopped;

        public SpikeAnimalWatcher(Animal animal, Vector3 target, User reporter)
        {
            this.animal = animal;
            this.target = target;
            this.reporter = reporter;
            this.endAt = Now() + 60.0;
        }

        public double NextTickTime => 0d;

        private static double Now() => Environment.TickCount64 / 1000.0;

        public bool TickOnDemand()
        {
            if (this.stopped || Now() > this.endAt) return false;
            if (Now() - this.lastReport >= 1.0)
            {
                this.lastReport = Now();
                var d = Vector3.Distance(this.animal.Position, this.target);
                this.reporter?.MsgLocStr($"[Q1 trace] animal={this.animal.Position.X:F1},{this.animal.Position.Y:F1},{this.animal.Position.Z:F1} dist-to-target={d:F1}");
            }
            return true;
        }

        public void Stop() => this.stopped = true;
    }
}
