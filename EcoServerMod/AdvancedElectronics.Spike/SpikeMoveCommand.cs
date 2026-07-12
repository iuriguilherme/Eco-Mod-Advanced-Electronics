using System;
using System.Linq;
using System.Reflection;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Gameplay.Systems.Messaging.Chat.Commands;
using System.Numerics;
using Eco.Shared.IoC;
using Quaternion = Eco.Shared.Math.Quaternion;

namespace AdvancedElectronics.Spike
{
    /// <summary>
    /// Q2 probe: does the server-moved WorldObject path render acceptably on the client?
    /// Spawns a vanilla WorldObject (no bespoke client movement components) and moves it
    /// along a circle every server tick via Position/Rotation + SyncPositionAndRotation().
    /// This answers only the MOTION-SMOOTHNESS half of Q2; locomotion-animation state
    /// hooks need a custom prefab and are out of this probe's reach by design.
    /// </summary>
    [ChatCommandHandler]
    public static class SpikeMoveCommand
    {
        private static SpikeMover active;

        [ChatCommand("Feasibility probes for the survey drone spike.", ChatAuthorizationLevel.Admin)]
        public static void Spike(User user) { }

        /// <param name="speed">Degrees of the circle advanced per tick (default 2 = slow walk; try 20 for fast).</param>
        /// <param name="objectType">Short type name of the vanilla WorldObject to spawn (default CampfireObject — plain prefab, no movement components).</param>
        [ChatSubCommand("Spike", "Q2: spawn a vanilla object and move it in a circle each tick.", ChatAuthorizationLevel.Admin)]
        public static void Move(User user, float speed = 2f, string objectType = "CampfireObject")
        {
            if (active != null && !active.IsAlive)
                active = null; // mover self-unregistered (object destroyed externally)
            if (active != null)
            {
                user.MsgLocStr("Spike mover already running; use /spike stop first.");
                return;
            }

            var type = FindWorldObjectType(objectType);
            if (type == null)
            {
                user.MsgLocStr($"No WorldObject type named '{objectType}' found in loaded assemblies.");
                return;
            }

            var center = user.Position + new Vector3(4f, 0.5f, 0f);
            WorldObject obj;
            try
            {
                obj = WorldObjectManager.ForceAdd(type, user, center, Quaternion.Identity, false);
            }
            catch (Exception e)
            {
                user.MsgLocStr($"Q2 evidence: ForceAdd({type.Name}) threw {e.GetType().Name}: {e.Message}");
                return;
            }

            if (obj == null)
            {
                user.MsgLocStr($"Q2 evidence: ForceAdd({type.Name}) returned null (placement rejected?).");
                return;
            }

            active = new SpikeMover(obj, center, radius: 4f, degreesPerTick: speed, reporter: user);
            ServiceHolder<IWorldObjectManager>.Obj.AddToTick(active);
            user.MsgLocStr($"Q2 probe started: {type.Name} circling at {speed} deg/tick. Watch smoothness; /spike stop to end.");
        }

        [ChatSubCommand("Spike", "Q2: stop and despawn the moving object.", ChatAuthorizationLevel.Admin)]
        public static void Stop(User user)
        {
            if (active == null)
            {
                user.MsgLocStr("No spike mover running.");
                return;
            }
            active.Stop();
            active = null;
            user.MsgLocStr("Q2 probe stopped; object destroyed.");
        }

        private static Type FindWorldObjectType(string shortName) =>
            AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); } })
                .FirstOrDefault(t => t != null
                    && t.Name.Equals(shortName, StringComparison.OrdinalIgnoreCase)
                    && typeof(WorldObject).IsAssignableFrom(t)
                    && !t.IsAbstract);
    }

    /// <summary>
    /// Tick-affine mover: registered with WorldObjectManager.Obj.AddToTick so updates run
    /// on the server's object tick, never a plain .NET timer thread (thread-affinity
    /// requirement recorded in the spike plan).
    /// </summary>
    internal sealed class SpikeMover : ITickOnDemand
    {
        // Self-termination window so a forgotten probe (or a disconnected admin)
        // cannot leave the mover ticking indefinitely; /spike stop remains the
        // primary teardown. Mirrors SpikeAnimalWatcher's expiry pattern.
        private const double MaxRunSeconds = 600.0;

        private readonly WorldObject obj;
        private readonly Vector3 center;
        private readonly float radius;
        private readonly float degreesPerTick;
        private readonly User reporter;
        private readonly double endAt;
        private float angle;
        private bool stopped;
        private double lastReport;

        public SpikeMover(WorldObject obj, Vector3 center, float radius, float degreesPerTick, User reporter)
        {
            this.obj = obj;
            this.center = center;
            this.radius = radius;
            this.degreesPerTick = degreesPerTick;
            this.reporter = reporter;
            this.endAt = SpikeUtil.NowSeconds() + MaxRunSeconds;
        }

        // Tick as often as the manager allows.
        public double NextTickTime => 0d;

        internal bool IsAlive => !this.stopped && !this.obj.IsDestroyed;

        public bool TickOnDemand()
        {
            if (this.stopped || this.obj.IsDestroyed) return false; // false = unregister

            var now = SpikeUtil.NowSeconds();
            if (now > this.endAt)
            {
                this.reporter?.MsgLocStr("[Q2] probe timed out after 10 minutes; object destroyed.");
                this.Stop();
                return false;
            }

            this.angle += this.degreesPerTick;
            var rad = this.angle * (float)(Math.PI / 180.0);
            var sin = (float)Math.Sin(rad);
            var cos = (float)Math.Cos(rad);
            var pos = this.center + new Vector3(cos * this.radius, 0f, sin * this.radius);
            this.obj.Position = pos;
            this.obj.Rotation = Quaternion.LookRotation(new Vector3(-sin, 0f, cos));
            this.obj.SyncPositionAndRotation();

            if (now - this.lastReport >= 1.0)
            {
                this.lastReport = now;
                this.reporter?.MsgLocStr($"[Q2 trace] pos={SpikeUtil.Fmt(pos)} angle={this.angle % 360f:F0}");
            }
            return true;
        }

        public void Stop()
        {
            this.stopped = true;
            if (!this.obj.IsDestroyed) WorldObjectManager.DestroyPermanently(this.obj);
        }
    }
}
