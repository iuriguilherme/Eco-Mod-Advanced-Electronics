using System.ComponentModel;
using Eco.Core.Items;
using Eco.Gameplay.Items;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// The mining drone's excavation tool (U4, R19, R20, KD8). Carries the same
    /// "Excavation" tag vanilla pickaxes declare on their <c>PickaxeItem</c> base class
    /// (confirmed during planning: <c>DigOrMine.ToolUsed</c> is configured with
    /// <c>RequiredTag: "Excavation"</c>, which is what makes a law editor's tool picker
    /// for excavation offer this item once tagged) -- enforcement equivalence with a
    /// citizen digging by hand (R20).
    ///
    /// A9's evaluation-time half, settled during planning by reading the picker's own
    /// match logic (<c>GamePickerList.DoEval</c>): a law's tool filter is a set of
    /// specific items the law's author selected, matched by exact membership, not by
    /// re-testing the tag at evaluation time. The tag only controls what the editor
    /// OFFERS to select -- it does not retroactively make this item match an existing
    /// law authored before this item existed. A law with no tool filter (an empty
    /// selection) matches unconditionally regardless, which is what the live pass's law
    /// test exercises. No fallback is needed: tagging is unconditionally safe here.
    ///
    /// Never crafted, held, or placed in an inventory -- the mining removal service
    /// (U5) builds its own game actions naming this item directly and never triggers a
    /// durability effect, so this carries no recipe, no durability, and no repairability.
    /// A plain <see cref="Item"/> rather than a <see cref="ToolItem"/>/<see cref="RepairableItem"/>
    /// for exactly that reason -- there is no tool-use interaction or durability concept
    /// to opt into. <c>GameActionExtensions.Fill</c>'s tool parameter is typed
    /// <see cref="Item"/>, not any narrower tool type, so a plain item satisfies it.
    /// </summary>
    /// <remarks>
    /// Test scenarios: none reachable as unit tests -- registration and tag membership
    /// are engine-side declarations with no logic to test. Live: the arm appears where a
    /// law editor offers excavation tools; a removal record names it as the tool used;
    /// whether a law written against excavation tools before this deploy already matches
    /// it is recorded during the live pass (A9).
    /// </remarks>
    [Serialized]
    // NOT Category("Hidden"), despite this item never being held or crafted. "Hidden" is the
    // engine's own switch for keeping a thing out of the civics UI -- Item.Hidden is derived
    // from it (Item.cs), ItemInfoManager syncs that flag to the client, and GameActionManager's
    // NoLawsAttribute is literally Category("Hidden") with the comment "we make sure to
    // automatically hide all actions that we don't want to be tracked by laws".
    //
    // So the tag below was doing its job and the category was cancelling it: the live pass found
    // the Mining Arm absent from a dig-or-mine law's tool picker. R20 wants the opposite -- a
    // settlement must be able to name this tool in a law. The cost is that an unobtainable item
    // shows up in item listings; the benefit is that server owners can regulate the drone.
    [Category("Tool")]
    [Tag("Excavation")]
    [LocDisplayName("Mining Arm")]
    [LocDescription("The mining drone's excavation tool. Never held or crafted.")]
    public class MiningArmItem : Item
    {
    }
}
