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
            // The dock's own master switch, same as the stock component's first check.
            if (!this.AutoLink) return Unlinked;

            // A target that has opted out stays out. Both of these are the stock component's own
            // refusals, kept deliberately: the deed rule is the only one being replaced.
            if (linkedObj.HasComponent<VehicleComponent>() && linkedObj.Auth?.IsPublicProperty == true)
                return Unlinked;

            if (compType.HasAttribute<DefaultToUnlinkedAttribute>())
                return Unlinked;

            // The replaced rule: no deed comparison. Reachable and permitted is enough.
            return new LinkSettings { Input = true, Output = true };
        }
    }
}
