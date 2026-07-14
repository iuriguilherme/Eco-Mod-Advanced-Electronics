using TMPro;
using UnityEngine;

/// <summary>
/// Client-side-only presentation glue for the DroneDock prefab's readout (U9,
/// step 7 of docs/guides/2026-07-survey-drone-unity-prefab-guide.md). Renders
/// the server-synced StringStates/FloatStates values (wired via the
/// WorldObject component's OnStringStateChanged/OnFloatStateChanged UnityEvent
/// arrays in the Inspector) into a single TextMeshPro block.
///
/// Not related to EcoServerMod/AdvancedElectronics/DockReadout.cs (the
/// server-side pure formatting class of a similar name) -- this script never
/// runs on the server and has no Eco dependency; it only displays text the
/// server already formatted and pushed via WorldObject.SetAnimatedState.
/// </summary>
public class DockReadoutDisplay : MonoBehaviour
{
    [SerializeField] private TMP_Text readoutText;

    // Index 0 = status line, 1-6 = the six ore lines (ReadoutOre0..ReadoutOre5),
    // matching the order the WorldObject component's StringStates array was set
    // up in (see the guide's step 6). Empty string = "no line" (an ore slot with
    // nothing sampled yet gets padded empty server-side -- see
    // DroneDock.RefreshReadout in EcoServerMod).
    private readonly string[] lines = new string[7];
    private float coveragePercent;

    public void SetStatusLine(string value) => SetLineAndRefresh(0, value);
    public void SetOreLine0(string value) => SetLineAndRefresh(1, value);
    public void SetOreLine1(string value) => SetLineAndRefresh(2, value);
    public void SetOreLine2(string value) => SetLineAndRefresh(3, value);
    public void SetOreLine3(string value) => SetLineAndRefresh(4, value);
    public void SetOreLine4(string value) => SetLineAndRefresh(5, value);
    public void SetOreLine5(string value) => SetLineAndRefresh(6, value);

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

        var body = string.Empty;
        foreach (var line in this.lines)
        {
            if (string.IsNullOrEmpty(line))
                continue;
            body += line + "\n";
        }

        this.readoutText.text = $"{body}Coverage: {this.coveragePercent:F0}%";
    }
}
