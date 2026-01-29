using BridgeBeats.Contracts.DTOs;

namespace BridgeBeats.Contracts.Records {

    /// <summary>
    /// Individual result item with card URL and fallback data.
    /// </summary>
    /// <param name="CardUrl">URL to the stored OpenGraph card, if available.</param>
    /// <param name="FallbackData">The raw result data for fallback display.</param>
    public record WebLookupResultItem( string? CardUrl, MediaLinkResult FallbackData );

}
