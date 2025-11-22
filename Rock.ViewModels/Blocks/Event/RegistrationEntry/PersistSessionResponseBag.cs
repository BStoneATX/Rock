using System;

namespace Rock.ViewModels.Blocks.Event.RegistrationEntry
{
    /// <summary>
    /// Represents a response containing session persistence details for the Registration Entry block.
    /// </summary>
    public class PersistSessionResponseBag
    {
        /// <summary>
        /// Gets or sets the expiration date and time for the registration session.
        /// </summary>
        public DateTimeOffset ExpirationDateTime { get; set; }

        /// <summary>
        /// Gets or sets the number of spots remaining for the registration instance.
        /// </summary>
        public int? SpotsRemaining { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the timeout is disabled for the registration instance.
        /// </summary>
        public bool IsTimeoutDisabled { get; set; }
    }
}
