using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using Microsoft.Extensions.Logging;

namespace BridgeBeats.Services.LinkResolver;

/// <summary>
/// Media link service that uses queue-based lookups with Redis cache and ATProto storage.
/// </summary>
/// <remarks>
/// All lookups are delegated to the <see cref="ILookupOrchestrator"/> which handles:
/// <list type="bullet">
///   <item>Redis cache checking</item>
///   <item>Request deduplication</item>
///   <item>Queue-based provider lookups</item>
///   <item>Saga management for partial results</item>
///   <item>ATProto storage for results</item>
/// </list>
/// </remarks>
public sealed class CachingMediaLinkService : IMediaLinkService {

    private readonly ILookupOrchestrator _orchestrator;
    private readonly ILogger<CachingMediaLinkService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CachingMediaLinkService"/> class.
    /// </summary>
    /// <param name="orchestrator">The lookup orchestrator for queue-based lookups.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    public CachingMediaLinkService(
        ILookupOrchestrator orchestrator,
        ILogger<CachingMediaLinkService> logger
    ) {
        _orchestrator = orchestrator ?? throw new ArgumentNullException( nameof( orchestrator ) );
        _logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
    }

    /// <inheritdoc/>
    public async Task<MediaLinkResult?> GetInfoAsync( string title, string artist ) {
        LookupResult result = await _orchestrator.LookupByMetadataAsync( title, artist );
        return AddPartialMessage( result );
    }

    /// <inheritdoc/>
    public async Task<MediaLinkResult?> GetInfoByISRCAsync( string isrc ) {
        LookupResult result = await _orchestrator.LookupByIsrcAsync( isrc );
        return AddPartialMessage( result );
    }

    /// <inheritdoc/>
    public async Task<MediaLinkResult?> GetInfoByUPCAsync( string upc ) {
        LookupResult result = await _orchestrator.LookupByUpcAsync( upc );
        return AddPartialMessage( result );
    }

    /// <inheritdoc/>
    public async Task<MediaLinkResult?> GetInfoByProviderIdAsync(
        string providerId,
        SupportedProviders provider,
        bool isAlbum
    ) {
        LookupResult result = await _orchestrator.LookupByProviderIdAsync( providerId, provider, isAlbum );
        return AddPartialMessage( result );
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<MediaLinkResult> GetInfoAsync( string content ) {
        await foreach (LookupResult result in _orchestrator.LookupByContentAsync( content )) {
            MediaLinkResult? mediaResult = AddPartialMessage( result );
            if (mediaResult is not null) {
                yield return mediaResult;
            }
        }
    }

    /// <summary>
    /// Adds rate limit information to the result's messages if this is a partial result.
    /// </summary>
    private MediaLinkResult? AddPartialMessage( LookupResult lookupResult ) {
        if (lookupResult.Result is null) {
            return null;
        }

        if (lookupResult.IsPartial && lookupResult.RateLimitedProviders is { Count: > 0 }) {
            lookupResult.Result.Messages ??= [];

            foreach (ProviderRateLimitInfo rateLimitInfo in lookupResult.RateLimitedProviders) {
                string message = $"{rateLimitInfo.Provider} is temporarily unavailable. Results will be updated when available (retry after {rateLimitInfo.RetryAfter:u}).";
                lookupResult.Result.Messages.Add( message );

                _logger.LogInformation(
                    "Partial result returned for saga {SagaId}. {Provider} rate-limited until {RetryAfter}",
                    lookupResult.SagaId,
                    rateLimitInfo.Provider,
                    rateLimitInfo.RetryAfter
                );
            }
        }

        return lookupResult.Result;
    }
}
