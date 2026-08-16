using System;
using Eco.Gameplay.Aliases;
using Eco.Gameplay.Components;
using Eco.Gameplay.Objects;
using Eco.Shared.Serialization;
using Eco.Shared.Utils;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// The dock's link component, differing from the stock one in exactly one rule: a dock
    /// auto-links to every storage it can reach, not only to storage on its own deed.
    ///
    /// WHY THIS EXISTS. A mining drone unloads into whatever its dock links to, and the stock
    /// default (<see cref="LinkComponent.NewDefaultLinkSettings"/>) refuses anything whose deed is
    /// null or different:
    ///
    ///     shouldLink = linkedObjDeed != null
    ///                  &amp;&amp; linkedObjDeed == parentObjDeed
    ///                  &amp;&amp; !compType.HasAttribute&lt;DefaultToUnlinkedAttribute&gt;();
    ///
    /// An unowned stockpile has no deed, so it was never a candidate. That is correct for a
    /// crafting table -- a player does not want their bench silently feeding from a public pile --
    /// but a drone dock is a hauling machine whose entire job is to put rock somewhere. Live pass
    /// #3 showed the cost: the hold filled, could not empty, and the NEXT dig was refused with
    /// "Not enough room in inventory", which the panel then reported as an obstructed plot. Two of
    /// three areas were lost to a full hold wearing the costume of blocked terrain.
    ///
    /// WHY THIS IS NOT A PERMISSION HOLE. Auto-link decides what is linked BY DEFAULT; it never
    /// decides what may be touched. Every read still goes through
    /// <see cref="LinkComponent.GetAuthorizedLinkedObjects"/>, which drops anything the querying
    /// alias lacks consumer access to, and the unload queries as the stamped citizen (R43). So this
    /// widens the default to "everything this citizen could reach anyway" -- a public stockpile
    /// qualifies, a stranger's locked chest does not, and no authorization decision moves.
    ///
    /// The other object's opt-out is still honoured: a component marked
    /// <c>[DefaultToUnlinked]</c> (fuel tanks, vehicle holds) stays unlinked, and so does a public
    /// vehicle. Those are deliberate refusals by the target, not an artefact of deed geometry, and
    /// overriding them would be this component overreaching.
    /// </summary>
    [Serialized]
    public class DroneDockLinkComponent : LinkComponent
    {
        private static LinkSettings Unlinked => new LinkSettings { Input = false, Output = false };

        protected override LinkSettings NewDefaultLinkSettings(IAlias alias, WorldObject linkedObj, Type compType)
        {
            // REVERTED to the stock default, deliberately.
            //
            // This class briefly replaced the deed rule so a dock auto-linked to every reachable
            // storage, because an unowned stockpile has no deed and the hold could not empty. That
            // fixed the wrong layer: the maintainer's requirement is that a drone behaves like
            // every other machine -- owned storage on the same deed is linked by default, and an
            // unowned stockpile is linked only when the player explicitly says so, exactly as a
            // store or crafting table works. Auto-dumping into public piles is a gameplay decision
            // this mod does not get to make on the player's behalf.
            //
            // The subclass is kept rather than deleted: it is the seam where a drone-specific link
            // rule belongs if one is ever justified, and removing it would mean another
            // [RequireComponent] type change on every saved dock.
            //
            // The real gap this exposed is that the dock's Storage tab offers no per-target
            // enable/disable, so "explicitly link it" is not yet an action the player can take.
            // That is a UI bug to fix, not a reason to widen the default.
            return base.NewDefaultLinkSettings(alias, linkedObj, compType);
        }
    }
}
