namespace TuneBridge.Domain.Types.Enums {

    /// <summary>
    /// Defines the type of lookup entry stored in the database.
    /// </summary>
    public enum LookupEntryType {
        /// <summary>
        /// URL from any source (user input or service API).
        /// </summary>
        Url = 0,

        /// <summary>
        /// External identifier (ISRC for tracks, UPC for albums).
        /// </summary>
        ExternalId = 1,

        /// <summary>
        /// Metadata combination (title|artist).
        /// </summary>
        Metadata = 2
    }
}
