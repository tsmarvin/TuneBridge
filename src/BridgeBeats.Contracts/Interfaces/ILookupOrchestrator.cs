using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Records;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Top-level entry point for cross-provider lookups. Coordinates the multi-provider saga and
/// returns lookup-result envelopes, streaming partial results as providers respond.
/// </summary>
/// <remarks>
/// Implemented in <c>BridgeBeats.Core</c> by <c>LookupOrchestrator</c>
/// (<c>Domain/Services/LinkResolver/LookupOrchestrator.cs</c>). Every lookup flows through cache
/// checking, request deduplication, queue submission, and Pub/Sub notification. Initial lookups
/// are queued at Interactive priority; secondary lookups run at Background priority. Each method
/// yields a <see cref="LookupResult"/> envelope, which may be partial (<c>IsPartial</c>) while
/// some providers are still pending or rate-limited.
/// </remarks>
public interface ILookupOrchestrator {

    /// <summary>
    /// Resolves arbitrary input content (such as a pasted provider link) across providers,
    /// extracting the music links it contains and streaming each <see cref="LookupResult"/> as it
    /// becomes available.
    /// </summary>
    /// <param name="content">The raw input content to resolve, for example a provider URL.</param>
    /// <returns>
    /// An asynchronous sequence of <see cref="LookupResult"/> envelopes; earlier items may be
    /// partial and later items more complete as additional providers respond.
    /// </returns>
    IAsyncEnumerable<LookupResult> LookupByContentAsync( string content );

    /// <summary>
    /// Resolves a track or album across providers by free-text title and artist.
    /// </summary>
    /// <param name="title">The title to search for.</param>
    /// <param name="artist">The artist to search for.</param>
    /// <returns>A task whose result is the lookup-result envelope for the metadata search.</returns>
    Task<LookupResult> LookupByMetadataAsync( string title, string artist );

    /// <summary>
    /// Resolves a track across providers by ISRC (International Standard Recording Code).
    /// </summary>
    /// <param name="isrc">The ISRC of the recording to resolve.</param>
    /// <returns>A task whose result is the lookup-result envelope for the ISRC.</returns>
    Task<LookupResult> LookupByIsrcAsync( string isrc );

    /// <summary>
    /// Resolves an album or release across providers by UPC (Universal Product Code).
    /// </summary>
    /// <param name="upc">The UPC of the release to resolve.</param>
    /// <returns>A task whose result is the lookup-result envelope for the UPC.</returns>
    Task<LookupResult> LookupByUpcAsync( string upc );

    /// <summary>
    /// Resolves an item across providers starting from a single provider's native id.
    /// </summary>
    /// <param name="providerId">The provider-native id to start from.</param>
    /// <param name="provider">The provider that <paramref name="providerId"/> belongs to.</param>
    /// <param name="isAlbum"><see langword="true"/> when the id identifies an album; <see langword="false"/> for a track.</param>
    /// <returns>A task whose result is the lookup-result envelope for the provider id.</returns>
    Task<LookupResult> LookupByProviderIdAsync( string providerId, SupportedProviders provider, bool isAlbum );
}
