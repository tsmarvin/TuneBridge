using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Records;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Orchestrates lookup operations through the queue-based infrastructure.
/// </summary>
/// <remarks>
/// <para>
/// All lookup operations flow through this orchestrator, which handles:
/// <list type="bullet">
///   <item>Cache checking (Redis)</item>
///   <item>Request deduplication (prevents duplicate in-flight requests)</item>
///   <item>Queue submission to provider-specific Redis streams</item>
///   <item>Pub/Sub subscription for result notification</item>
///   <item>Saga creation for multi-provider lookups</item>
///   <item>Partial result handling when providers are rate-limited</item>
/// </list>
/// </para>
/// <para>
/// The orchestrator ensures all lookups are queue-based for metrics collection
/// and rate limit tracking. Initial lookups use Interactive priority, while
/// secondary provider lookups (after initial result) use Background priority.
/// </para>
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
