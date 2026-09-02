using System.ComponentModel;
using Eco.Core.Items;
using Eco.Gameplay.Items;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// The Farm Drone's farming tool (U5, R3, R4). One item named by all three farming
    /// packs -- plow, sow, and harvest -- so a settlement regulates the whole of the
    /// drone's farming through one entry in a law's tool picker rather than three.
    ///
    /// The three tags are the ones Eco's own action definitions filter their tool pickers
    /// by, read from the engine rather than recalled: <c>PlowField</c> carries
    /// <c>ChangeParentConfigLoc(BlockAddRemove.ToolUsed, tag: "Plow")</c>,
    /// <c>PlantSeeds.ToolUsed</c> is declared <c>RequiredTag("Planter")</c>, and
    /// <c>HarvestOrHunt</c> carries <c>ChangeParentConfigLoc(ToolInteractAction.ToolUsed,
    /// tag: "Harvester")</c>. Missing any one of the three leaves that verb unregulatable,
    /// which is the failure R4 forbids.
    ///
    /// Block placement is not among them, and needs no tag here:
    /// <c>DropOrPickupBlock</c> derives from <c>ItemInteractAction</c> and has no
    /// <c>ToolUsed</c> at all. A settlement regulates the level pass's dirt placement by
    /// naming the block item in that action's own picker (U6), so the capability R4 asks
    /// for exists -- it is simply reached through the block rather than through a tool.
    ///
    /// A plain <see cref="Item"/> rather than a <see cref="ToolItem"/>, for the same
    /// reason <see cref="MiningArmItem"/> is: the farming services build their own game
    /// actions naming this item directly and never trigger a durability effect, and
    /// <c>GameActionExtensions.Fill</c>'s tool parameter is typed <see cref="Item"/>
    /// rather than any narrower tool type. Never crafted, held, or placed -- it exists to
    /// be named.
    /// </summary>
    /// <remarks>
    /// Test scenarios: none reachable as unit tests. Registration and tag membership are
    /// engine-side declarations with no logic to run. The live check is the law editor:
    /// this arm must appear in the tool picker for the plow, plant-seeds, and
    /// harvest-or-hunt actions, and absence from any one of the three is a failure of
    /// this unit rather than of the service that raises that action.
    /// </remarks>
    [Serialized]
    // NOT Category("Hidden"), for the reason MiningArm.cs records at length: "Hidden" is the
    // engine's own switch for keeping a thing out of the civics UI, and it cancels the tags
    // below. The live pass found the Mining Arm absent from a dig-or-mine law's tool picker
    // for exactly that reason. The cost is an unobtainable item appearing in item listings;
    // the benefit is that a settlement can regulate the drone at all.
    [Category("Tool")]
    [Tag("Plow")]
    [Tag("Planter")]
    [Tag("Harvester")]
    [LocDisplayName("Harvest Arm")]
    [LocDescription("The farm drone's plowing, sowing and harvesting tool. Never held or crafted.")]
    public class HarvestArmItem : Item
    {
    }
}
