namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// The names the drone's animation booleans travel under. Each string is used in
    /// THREE places that must agree exactly, and nothing checks them at compile time:
    /// <list type="number">
    /// <item><description>here, as the key passed to <c>WorldObject.SetAnimatedState</c>
    /// (see DroneLifecycle.RefreshAnimationStates);</description></item>
    /// <item><description><c>DroneAnimatorStates.BoolStateNames</c> on the Unity side,
    /// which stamps them into the prefab's <c>WorldObject.States</c> array;</description></item>
    /// <item><description>the parameter names in
    /// Assets/Art/AdvancedElectronics/Animators/HRVSTR_Animator_Controller.controller.</description></item>
    /// </list>
    /// A mismatch in any one of them is silent: the state syncs, the event fires, and the
    /// Animator parameter that was supposed to receive it simply never exists. Renaming
    /// means renaming in all three at once, which is why they are frozen constants rather
    /// than inline literals.
    ///
    /// These names come from the art. An earlier set was invented here instead
    /// (State_Docked, State_Flying, Drone_Mining and four more) and did not overlap the
    /// controller's parameters by a single string, so every push landed nowhere and the
    /// drone rendered and moved correctly while animating nothing at all.
    /// </summary>
    public static class DroneAnimationStateNames
    {
        public const string IsAtHomeDock = "IsAtHomeDock";
        public const string IsWorking    = "IsWorking";
        public const string ModeMining   = "ModeMining";
        public const string ModeHarvest  = "ModeHarvest";

        /// <summary>
        /// State names the ModKit's own WorldObject already registers, which a custom
        /// <c>States</c> entry must never repeat.
        ///
        /// WorldObject.cs ships three built-in state channels -- Enabled, Operating and
        /// Using -- each with its own enable/disable/changed events, and
        /// <c>RegisterCustomEvents</c> adds them to the same dictionary the custom States
        /// array feeds. A repeat throws <c>ArgumentException: An item with the same key
        /// has already been added</c> inside <c>OnCreateArchetype</c>, so the archetype is
        /// never created and the object is never instanced: it does not render, does not
        /// preview a placement ghost, and produces nothing in the server log, because the
        /// failure is entirely client-side.
        ///
        /// This cost a night. "Operating" was the animator's own parameter name and looked
        /// like the obvious choice on both sides.
        /// </summary>
        public static readonly string[] Reserved = { "Enabled", "Operating", "Using" };
    }

    /// <summary>
    /// The four animation booleans the drone's Animator consumes, derived from lifecycle
    /// status rather than stored. Pure and Eco-free (KTD2, the same reason
    /// <see cref="DroneStateMachine"/> is), so the whole animation contract is exercised by
    /// DroneAnimationStateTests without a running server -- which matters more here than
    /// usual, because the alternative way to find out that a mapping is wrong is to watch a
    /// drone in game and judge an animation by eye.
    ///
    /// Deliberately a projection, not a second state machine. Everything below is computed
    /// from <see cref="DroneStatus"/> and three booleans the caller already has; there is no
    /// transition logic and no memory. If these could drift from the real status, the drone
    /// would animate one thing while doing another.
    /// </summary>
    public readonly struct DroneAnimationState
    {
        /// <summary>
        /// The drone is physically home and settled. The controller's entry state is fully
        /// stopped, and this going false is what starts mode-select and take-off.
        ///
        /// Position, not assignment. An assignment outlives the survey that completes it --
        /// a drone that finishes its area and flies home is still assigned -- so a flag
        /// derived from "has an area" would go false on dispatch and never come back, and
        /// the drone would sit in its dock running the flying loop forever.
        /// </summary>
        public bool IsAtHomeDock { get; }

        /// <summary>
        /// On station within the assigned area and stopped there.
        ///
        /// False while the drone repositions between plots. Park-and-sweep alternates between
        /// hopping and standing still, and each hop is genuine travel: the arm stows, the
        /// drone flies, and work restarts on arrival.
        /// </summary>
        public bool IsWorking { get; }

        /// <summary>
        /// The drone carries the mining arm. Exactly one of this and
        /// <see cref="ModeHarvest"/> is true; the controller's mode-select has no third
        /// branch and no neither-branch.
        /// </summary>
        public bool ModeMining { get; }

        /// <summary>The drone carries the harvest arm. The exact negation of <see cref="ModeMining"/>.</summary>
        public bool ModeHarvest { get; }

        private DroneAnimationState(bool isAtHomeDock, bool isWorking,
                                    bool modeMining, bool modeHarvest)
        {
            this.IsAtHomeDock = isAtHomeDock;
            this.IsWorking    = isWorking;
            this.ModeMining   = modeMining;
            this.ModeHarvest  = modeHarvest;
        }

        /// <summary>
        /// Projects the current lifecycle situation onto the four booleans.
        /// </summary>
        /// <param name="status">The lifecycle status machine's current status.</param>
        /// <param name="isMoving">Whether the mover is currently advancing along a path.</param>
        /// <param name="isAtHomeDock">Whether the drone is physically within docking range of its dock.</param>
        /// <param name="usesHarvestTool">Whether this drone's class carries the harvest arm rather than the mining arm.</param>
        public static DroneAnimationState From(DroneStatus status, bool isMoving,
                                               bool isAtHomeDock, bool usesHarvestTool)
        {
            // On station AND stopped. Travel is travel, even the short hop from one plot to
            // the next inside the same area: the drone stows its arm, flies, and starts work
            // again on arrival. An earlier version held this true across hops to avoid a
            // stutter, which instead played the work loop over a drone visibly sliding
            // sideways -- the arm swinging at nothing it was near.
            var working = status == DroneStatus.OnStation && !isMoving;

            // Settled, not merely nearby: the status machine flips to Idle on arrival while
            // the mover can still be closing the last metre, and playing the fully-stopped
            // animation over a drone that is visibly drifting is the artifact this avoids.
            //
            // And not working: the caller's "at the dock" test is a radius wide enough to
            // clear the pad's diagonal, which also contains any survey plot next to the dock.
            // A drone sweeping that plot is physically home and busy at the same time, and
            // reporting both would stop the blades and replay the take-off on every plot hop.
            // Working wins, because it is the more specific claim about what the drone is
            // doing. The rule lives here rather than in the caller so it can be tested.
            var home = isAtHomeDock && !isMoving && !working;

            return new DroneAnimationState(
                isAtHomeDock: home,
                isWorking:    working,
                modeMining:   !usesHarvestTool,
                modeHarvest:  usesHarvestTool);
        }

        /// <summary>
        /// The state paired with the name it is pushed under, so callers iterate one list
        /// instead of writing four near-identical push lines that can disagree.
        /// </summary>
        public (string Name, bool Value)[] AsNamedValues() => new[]
        {
            (DroneAnimationStateNames.IsAtHomeDock, this.IsAtHomeDock),
            (DroneAnimationStateNames.IsWorking,    this.IsWorking),
            (DroneAnimationStateNames.ModeMining,   this.ModeMining),
            (DroneAnimationStateNames.ModeHarvest,  this.ModeHarvest),
        };
    }
}
