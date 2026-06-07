using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Logging;

namespace BridgeBeats.Core.Domain.Services.LinkResolver;

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
/// <remarks>
/// Initializes a new instance of the <see cref="CachingMediaLinkService"/> class.
/// </remarks>
/// <param name="orchestrator">The lookup orchestrator for queue-based lookups.</param>
/// <param name="logger">Logger for diagnostic information.</param>
public sealed partial class CachingMediaLinkService(
    ILookupOrchestrator orchestrator,
    ILogger<CachingMediaLinkService> logger
) : IMediaLinkService {

    private readonly ILookupOrchestrator _orchestrator = orchestrator
                                                       ?? throw new ArgumentNullException( nameof( orchestrator ) );
    private readonly ILogger<CachingMediaLinkService> _logger = logger
                                                              ?? throw new ArgumentNullException( nameof( logger ) );

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
    /// Adds rate limit or pending-provider information to the result's messages if this is a partial result.
    /// Creates a lightweight result with messages when no data is available but rate limiting occurred.
    /// </summary>
    private MediaLinkResult? AddPartialMessage( LookupResult lookupResult ) {
        if (lookupResult.Result is null) {
            // If rate-limited with no data, create a lightweight result carrying only messages
            if (lookupResult.IsPartial && lookupResult.RateLimitedProviders is { Count: > 0 }) {
                MediaLinkResult rateLimitResult = new( ) {
                    Messages = [],
                    IsPartial = true,
                    RateLimitedProviders = [.. lookupResult.RateLimitedProviders.Select( r => r.Provider )]
                };

                foreach (ProviderRateLimitInfo rateLimitInfo in lookupResult.RateLimitedProviders) {
                    string message = $"{rateLimitInfo.Provider} is temporarily rate-limited. Results will be available shortly (retry after {rateLimitInfo.RetryAfter:u}).";
                    rateLimitResult.Messages.Add( message );

                    LogPartialResultReturned( _logger, lookupResult.SagaId ?? string.Empty, rateLimitInfo.Provider, rateLimitInfo.RetryAfter );
                }

                return rateLimitResult;
            }

            return null;
        }

        if (lookupResult.IsPartial) {
            // Keep the DTO honest for downstream consumers (web UI, bot, cache)
            lookupResult.Result.IsPartial = true;
            lookupResult.Result.Messages ??= [];

            if (lookupResult.RateLimitedProviders is { Count: > 0 }) {
                foreach (ProviderRateLimitInfo rateLimitInfo in lookupResult.RateLimitedProviders) {
                    string message = $"{rateLimitInfo.Provider} is temporarily unavailable. Results will be updated when available (retry after {rateLimitInfo.RetryAfter:u}).";
                    lookupResult.Result.Messages.Add( message );

                    LogPartialResultReturned( _logger, lookupResult.SagaId ?? string.Empty, rateLimitInfo.Provider, rateLimitInfo.RetryAfter );
                }
            } else {
                // Partial because secondary provider lookups are still pending
                lookupResult.Result.Messages.Add( "Additional provider results are still being fetched. Look up this link again shortly for the complete set of links." );

                LogPendingPartialReturned( _logger, lookupResult.SagaId ?? string.Empty );
            }
        }

        return lookupResult.Result;
    }

    #region LoggerMessage Definitions

    /// <summary>
    /// Logs that a partial result was returned due to rate limiting.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.LinkResolver.PartialResultReturned,
        Level = LogLevel.Information,
        Message = "Partial result returned for saga {SagaId}. {Provider} rate-limited until {RetryAfter}" )]
    private static partial void LogPartialResultReturned( ILogger logger, string sagaId, SupportedProviders provider, DateTimeOffset retryAfter );

    /// <summary>
    /// Logs that a partial result was returned with secondary provider lookups still pending.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.LinkResolver.PendingPartialResultReturned,
        Level = LogLevel.Information,
        Message = "Partial result returned for saga {SagaId} with secondary provider lookups still pending" )]
    private static partial void LogPendingPartialReturned( ILogger logger, string sagaId );

    #endregion LoggerMessage Definitions
}
