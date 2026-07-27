using TMPro;
using UnityEngine;

/// <summary>
/// Client-side-only presentation glue for the DroneDock prefab's readout (U9).
/// Renders the server-synced StringStates/FloatStates values into a single
/// TextMeshPro block.
///
/// SELF-WIRING -- no Inspector interaction required. Add this component to the
/// same GameObject as the ModKit's <c>WorldObject</c> component and it does
/// everything else itself:
/// <list type="bullet">
/// <item><description><see cref="Reset"/> (Editor-only, runs once when the
/// component is first added) sets <c>WorldObject.StringStates</c> /
/// <c>FloatStates</c> to the exact 7 + 1 slot names
/// <see cref="DroneDockStateNames"/> holds -- no need to click "Add" in the
/// custom WorldObject Inspector or type the names in by hand.</description></item>
/// <item><description><see cref="Awake"/> (runtime, every time the prefab is
/// instantiated) finds the sibling <c>WorldObject</c> and calls
/// <c>AddListener</c> on each <c>OnStringStateChanged</c>/
/// <c>OnFloatStateChanged</c> slot in code -- no dragging a "Handler" entry in
/// the Inspector's persistent-listener UI. A runtime <c>AddListener</c> is
/// invoked identically to a serialized/persistent one; the only difference is
/// where the wiring is declared (here, not the Inspector).</description></item>
/// <item><description>The TMP text target is found automatically via
/// <c>GetComponentInChildren&lt;TMP_Text&gt;()</c> -- no field to drag a
/// reference into. Add exactly one <c>TMP_Text</c> (or
/// <c>TextMeshProUGUI</c>, which derives from it) somewhere under this
/// GameObject and it will be found.</description></item>
/// </list>
///
/// Not related to EcoServerMod/AdvancedElectronics/DockReadout.cs (the
/// server-side pure formatting class of a similar name) -- this script never
/// runs on the server and has no Eco dependency; it only displays text the
/// server already formatted and pushed via WorldObject.SetAnimatedState.
///
/// ASSUMPTION -- verify once a live server/client pair is available: that
/// WorldObject.OnStringStateChanged[i] actually fires when StringStates[i]'s
/// named value changes server-side, invoking the event with the new string as
/// its argument. This is the ModKit's evident index-pairing convention (see
/// Assets/EcoModKit/Scripts/Editor/WorldObjectEditor.cs's "String State
/// Events"/"Float State Events" sections, which draw StringStates[i] and
/// OnStringStateChanged[i] side by side under one foldout) but the exact
/// runtime dispatch is in engine code this repo doesn't ship source for.
/// </summary>
[RequireComponent(typeof(WorldObject))]
public class DockReadoutDisplay : MonoBehaviour
{
    /// <summary>
    /// The exact StringStates order this display expects, index-paired with
    /// EcoServerMod/AdvancedElectronics/DroneDock.cs's RefreshReadout(): index 0
    /// is the status line (server constant <c>StatusStateName = "ReadoutStatus"</c>),
    /// indices 1-6 are the six ore lines (server constant
    /// <c>OreLineStateNamePrefix = "ReadoutOre"</c> + 0..5, from
    /// DockReadout.MaxOreLines). If the server-side constants change, update
    /// this array to match -- the server source is authoritative.
    /// </summary>
    private static readonly string[] StringStateNames =
    {
        "ReadoutStatus",
        "ReadoutOre0", "ReadoutOre1", "ReadoutOre2",
        "ReadoutOre3", "ReadoutOre4", "ReadoutOre5",
    };

    /// <summary>Server constant <c>CoverageStateName = "ReadoutCoverage"</c>.</summary>
    private static readonly string[] FloatStateNames = { "ReadoutCoverage" };

    private readonly string[] lines = new string[7];
    private float coveragePercent;
    private TMP_Text readoutText;

#if UNITY_EDITOR
    // Editor-only: runs once when this component is first added to a
    // GameObject (or via right-click > Reset on the component). Populates the
    // sibling WorldObject's state-name arrays so nobody has to type them into
    // the Inspector by hand.
    private void Reset()
    {
        EnsureStateArrays(this.GetComponent<WorldObject>());
    }
#endif

    /// <summary>
    /// Force-sets <paramref name="worldObject"/>'s StringStates/FloatStates (and
    /// resizes OnStringStateChanged/OnFloatStateChanged to match) to this
    /// display's expected slot names. Idempotent -- safe to call every time a
    /// prefab is built/rebuilt (see AdvancedElectronicsBuildTools.cs), not just
    /// from <see cref="Reset"/>.
    /// </summary>
    public static void EnsureStateArrays(WorldObject worldObject)
    {
        if (worldObject == null) return;

        worldObject.StringStates = (string[])StringStateNames.Clone();
        if (worldObject.OnStringStateChanged == null || worldObject.OnStringStateChanged.Length != StringStateNames.Length)
        {
            worldObject.OnStringStateChanged = new ChangedStringStateEvent[StringStateNames.Length];
            for (var i = 0; i < worldObject.OnStringStateChanged.Length; i++)
                worldObject.OnStringStateChanged[i] = new ChangedStringStateEvent();
        }

        worldObject.FloatStates = (string[])FloatStateNames.Clone();
        if (worldObject.OnFloatStateChanged == null || worldObject.OnFloatStateChanged.Length != FloatStateNames.Length)
        {
            worldObject.OnFloatStateChanged = new ChangedFloatStateEvent[FloatStateNames.Length];
            for (var i = 0; i < worldObject.OnFloatStateChanged.Length; i++)
                worldObject.OnFloatStateChanged[i] = new ChangedFloatStateEvent();
        }
    }

    private void Awake()
    {
        this.readoutText = this.GetComponentInChildren<TMP_Text>(true);

        var worldObject = this.GetComponent<WorldObject>();
        EnsureStateArrays(worldObject); // defensive: heals a prefab built before this method existed

        worldObject.OnStringStateChanged[0].AddListener(this.SetStatusLine);
        worldObject.OnStringStateChanged[1].AddListener(this.SetOreLine0);
        worldObject.OnStringStateChanged[2].AddListener(this.SetOreLine1);
        worldObject.OnStringStateChanged[3].AddListener(this.SetOreLine2);
        worldObject.OnStringStateChanged[4].AddListener(this.SetOreLine3);
        worldObject.OnStringStateChanged[5].AddListener(this.SetOreLine4);
        worldObject.OnStringStateChanged[6].AddListener(this.SetOreLine5);
        worldObject.OnFloatStateChanged[0].AddListener(this.SetCoverage);
    }

    public void SetStatusLine(string value) => this.SetLineAndRefresh(0, value);
    public void SetOreLine0(string value) => this.SetLineAndRefresh(1, value);
    public void SetOreLine1(string value) => this.SetLineAndRefresh(2, value);
    public void SetOreLine2(string value) => this.SetLineAndRefresh(3, value);
    public void SetOreLine3(string value) => this.SetLineAndRefresh(4, value);
    public void SetOreLine4(string value) => this.SetLineAndRefresh(5, value);
    public void SetOreLine5(string value) => this.SetLineAndRefresh(6, value);

    public void SetCoverage(float value)
    {
        this.coveragePercent = value;
        this.Refresh();
    }

    private void SetLineAndRefresh(int index, string value)
    {
        this.lines[index] = value;
        this.Refresh();
    }

    private void Refresh()
    {
        if (this.readoutText == null)
            return;

        // The world-space text is reserved for the short drone status line only.
        // lines[0] is the status (server StatusStateName = "ReadoutStatus"). The
        // detailed per-ore survey + coverage moved to the dock's info-window tooltip
        // (server DroneDockObject.SurveyReadoutTooltip), so they are intentionally not
        // rendered here even though the ore/coverage listeners still update their fields.
        this.readoutText.text = this.lines[0] ?? string.Empty;
    }
}
