namespace TuneBridge.Domain.Types.Enums {

    /// <summary>
    /// Defines the type of lookup entry stored in the database.
    /// </summary>
    public enum LookupEntryType {
        /// <summary>
        /// User-provided input link (may contain tracking parameters, stored privately).
        /// </summary>
        UserInput = 0,

        /// <summary>
        /// Clean link provided by a music service API.
        /// </summary>
        ServiceLink = 1,

        /// <summary>
        /// External identifier (ISRC for tracks, UPC for albums).
        /// </summary>
        ExternalId = 2,

        /// <summary>
        /// Metadata combination (title|artist).
        /// </summary>
        Metadata = 3
    }
}
