using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Records;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Orchestrates lookup operations through the queue-based infrastructure.
/// </summary>
/// <remarks>
/// All lookups flow through cache checking, deduplication, queue submission, and Pub/Sub notification.
/// Initial lookups use Interactive priority; secondary lookups use Background priority.
/// </remarks>
public interface ILookupOrchestrator {
    /// <summary>
    /// Performs a lookup by URL content, extracting and processing music links.
    /// </summary>
    /// <param name="content">Content containing music URLs to parse and look up.</param>
    /// <returns>An async enumerable of lookup results for each discovered link.</returns>
    IAsyncEnumerable<LookupResult> LookupByContentAsync( string content );

    /// <summary>
    /// Performs a lookup by track/album title and artist name.
    /// </summary>
    /// <param name="title">The track or album title.</param>
    /// <param name="artist">The artist name.</param>
    /// <returns>The lookup result with partial status and any rate limit info.</returns>
    Task<LookupResult> LookupByMetadataAsync( string title, string artist );

    /// <summary>
    /// Performs a lookup by ISRC (International Standard Recording Code).
    /// </summary>
    /// <param name="isrc">The ISRC code to look up.</param>
    /// <returns>The lookup result with partial status and any rate limit info.</returns>
    Task<LookupResult> LookupByIsrcAsync( string isrc );

    /// <summary>
    /// Performs a lookup by UPC (Universal Product Code) for albums.
    /// </summary>
    /// <param name="upc">The UPC code to look up.</param>
    /// <returns>The lookup result with partial status and any rate limit info.</returns>
    Task<LookupResult> LookupByUpcAsync( string upc );

    /// <summary>
    /// Performs a lookup by provider-specific ID.
    /// </summary>
    /// <param name="providerId">The provider's internal ID for the track/album.</param>
    /// <param name="provider">The music provider the ID belongs to.</param>
    /// <param name="isAlbum">True if looking up an album, false for a track.</param>
    /// <returns>The lookup result with partial status and any rate limit info.</returns>
    Task<LookupResult> LookupByProviderIdAsync( string providerId, SupportedProviders provider, bool isAlbum );
}
