using BridgeBeats.Contracts.DTOs;

namespace BridgeBeats.Contracts.Records {

    /// <summary>
    /// A web-facing lookup result item, pairing an optional stored OpenGraph card URL with the
    /// underlying media-link data to fall back on when no card URL is available.
    /// </summary>
    /// <param name="CardUrl">The URL of the stored OpenGraph card for this result, when one exists; otherwise <see langword="null"/>.</param>
    /// <param name="FallbackData">The resolved media-link data to use for fallback display when no card URL is available.</param>
    public record WebLookupResultItem( string? CardUrl, MediaLinkResult FallbackData );

}
