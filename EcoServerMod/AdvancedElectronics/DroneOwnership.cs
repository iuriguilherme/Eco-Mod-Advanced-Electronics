using Eco.Gameplay.Players;

namespace Eco.Mods.TechTree
{
    /// <summary>
    /// Owner-attribution helper for the survey drone (U7, R5). Computes the
    /// (OwnerName, OwnerId) pair that <see cref="SurveyDrone.SetOwner"/> stamps onto the
    /// entity -- see that method for where/when it gets called.
    ///
    /// Deliberately not a live <see cref="User"/> reference stored anywhere: users
    /// log off/disconnect, and a stale reference would dangle across a save/load or
    /// session boundary. A plain (name, id) snapshot is what "attribution stamped on the
    /// entity" (KTD7) means here -- enough to say whose action this was without holding
    /// onto anything that can go invalid.
    ///
    /// Deliberately NOT wired to Eco's law-enforcement/citizenship APIs. That is
    /// explicitly deferred per KTD7 -- do not add a violation/citizenship hook here even
    /// though a drone crossing a claim boundary (R4) is an obvious integration point for
    /// one.
    /// </summary>
    public readonly struct DroneOwnership
    {
        /// <summary>No owner assigned (e.g. before <see cref="SurveyDrone.SetOwner"/> is ever called).</summary>
        public static readonly DroneOwnership Unowned = new DroneOwnership(null, 0);

        /// <summary>Display name of the owner at the moment of stamping.</summary>
        public string OwnerName { get; }

        /// <summary>Eco user ID of the owner, or 0 for <see cref="Unowned"/>.</summary>
        public int OwnerId { get; }

        /// <summary>
        /// Plain field assignment -- no Eco dependency. This constructor is the one
        /// piece of genuinely testable logic in this file (see the "Test scenarios" note
        /// on this unit: a dedicated test project for the AdvancedElectronics assembly
        /// is out of this unit's file scope, since that assembly targets
        /// Eco.ReferenceAssemblies and is not designed to run standalone -- see this
        /// unit's report for why one test for a straight-line two-field assignment was
        /// judged not worth a new test project).
        /// </summary>
        public DroneOwnership(string ownerName, int ownerId)
        {
            this.OwnerName = ownerName;
            this.OwnerId = ownerId;
        }

        /// <summary>True when this snapshot identifies a real owner (i.e. not <see cref="Unowned"/>).</summary>
        public bool HasOwner => this.OwnerId != 0;

        /// <summary>
        /// Resolves ownership from the acting user. A null user (e.g. a programmatic
        /// stack change with no clear acting player) stamps <see cref="Unowned"/> rather
        /// than throwing.
        /// </summary>
        public static DroneOwnership FromUser(User user) =>
            user == null ? Unowned : new DroneOwnership(user.Name, user.Id);
    }
}
