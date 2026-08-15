namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// A drone's externally-visible status (U8, R15; job-neutral since U14/KTD3, which
    /// put a second job kind -- mining -- behind the same travel machine). Idle/EnRoute/
    /// OnStation/Unreachable per the unit's state diagram -- deliberately just the label;
    /// all mutation happens through <see cref="DroneStateMachine"/>'s named transition
    /// methods, never by assigning this enum from outside.
    /// </summary>
    public enum DroneStatus
    {
        Idle,
        EnRoute,

        /// <summary>
        /// Arrived at the assigned area and parked there, doing whatever work the
        /// slotted drone's job strategy defines (surveying or mining) -- renamed from
        /// the survey-specific "Surveying" once mining became a second consumer of this
        /// same travel machine (KTD13). Behaviourally identical to the old value; only
        /// the name stopped being survey-only vocabulary.
        /// </summary>
        OnStation,
        Unreachable
    }

    /// <summary>
    /// What an EnRoute (or Unreachable-and-retrying) drone is currently travelling
    /// toward. Kept distinct from <see cref="DroneStatus"/> because "EnRoute" alone is
    /// ambiguous -- EnRoute to the assigned district and EnRoute back to the dock are
    /// different destinations with different arrival handlers on the Eco side (see
    /// DroneLifecycle.cs).
    /// </summary>
    public enum DroneTravelTarget
    {
        /// <summary>Not travelling anywhere (Idle).</summary>
        None,

        /// <summary>Travelling to <see cref="DroneStateMachine.TargetDistrictName"/>.</summary>
        District,

        /// <summary>Travelling back to the home dock.</summary>
        Dock
    }

    /// <summary>
    /// Pure status state machine for the survey drone (U8), covering R6 (district-
    /// scoped survey -- Idle when unassigned), R13 (immediate re-path on reassignment),
    /// and R15 (idle/en-route/surveying/unreachable status). Zero dependency on any
    /// Eco.* namespace (KTD2) so every transition is exercised directly by
    /// DroneStateMachineTests.cs without a running server.
    ///
    /// Modeled as an explicit state type (<see cref="DroneStatus"/>/
    /// <see cref="DroneTravelTarget"/>) mutated ONLY through named transition methods
    /// (OnDistrictAssigned / OnArrived / OnNoPathFound / OnDistrictCleared /
    /// OnReturnedToDock) -- not a raw enum with a public setter -- so a caller cannot
    /// jam the machine into a combination the diagram never allows (e.g. "Idle but
    /// travelling to a district", or "Surveying with no target district recorded").
    /// Every field mutation is private and only reachable by calling a method whose
    /// name states the real-world event that justifies it; the Eco-side DroneLifecycle
    /// (the only intended caller) reads events off DroneDock/DroneMoverComponent and
    /// forwards them 1:1 to these methods rather than poking Status directly.
    ///
    /// State diagram this implements (see the unit's Approach doc for the full
    /// annotated version):
    /// <code>
    /// [*] --> Idle: drone in dock, no district
    /// Idle --> EnRoute: district assigned
    /// EnRoute --> Surveying: reached district
    /// EnRoute --> Unreachable: no path found
    /// Surveying --> EnRoute: district reassigned (re-path)
    /// Surveying --> Idle: district cleared -> return to dock
    /// Unreachable --> EnRoute: new reachable district assigned
    /// Unreachable --> Idle: returned to dock
    /// </code>
    /// Plus two transitions the diagram's arrows don't spell out but the unit's
    /// requirements demand explicitly (see each method's doc below):
    /// (1) a no-path result on the RETURN leg (attempted from Surveying or while
    /// already Unreachable) also lands in Unreachable, not stuck; (2) a failed return
    /// attempt while already Unreachable stays Unreachable -- only an actual arrival
    /// (<see cref="OnReturnedToDock"/>) reaches Idle.
    /// </summary>
    public sealed class DroneStateMachine
    {
        /// <summary>Current status. Starts Idle (drone docked, no district assigned).</summary>
        public DroneStatus Status { get; private set; } = DroneStatus.Idle;

        /// <summary>
        /// What the drone is currently travelling toward. <see cref="DroneTravelTarget.None"/>
        /// while Idle; meaningful for EnRoute and (as "what to keep retrying")
        /// Unreachable.
        /// </summary>
        public DroneTravelTarget TravelTarget { get; private set; } = DroneTravelTarget.None;

        /// <summary>
        /// Name of the district currently assigned/being travelled to or surveyed, or
        /// null once cleared/returned. Mirrors DroneDock.AssignedDistrictName's
        /// name-not-object storage choice for the same reasons (see that property's
        /// doc comment) -- this pure library has no District type to reference anyway.
        /// </summary>
        public string TargetDistrictName { get; private set; }

        /// <summary>
        /// True only while Surveying. The Eco side gates its per-tick ore-sampling
        /// work on this (R6) -- an Idle, EnRoute, or Unreachable drone never samples.
        /// </summary>
        public bool ShouldSample => this.Status == DroneStatus.OnStation;

        /// <summary>
        /// A district has been assigned (fresh assignment from Idle/Unreachable, or a
        /// reassignment while EnRoute/Surveying -- R13). Always transitions to EnRoute
        /// targeting the new district and records its name, regardless of the
        /// PREVIOUS status: this is an authoritative external command (the player set
        /// <c>/drone district &lt;name&gt;</c>), so it always wins immediately rather
        /// than waiting for the drone to finish whatever it was doing. The Eco side is
        /// responsible for actually re-pathing from the drone's CURRENT position (not
        /// the dock) when this fires while already EnRoute/Surveying -- see
        /// DroneLifecycle.cs.
        /// </summary>
        public void OnDistrictAssigned(string districtName)
        {
            this.Status = DroneStatus.EnRoute;
            this.TravelTarget = DroneTravelTarget.District;
            this.TargetDistrictName = districtName;
        }

        /// <summary>
        /// The drone has physically reached its assigned district (Eco side checks
        /// this via DistrictAssignment.IsPositionInAssignedDistrict). Only meaningful
        /// while EnRoute to a district; a stray call in any other state/target is a
        /// no-op rather than corrupting state, since "arrived" only makes sense in
        /// that one context.
        /// </summary>
        public void OnArrived()
        {
            if (this.Status == DroneStatus.EnRoute && this.TravelTarget == DroneTravelTarget.District)
            {
                this.Status = DroneStatus.OnStation;
            }
        }

        /// <summary>
        /// The most recent SetDestination attempt found no path -- valid from EnRoute
        /// (to the district, or already retrying to the dock), from Surveying (the
        /// Eco side attempted an immediate return-to-dock leg straight from Surveying
        /// and it failed -- the extra transition this unit's spec calls out
        /// explicitly: Surveying can land in Unreachable directly, without passing
        /// through EnRoute), and from Unreachable itself (a retried return attempt
        /// failed again). In every case the result is Unreachable with the travel
        /// target set to Dock (the only thing left worth retrying). Returns true the
        /// FIRST time this call makes the drone Unreachable (signalling the Eco side
        /// should immediately attempt a return-to-dock leg -- AE9/R15), and false when
        /// the drone was already Unreachable (a repeated failure needs no fresh
        /// "go attempt a return" signal -- it's already the standing instruction; this
        /// is the second extra transition the spec calls out: a failed return attempt
        /// stays Unreachable rather than re-signalling or transitioning anywhere else).
        /// </summary>
        public bool OnNoPathFound()
        {
            var wasAlreadyUnreachable = this.Status == DroneStatus.Unreachable;
            this.Status = DroneStatus.Unreachable;
            this.TravelTarget = DroneTravelTarget.Dock;
            return !wasAlreadyUnreachable;
        }

        /// <summary>
        /// The dock's district assignment was cleared while the drone was actively
        /// pursuing one (Surveying, or still EnRoute to the district) AND the Eco side
        /// already confirmed a path home exists (SetDestination to the dock returned
        /// true) -- if it didn't, the Eco side calls <see cref="OnNoPathFound"/>
        /// instead (see that method's doc). Transitions to EnRoute targeting the dock;
        /// arrival is a SEPARATE later call to <see cref="OnReturnedToDock"/> (R6's
        /// "two-step" Surveying -> EnRoute -> Idle). A stray call while already
        /// Idle/EnRoute-to-dock/Unreachable is a no-op -- there is no district-in-
        /// progress to clear.
        /// </summary>
        public void OnDistrictCleared()
        {
            var pursuingDistrict =
                this.Status == DroneStatus.OnStation ||
                (this.Status == DroneStatus.EnRoute && this.TravelTarget == DroneTravelTarget.District);

            if (pursuingDistrict)
            {
                this.Status = DroneStatus.EnRoute;
                this.TravelTarget = DroneTravelTarget.Dock;
                this.TargetDistrictName = null;
            }
        }

        /// <summary>
        /// The drone has physically arrived back at the dock (Eco side checks this via
        /// ArrivalDetector against the dock's position). Valid from EnRoute-to-dock
        /// (the normal return) and from Unreachable (a retried return finally
        /// succeeded, or the operator manually walked/repathed it home) -- both land in
        /// Idle. A stray call while EnRoute-to-district or Surveying is a no-op: the
        /// drone cannot have arrived home while it is demonstrably elsewhere.
        /// </summary>
        public void OnReturnedToDock()
        {
            var canReturn =
                (this.Status == DroneStatus.EnRoute && this.TravelTarget == DroneTravelTarget.Dock) ||
                this.Status == DroneStatus.Unreachable;

            if (canReturn)
            {
                this.Status = DroneStatus.Idle;
                this.TravelTarget = DroneTravelTarget.None;
                this.TargetDistrictName = null;
            }
        }
    }
}
