namespace BridgeBeats.Contracts.Enums;

/// <summary>
/// Coarse classification of how a lookup entry was keyed. Distinct from
/// <see cref="LookupRequestType"/>, which names the specific lookup strategy.
/// </summary>
public enum LookupEntryType {

    /// <summary>
    /// The lookup was keyed by a provider URL (from user input or a service API).
    /// </summary>
    Url = 0,

    /// <summary>
    /// The lookup was keyed by an external identifier: the ISRC for tracks or the UPC for albums.
    /// </summary>
    ExternalId = 1,

    /// <summary>
    /// The lookup was keyed by free-text metadata, such as title and artist.
    /// </summary>
    Metadata = 2
}
