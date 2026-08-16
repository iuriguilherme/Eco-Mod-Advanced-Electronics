using System;
using Eco.Gameplay.Aliases;
using Eco.Gameplay.Components;
using Eco.Gameplay.Objects;
using Eco.Shared.Serialization;
using Eco.Shared.Utils;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// The dock's link component. Now a <see cref="SharedLinkComponent"/> -- the same base a Store
    /// uses -- rather than a plain LinkComponent.
    ///
    /// WHY. The dock's Storage tab rendered "LINKABLE INVENTORIES (PERSONAL)" with no per-target
    /// Take From / Put Into controls, while a Store beside it rendered "(SHARED)" with them. That
    /// missing control is the whole problem: without it "link the unowned stockpile yourself" is
    /// not an action the player can perform, which is what made the auto-link workaround look
    /// necessary in the first place. SharedLinkComponent is the difference between those two
    /// panels, and it is also correct on the merits -- link choices on a machine belong to the
    /// property, so a co-owner sees the same wiring rather than each person maintaining a private
    /// copy.
    ///
    /// The default stays vanilla (see below). Manual control plus a sane default is how every
    /// other machine in the game behaves, and it is what was asked for.
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
    // Tab, icon and priority are inherited from SharedLinkComponent -- redeclaring them here only
    // risks the two drifting apart.
    [Serialized]
    public class DroneDockLinkComponent : SharedLinkComponent
    {
        private static LinkSettings Unlinked => new LinkSettings { Input = false, Output = false };

        protected override LinkSettings NewDefaultLinkSettings(IAlias alias, WorldObject linkedObj, Type compType)
        {
            // The wide default is back, KNOWINGLY AGAINST the stated preference, because the
            // narrow one is only correct once the player can opt a target in -- and they cannot
            // yet. Reverting to vanilla while the Storage tab still offers no Take From / Put Into
            // controls did not restore a choice, it removed the behaviour and left nothing in its
            // place: the hold could reach only owned, same-deed storage and simply stopped when
            // that filled.
            //
            // So this stays until the tab's controls work, at which point the correct default is
            // base.NewDefaultLinkSettings(...) -- one line, already written once, and the commit
            // that flips it should also delete /drone link.
            //
            // Meanwhile /drone link provides the opt-OUT: the wide default plus an explicit way to
            // unlink is a strictly better position than a narrow default with no way to link.
            if (!this.AutoLink) return Unlinked;

            // A target that has opted out stays out. Both are the stock component's own refusals,
            // kept deliberately: the deed rule is the only one being replaced.
            if (linkedObj.HasComponent<VehicleComponent>() && linkedObj.Auth?.IsPublicProperty == true)
                return Unlinked;

            if (compType.HasAttribute<DefaultToUnlinkedAttribute>())
                return Unlinked;

            // The replaced rule: no deed comparison. Reachable and permitted is enough.
            // Authorization is untouched -- GetAuthorizedLinkedObjects still drops anything the
            // querying alias cannot reach, so this widens the default to what that citizen could
            // already access by hand, never beyond it.
            return new LinkSettings { Input = true, Output = true };
        }
    }
}
