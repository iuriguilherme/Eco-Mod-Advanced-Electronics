using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Client-side-only glue that drives a drone's Animator from the
/// server-synced boolean WorldObject states. The server never touches an
/// Animator; it pushes named booleans with <c>SetAnimatedState</c> and this
/// component is the only thing that turns those into
/// <c>Animator.SetBool</c> calls.
///
/// SELF-WIRING -- no Inspector interaction required, exactly like
/// <see cref="DockReadoutDisplay"/>, which this deliberately mirrors:
/// <list type="bullet">
/// <item><description><see cref="Reset"/> (Editor-only, runs once when the
/// component is first added) sets <c>WorldObject.States</c> to the exact names
/// in <see cref="BoolStateNames"/> and resizes the paired event arrays, so
/// nobody has to type five strings into the custom WorldObject
/// Inspector.</description></item>
/// <item><description><see cref="Awake"/> (runtime, every time the prefab is
/// instantiated) finds the sibling <c>WorldObject</c> and the Animator, then
/// calls <c>AddListener</c> on each <c>OnStateChangedEvents</c> slot in code.
/// A runtime AddListener is invoked identically to a serialized/persistent one;
/// the only difference is where the wiring is declared (here, not the
/// Inspector).</description></item>
/// </list>
///
/// WHY BOOLS AND NOT THE EXISTING MoveSpeed FLOAT. The drone's
/// DroneMoverComponent pushes a float state named "MoveSpeed"
/// (EcoServerMod/AdvancedElectronics/DroneMoverComponent.cs). The HRVSTR
/// controller declares no float parameters at all -- all five of its
/// parameters are booleans -- so MoveSpeed has no consumer on this controller,
/// and <c>Operating</c> carries "away from the dock" in the shape the art
/// actually asks for. MoveSpeed is left pushed and unread rather than removed:
/// it costs one synced write per movement transition and a future controller
/// may want a real speed blend.
///
/// ASSUMPTION, shared with <see cref="DockReadoutDisplay"/> and unverified
/// until a live server/client pair runs this: that
/// <c>WorldObject.OnStateChangedEvents[i]</c> fires when <c>States[i]</c>'s
/// named value changes server-side, invoking the event with the new value. That
/// is the ModKit's evident index-pairing convention (see
/// Assets/EcoModKit/Scripts/Editor/WorldObjectEditor.cs, which draws States[i]
/// and its event slots side by side under one foldout), but the runtime
/// dispatch lives in engine code this repo does not ship source for.
/// </summary>
[RequireComponent(typeof(WorldObject))]
public class DroneAnimatorStates : MonoBehaviour
{
    /// <summary>
    /// The boolean state names this component bridges, in the order they occupy
    /// <c>WorldObject.States</c>. Each one is BOTH the server-side
    /// <c>SetAnimatedState</c> key and the Animator parameter name -- they are
    /// deliberately identical strings so there is no translation table to keep
    /// in sync, and renaming one means renaming the server constant, this array,
    /// and the parameter in HRVSTR_Animator_Controller together.
    ///
    /// Taken from the controller's own parameter list, which is authoritative
    /// for the art side; the server constants in
    /// EcoServerMod/AdvancedElectronics.Navigation/DroneAnimationState.cs are
    /// authoritative for the push side and must match this array exactly.
    ///
    /// They did not, for the whole of this component's first life: the server
    /// pushed seven names of its own invention (State_Docked, State_Flying,
    /// Drone_Mining and four more) that the controller had never heard of. A
    /// pushed name no consumer matches is dropped rather than reported, so the
    /// drone rendered, moved, and animated nothing at all, with a clean build and
    /// a clean log on both sides.
    /// </summary>
    private static readonly string[] BoolStateNames =
    {
        "IsAtHomeDock",
        "IsWorking",
        "ModeMining",
        "ModeHarvest",
        "Operating",
    };

    private Animator animator;

    /// <summary>
    /// Parameter name hashes, index-paired with <see cref="BoolStateNames"/>.
    /// Resolved once in Awake because Animator.SetBool(string) hashes the string
    /// on every call.
    /// </summary>
    private int[] parameterHashes;

    /// <summary>
    /// Which indices of <see cref="BoolStateNames"/> the attached controller
    /// actually declares. Setting a parameter an Animator does not have is not an
    /// exception -- it logs a warning every single call, which at drone-transition
    /// frequency is a scrolling console. A controller is allowed to consume a
    /// subset of the states the server pushes.
    /// </summary>
    private bool[] parameterExists;

#if UNITY_EDITOR
    // Editor-only: runs once when this component is first added to a GameObject
    // (or via right-click > Reset on the component).
    private void Reset()
    {
        EnsureStateArrays(this.GetComponent<WorldObject>());
    }
#endif

    /// <summary>
    /// Force-sets <paramref name="worldObject"/>'s bool <c>States</c> array (and
    /// resizes the paired event arrays to match) to the names this component
    /// bridges. Idempotent -- safe to call every time a prefab is built or
    /// rebuilt, which is how AdvancedElectronicsBuildTools.cs uses it, not just
    /// from <see cref="Reset"/>.
    /// </summary>
    public static void EnsureStateArrays(WorldObject worldObject)
    {
        if (worldObject == null) return;

        worldObject.States = (string[])BoolStateNames.Clone();

        worldObject.OnStateChangedEvents = Resize(worldObject.OnStateChangedEvents);
        worldObject.OnStateEnabledEvents = Resize(worldObject.OnStateEnabledEvents);
        worldObject.OnStateDisabledEvents = Resize(worldObject.OnStateDisabledEvents);
    }

    /// <summary>
    /// Grows or shrinks an event array to one slot per state name, filling new
    /// slots with fresh instances. Unity serializes a null UnityEvent slot as an
    /// empty one, but a null in a live array throws on AddListener, so every slot
    /// is populated rather than left null.
    /// </summary>
    private static T[] Resize<T>(T[] existing) where T : new()
    {
        if (existing != null && existing.Length == BoolStateNames.Length)
        {
            for (var i = 0; i < existing.Length; i++)
                if (existing[i] == null) existing[i] = new T();
            return existing;
        }

        var resized = new T[BoolStateNames.Length];
        for (var i = 0; i < resized.Length; i++)
            resized[i] = existing != null && i < existing.Length && existing[i] != null
                ? existing[i]
                : new T();
        return resized;
    }

    private void Awake()
    {
        // GetComponentInChildren rather than GetComponent: the FBX import puts the
        // Animator on the model's own root, which becomes a child if the prefab
        // root is a separate GameObject carrying the WorldObject. Searching
        // children covers both layouts.
        this.animator = this.GetComponentInChildren<Animator>(true);

        var worldObject = this.GetComponent<WorldObject>();
        EnsureStateArrays(worldObject); // defensive: heals a prefab built before this existed

        this.parameterHashes = new int[BoolStateNames.Length];
        this.parameterExists = new bool[BoolStateNames.Length];

        var declared = new HashSet<string>();
        if (this.animator != null)
            foreach (var parameter in this.animator.parameters)
                if (parameter.type == AnimatorControllerParameterType.Bool)
                    declared.Add(parameter.name);

        for (var i = 0; i < BoolStateNames.Length; i++)
        {
            this.parameterHashes[i] = Animator.StringToHash(BoolStateNames[i]);
            this.parameterExists[i] = declared.Contains(BoolStateNames[i]);

            // Copy the loop variable: the listener closure outlives this iteration
            // and a captured `i` would read BoolStateNames.Length on every call.
            var index = i;
            worldObject.OnStateChangedEvents[i].AddListener(value => this.SetState(index, value));
        }

        if (this.animator == null)
            Debug.LogWarning($"[AdvancedElectronics] '{this.name}' has a DroneAnimatorStates but no Animator anywhere under it -- every server state push will be dropped. Attach the HRVSTR_Animator_Controller.");
    }

    /// <summary>
    /// Applies one state change to the Animator. Public and index-free overloads
    /// are deliberately not offered: the index-to-name pairing is the whole
    /// contract and exposing a name-based setter would invite a caller to push a
    /// name the WorldObject never declared, which would sync nothing.
    /// </summary>
    private void SetState(int index, bool value)
    {
        if (this.animator == null || !this.parameterExists[index]) return;
        this.animator.SetBool(this.parameterHashes[index], value);
    }
}
