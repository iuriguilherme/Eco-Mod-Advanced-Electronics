namespace AdvancedElectronics.Navigation
{
    /// <summary>
    /// Which arm a drone carries. The HRVSTR chassis models both and the animator's
    /// mode-select branches on exactly one of them, so there is no third arm and no
    /// armless drone -- an enum rather than a pair of booleans, because "neither" and
    /// "both" are states the art cannot render and this shape cannot express.
    ///
    /// Moved here from the Eco-coupled assembly (KTD6/U14) so <see cref="DroneJobSelection"/>
    /// can key job-strategy selection on it without leaving the Eco-free assembly.
    /// </summary>
    public enum DroneTool
    {
        /// <summary>The mining arm.</summary>
        Mining,

        /// <summary>The harvest arm.</summary>
        Harvest,
    }

    /// <summary>Which job strategy a drone's declared tool selects (KTD3).</summary>
    public enum DroneJobKind
    {
        Survey,
        Mining
    }

    /// <summary>
    /// Pure mapping from a drone's declared <see cref="DroneTool"/> to the job kind it
    /// should run (KTD3, R10, R13) -- keyed on the tool the drone's class declares, never
    /// on which component happens to be attached. The present (pre-U14) dispatch is by
    /// component presence, which is why the harvest drone currently runs surveys: it
    /// inherited an ore sensor from the same chassis the survey drone uses. That
    /// component-presence dispatch is not this function's problem to fix -- the harvest
    /// drone's own tool value already maps to <see cref="DroneJobKind.Survey"/> here,
    /// which is what keeps its accepted (if accidental) survey behaviour unchanged; only
    /// the mining drone's distinct tool value newly resolves to <see cref="DroneJobKind.Mining"/>.
    /// </summary>
    public static class DroneJobSelection
    {
        /// <summary>The job kind for <paramref name="tool"/>, or null for a tool value this mapping does not recognise.</summary>
        public static DroneJobKind? SelectFor(DroneTool tool) => tool switch
        {
            DroneTool.Mining => DroneJobKind.Mining,
            DroneTool.Harvest => DroneJobKind.Survey,
            _ => null
        };
    }
}
