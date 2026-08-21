namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// Which arm a drone carries. The HRVSTR chassis models both and the animator's
    /// mode-select branches on exactly one of them, so there is no third arm and no
    /// armless drone -- an enum rather than a pair of booleans, because "neither" and
    /// "both" are states the art cannot render and this shape cannot express.
    ///
    /// Presentation only. Job selection used to be derived from this value, which made the arm
    /// load-bearing and forced the survey drone to declare an arm it did not want; a drone now
    /// declares its job separately (IDroneToolbearer.Job).
    /// </summary>
    public enum DroneTool
    {
        /// <summary>The mining arm.</summary>
        Mining,

        /// <summary>The harvest arm.</summary>
        Harvest,
    }

    /// <summary>Which job a drone runs (KTD3). Declared per drone class, not derived from its arm.</summary>
    public enum DroneJobKind
    {
        Survey,
        Mining
    }
}
