using System.Text.RegularExpressions;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Domain.Providers.Common;
using BridgeBeats.Core.Infrastructure.Utilities;

namespace BridgeBeats.Services.LinkResolver;

/// <summary>
/// Orchestrates lookup operations through the queue-based infrastructure.
/// </summary>
/// <remarks>
/// <para>
/// All lookup operations flow through this orchestrator:
/// 1. Check Redis cache for existing results
/// 2. Check deduplication (is another request in-flight?)
/// 3. If in-flight, subscribe to completion notification
/// 4. Otherwise, create saga, queue initial provider lookup
/// 5. Wait for initial result via Pub/Sub
/// 6. On result, spawn secondary provider lookups at Background priority
/// 7. Return result to caller (partial if rate-limited)
/// </para>
/// </remarks>
public sealed partial class LookupOrchestrator : ILookupOrchestrator {
    private readonly IMediaLinkCacheRepository _cache;
    private readonly IRequestDeduplicator _deduplicator;
    private readonly ISagaStateManager _sagaManager;
    private readonly IProviderQueueResolver<QueuedLookupRequest> _queueResolver;
    private readonly IATProtoStorageService _atProtoStorage;
    private readonly HashSet<SupportedProviders> _enabledProviders;
    private readonly ILogger<LookupOrchestrator> _logger;

    private static readonly TimeSpan s_defaultTimeout = TimeSpan.FromSeconds( 30 );
    private static readonly TimeSpan s_deduplicationLockDuration = TimeSpan.FromMinutes( 5 );

    /// <summary>
    /// Initializes a new instance of the <see cref="LookupOrchestrator"/> class.
    /// </summary>
    public LookupOrchestrator(
        IMediaLinkCacheRepository cache,
        IRequestDeduplicator deduplicator,
        ISagaStateManager sagaManager,
        IProviderQueueResolver<QueuedLookupRequest> queueResolver,
        IATProtoStorageService atProtoStorage,
        HashSet<SupportedProviders> enabledProviders,
        ILogger<LookupOrchestrator> logger
    ) {
        _cache = cache ?? throw new ArgumentNullException( nameof( cache ) );
        _deduplicator = deduplicator ?? throw new ArgumentNullException( nameof( deduplicator ) );
        _sagaManager = sagaManager ?? throw new ArgumentNullException( nameof( sagaManager ) );
        _queueResolver = queueResolver ?? throw new ArgumentNullException( nameof( queueResolver ) );
        _atProtoStorage = atProtoStorage ?? throw new ArgumentNullException( nameof( atProtoStorage ) );
        _enabledProviders = enabledProviders ?? throw new ArgumentNullException( nameof( enabledProviders ) );
        _logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<LookupResult> LookupByContentAsync( string content ) {
        if (string.IsNullOrWhiteSpace( content )) {
            yield break;
        }

        HashSet<string> processedLinks = new( StringComparer.OrdinalIgnoreCase );

        foreach (string link in ValidHttpsLink( ).GetGroupValues( content, "Url" )) {
            if (!processedLinks.Add( link )) {
                continue;
            }

            // Determine provider from URL
            SupportedProviders? provider = DetermineProviderFromUrl( link );
            if (provider is null || !_enabledProviders.Contains( provider.Value )) {
                continue;
            }

            LookupResult result = await LookupByUrlAsync( link, provider.Value );
            yield return result;
        }
    }

    /// <inheritdoc/>
    public async Task<LookupResult> LookupByMetadataAsync( string title, string artist ) {
        if (string.IsNullOrWhiteSpace( title ) || string.IsNullOrWhiteSpace( artist )) {
            return new LookupResult { Result = null, IsPartial = false };
        }

        string lookupKey = $"{LookupRequestType.SongLookup}:{title.Trim().ToUpperInvariant()}:{artist.Trim().ToUpperInvariant()}";

        return await PerformLookupAsync(
            lookupKey: lookupKey,
            lookupType: LookupRequestType.SongLookup,
            lookupValue: $"{title}|{artist}",
            cacheCheck: ( ) => _cache.TryGetCachedResultByMetadataAsync( title, artist ),
            title: title,
            artist: artist,
            isAlbum: false
        );
    }

    /// <inheritdoc/>
    public async Task<LookupResult> LookupByIsrcAsync( string isrc ) {
        if (string.IsNullOrWhiteSpace( isrc )) {
            return new LookupResult { Result = null, IsPartial = false };
        }

        string normalizedIsrc = isrc.Trim( ).ToUpperInvariant( );
        string lookupKey = $"{LookupRequestType.IsrcLookup}:{normalizedIsrc}";

        return await PerformLookupAsync(
            lookupKey: lookupKey,
            lookupType: LookupRequestType.IsrcLookup,
            lookupValue: normalizedIsrc,
            cacheCheck: ( ) => _cache.TryGetCachedResultByISRCAsync( normalizedIsrc ),
            isAlbum: false
        );
    }

    /// <inheritdoc/>
    public async Task<LookupResult> LookupByUpcAsync( string upc ) {
        if (string.IsNullOrWhiteSpace( upc )) {
            return new LookupResult { Result = null, IsPartial = false };
        }

        string normalizedUpc = upc.Trim( );
        string lookupKey = $"{LookupRequestType.UpcLookup}:{normalizedUpc}";

        return await PerformLookupAsync(
            lookupKey: lookupKey,
            lookupType: LookupRequestType.UpcLookup,
            lookupValue: normalizedUpc,
            cacheCheck: ( ) => _cache.TryGetCachedResultByUPCAsync( normalizedUpc ),
            isAlbum: true
        );
    }

    /// <inheritdoc/>
    public async Task<LookupResult> LookupByProviderIdAsync( string providerId, SupportedProviders provider, bool isAlbum ) {
        if (string.IsNullOrWhiteSpace( providerId )) {
            return new LookupResult { Result = null, IsPartial = false };
        }

        string normalizedId = providerId.Trim( );
        LookupRequestType lookupType = isAlbum ? LookupRequestType.AlbumIdLookup : LookupRequestType.SongIdLookup;
        string lookupKey = $"{lookupType}:{provider}:{normalizedId}";

        return await PerformLookupAsync(
            lookupKey: lookupKey,
            lookupType: lookupType,
            lookupValue: normalizedId,
            cacheCheck: ( ) => _cache.TryGetCachedResultByProviderIdAsync( normalizedId, provider, isAlbum ),
            isAlbum: isAlbum,
            initialProvider: provider
        );
    }

    private async Task<LookupResult> LookupByUrlAsync( string url, SupportedProviders provider ) {
        string lookupKey = $"{LookupRequestType.UriLookup}:{HashUtility.HashUrl( url )}";

        return await PerformLookupAsync(
            lookupKey: lookupKey,
            lookupType: LookupRequestType.UriLookup,
            lookupValue: url,
            cacheCheck: ( ) => _cache.TryGetCachedResultAsync( url ),
            isAlbum: false, // Will be determined by the lookup
            initialProvider: provider
        );
    }

    private async Task<LookupResult> PerformLookupAsync(
        string lookupKey,
        LookupRequestType lookupType,
        string lookupValue,
        Func<Task<(MediaLinkResult result, string recordUri, bool isStale)?>> cacheCheck,
        bool isAlbum = false,
        string? title = null,
        string? artist = null,
        SupportedProviders? initialProvider = null
    ) {
        // Step 1: Check cache
        (MediaLinkResult result, string recordUri, bool isStale)? cached = await cacheCheck( );

        if (cached.HasValue && !cached.Value.isStale) {
            _logger.LogDebug( "Cache hit for {LookupKey}", lookupKey );
            return new LookupResult {
                Result = cached.Value.result,
                IsPartial = false
            };
        }

        // Step 2: Check deduplication - is another request in-flight?
        DeduplicationResult dedup = await _deduplicator.TryAcquireAsync( lookupKey, s_deduplicationLockDuration );

        if (!dedup.Acquired && dedup.AlreadyInFlight) {
            _logger.LogDebug( "Request {LookupKey} already in-flight, waiting for completion", lookupKey );

            // Wait for the other instance to complete
            string? resultUri = await _deduplicator.WaitForCompletionAsync( lookupKey, s_defaultTimeout );

            if (!string.IsNullOrEmpty( resultUri )) {
                MediaLinkResult? completedResult = await _atProtoStorage.GetMediaLinkResultAsync( resultUri );
                return new LookupResult {
                    Result = completedResult,
                    IsPartial = false
                };
            }

            // Timeout or failure - check cache again, might have been populated
            cached = await cacheCheck( );
            if (cached.HasValue) {
                return new LookupResult {
                    Result = cached.Value.result,
                    IsPartial = false
                };
            }

            // Still nothing, return null
            return new LookupResult { Result = null, IsPartial = false };
        }

        try {
            // Step 3: Create saga and queue initial lookup
            return await CreateSagaAndQueueLookupAsync(
                lookupKey,
                lookupType,
                lookupValue,
                isAlbum,
                title,
                artist,
                initialProvider
            );
        } catch (Exception ex) {
            _logger.LogError( ex, "Error performing lookup for {LookupKey}", lookupKey );

            // Release the lock on error
            await _deduplicator.ReleaseAsync( lookupKey, null );

            throw;
        }
    }

    private async Task<LookupResult> CreateSagaAndQueueLookupAsync(
        string lookupKey,
        LookupRequestType lookupType,
        string lookupValue,
        bool isAlbum,
        string? title,
        string? artist,
        SupportedProviders? initialProvider
    ) {
        // Generate deterministic saga ID from lookup key
        string sagaId = ISagaStateManager.GenerateSagaId( lookupKey );

        // Create saga
        _ = await _sagaManager.GetOrCreateAsync( sagaId, lookupKey, lookupType, lookupValue );

        // Determine which provider to queue first
        SupportedProviders firstProvider = initialProvider ?? _enabledProviders.First();

        // For direct lookups (ISRC/UPC without initialProvider), initialize all enabled providers
        // For URL lookups (with initialProvider), only initialize the first provider
        // Secondary provider lookups will create their own separate sagas
        List<SupportedProviders> providersToInitialize = initialProvider.HasValue
            ? [firstProvider]
            : [.. _enabledProviders];

        await _sagaManager.InitializeProviderStatesAsync( sagaId, providersToInitialize );

        // If we have an initial provider (from URL lookup), set it
        if (initialProvider.HasValue) {
            await _sagaManager.SetInitialProviderAsync( sagaId, initialProvider.Value );
        }

        // Queue the initial provider lookup at Interactive priority

        QueuedLookupRequest request = new( ) {
            RequestId = Guid.NewGuid( ).ToString( "N" ),
            Provider = firstProvider,
            LookupType = lookupType,
            LookupValue = lookupValue,
            SagaId = sagaId,
            IsAlbum = isAlbum,
            Title = title,
            Artist = artist
        };

        IRequestQueue<QueuedLookupRequest> queue = _queueResolver.GetQueue( firstProvider );
        await queue.EnqueueAsync( request, QueuePriority.Interactive );

        _logger.LogInformation(
            "Created saga {SagaId} and queued initial lookup for {Provider} ({LookupType}:{LookupValue})",
            sagaId,
            firstProvider,
            lookupType,
            lookupValue
        );

        // Wait for initial result via deduplicator subscription
        string? resultUri = await _deduplicator.WaitForCompletionAsync( lookupKey, s_defaultTimeout );

        if (!string.IsNullOrEmpty( resultUri )) {
            // Initial lookup completed, fetch result
            MediaLinkResult? result = await _atProtoStorage.GetMediaLinkResultAsync( resultUri );

            // Check if saga is complete or has pending providers
            LookupSagaState? updatedSaga = await _sagaManager.GetAsync( sagaId );

            return updatedSaga is not null
                ? new LookupResult {
                    Result = result,
                    IsPartial = updatedSaga.IsPartial,
                    SagaId = updatedSaga.IsPartial ? sagaId : null,
                    RateLimitedProviders = updatedSaga.RateLimitInfo
                }
                : new LookupResult {
                    Result = result,
                    IsPartial = false
                };
        }

        // Timeout - check if we have any result
        LookupSagaState? saga2 = await _sagaManager.GetAsync( sagaId );

        if (saga2?.PartialResultUri is not null) {
            MediaLinkResult? partialResult = await _atProtoStorage.GetMediaLinkResultAsync( saga2.PartialResultUri );
            return new LookupResult {
                Result = partialResult,
                IsPartial = true,
                SagaId = sagaId,
                RateLimitedProviders = saga2.RateLimitInfo
            };
        }

        // No result yet
        return new LookupResult {
            Result = null,
            IsPartial = true,
            SagaId = sagaId
        };
    }

    private static SupportedProviders? DetermineProviderFromUrl( string url ) {
        if (string.IsNullOrWhiteSpace( url )) {
            return null;
        }

        string lowerUrl = url.ToLowerInvariant( );

        return lowerUrl.Contains( "apple" ) || lowerUrl.Contains( "music.apple" )
            ? SupportedProviders.AppleMusic
            : lowerUrl.Contains( "spotify" ) || lowerUrl.Contains( "open.spotify" )
            ? SupportedProviders.Spotify
            : lowerUrl.Contains( "tidal" ) ? SupportedProviders.Tidal : null;
    }

    [GeneratedRegex(
        @"(?<Url>(?<Link>(?:https?://)?(?:www\.)?((?:[a-z0-9-]+\.)?(?:apple\.com|spotify\.com|tidal\.com))[^\s""'<>]*))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex ValidHttpsLink( );
}
