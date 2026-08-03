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
    /// Assets/Art/AdvancedElectronics/HRVSTR_Animator_Controller.controller.</description></item>
    /// </list>
    /// A mismatch in any one of them is silent: the state syncs, the event fires, and the
    /// Animator parameter that was supposed to receive it simply never exists. Renaming
    /// means renaming in all three at once, which is why they are frozen constants rather
    /// than inline literals.
    ///
    /// The names come from the art, not from this code -- the controller was authored
    /// first and these mirror it.
    /// </summary>
    public static class DroneAnimationStateNames
    {
        public const string Docked     = "State_Docked";
        public const string Unassigned = "State_Unassigned";
        public const string Assigned   = "State_Assigned";
        public const string Flying     = "State_Flying";
        public const string Working    = "State_Working";
        public const string Mining     = "Drone_Mining";
        public const string Harvesting = "Drone_Harvest";
    }

    /// <summary>
    /// The seven animation booleans the drone's Animator consumes, derived from lifecycle
    /// status rather than stored. Pure and Eco-free (KTD2, the same reason
    /// <see cref="DroneStateMachine"/> is), so the whole animation contract is exercised by
    /// DroneAnimationStateTests without a running server -- which matters more here than
    /// usual, because the alternative way to find out that a mapping is wrong is to watch a
    /// drone in game and judge an animation by eye.
    ///
    /// Deliberately a projection, not a second state machine. Everything below is computed
    /// from <see cref="DroneStatus"/>, <see cref="DroneTravelTarget"/>, and two booleans the
    /// caller already has; there is no transition logic and no memory. If these could drift
    /// from the real status, the drone would animate one thing while doing another.
    /// </summary>
    public readonly struct DroneAnimationState
    {
        /// <summary>Parked at the dock with nothing to do -- Idle and not moving.</summary>
        public bool Docked { get; }

        /// <summary>No survey area assigned. Always the exact negation of <see cref="Assigned"/>.</summary>
        public bool Unassigned { get; }

        /// <summary>A survey area is assigned, whether or not the drone has reached it.</summary>
        public bool Assigned { get; }

        /// <summary>
        /// Moving under its own power. Carries the same fact as the pre-existing "MoveSpeed"
        /// float the mover pushes; the HRVSTR controller declares no float parameters, so the
        /// bool is the shape the art can actually consume.
        /// </summary>
        public bool Flying { get; }

        /// <summary>
        /// On station in the assigned area. Narrower than
        /// <see cref="DroneLifecycle.IsWorking"/>, which also counts the outbound leg because
        /// it is deciding what to charge fuel for; the art means "deployed and doing the job",
        /// and a drone still flying out is flying, not working.
        /// </summary>
        public bool Working { get; }

        /// <summary>
        /// Actively sampling: on station AND stationary. The drone's park-and-sweep pass
        /// alternates between hopping to the next plot and standing still while it reads the
        /// columns beneath it, so this goes false during each hop and true again on arrival.
        /// </summary>
        public bool Mining { get; }

        /// <summary>
        /// Reserved. The harvest drone surveys today -- it carries an OreSensorComponent and no
        /// harvesting behaviour exists on the server -- so nothing can truthfully drive this yet
        /// and it stays false. Wire it here when harvest behaviour lands rather than faking it
        /// from survey state, which would play a harvest animation over a drone that is scanning.
        /// </summary>
        public bool Harvesting { get; }

        private DroneAnimationState(bool docked, bool unassigned, bool assigned,
                                    bool flying, bool working, bool mining, bool harvesting)
        {
            this.Docked     = docked;
            this.Unassigned = unassigned;
            this.Assigned   = assigned;
            this.Flying     = flying;
            this.Working    = working;
            this.Mining     = mining;
            this.Harvesting = harvesting;
        }

        /// <summary>
        /// Projects the current lifecycle situation onto the seven booleans.
        /// </summary>
        /// <param name="status">The lifecycle status machine's current status.</param>
        /// <param name="travelTarget">What an EnRoute drone is travelling toward.</param>
        /// <param name="isMoving">Whether the mover is currently advancing along a path.</param>
        /// <param name="hasAssignment">Whether the home dock currently has an assigned area.</param>
        public static DroneAnimationState From(DroneStatus status, DroneTravelTarget travelTarget,
                                               bool isMoving, bool hasAssignment)
        {
            var onStation = status == DroneStatus.Surveying;

            return new DroneAnimationState(
                docked:     status == DroneStatus.Idle && !isMoving,
                unassigned: !hasAssignment,
                assigned:   hasAssignment,
                flying:     isMoving,
                working:    onStation,
                mining:     onStation && !isMoving,
                harvesting: false);
        }

        /// <summary>
        /// The state paired with the name it is pushed under, so callers iterate one list
        /// instead of writing seven near-identical push lines that can disagree.
        /// </summary>
        public (string Name, bool Value)[] AsNamedValues() => new[]
        {
            (DroneAnimationStateNames.Docked,     this.Docked),
            (DroneAnimationStateNames.Unassigned, this.Unassigned),
            (DroneAnimationStateNames.Assigned,   this.Assigned),
            (DroneAnimationStateNames.Flying,     this.Flying),
            (DroneAnimationStateNames.Working,    this.Working),
            (DroneAnimationStateNames.Mining,     this.Mining),
            (DroneAnimationStateNames.Harvesting, this.Harvesting),
        };
    }
}
