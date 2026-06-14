using System.Text.RegularExpressions;
using BridgeBeats.Contracts.Constants;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Domain.Providers.Common;
using BridgeBeats.Core.Infrastructure.Logging;
using BridgeBeats.Core.Infrastructure.Utilities;

namespace BridgeBeats.Services.LinkResolver;

/// <summary>
/// Orchestrates lookup operations through the queue-based infrastructure.
/// </summary>
/// <remarks>
/// All lookups flow through cache check → deduplication → saga creation → queue submission → Pub/Sub wait.
/// Returns a partial result only when a provider is rate-limited or the interactive time budget is exhausted.
/// </remarks>
/// <remarks>
/// Initializes a new instance of the <see cref="LookupOrchestrator"/> class.
/// </remarks>
public sealed partial class LookupOrchestrator(
    IMediaLinkCacheRepository cache,
    IRequestDeduplicator deduplicator,
    ISagaStateManager sagaManager,
    IProviderQueueResolver<QueuedLookupRequest> queueResolver,
    IATProtoStorageService atProtoStorage,
    HashSet<SupportedProviders> enabledProviders,
    ILogger<LookupOrchestrator> logger,
    QueueSettings? settings = null
) : ILookupOrchestrator {
    private readonly IMediaLinkCacheRepository _cache = cache
                                                        ?? throw new ArgumentNullException( nameof( cache ) );
    private readonly IRequestDeduplicator _deduplicator = deduplicator
                                                        ?? throw new ArgumentNullException( nameof( deduplicator ) );
    private readonly ISagaStateManager _sagaManager = sagaManager
                                                    ?? throw new ArgumentNullException( nameof( sagaManager ) );
    private readonly IProviderQueueResolver<QueuedLookupRequest> _queueResolver = queueResolver
                                                                                ?? throw new ArgumentNullException( nameof( queueResolver ) );
    private readonly IATProtoStorageService _atProtoStorage = atProtoStorage
                                                            ?? throw new ArgumentNullException( nameof( atProtoStorage ) );
    private readonly HashSet<SupportedProviders> _enabledProviders = enabledProviders
                                                                   ?? throw new ArgumentNullException( nameof( enabledProviders ) );
    private readonly ILogger<LookupOrchestrator> _logger = logger
                                                         ?? throw new ArgumentNullException( nameof( logger ) );

    private readonly TimeSpan _interactiveWaitTimeout = TimeSpan.FromSeconds( (settings ?? new QueueSettings( )).InteractiveWaitSeconds );

    private static readonly TimeSpan s_deduplicationLockDuration = TimeSpan.FromMinutes( 5 );

    /// <summary>
    /// Target wait budget across all links of a single content lookup. The budget is kept
    /// under the global 120s resilience AttemptTimeout so the server-side budget, not the
    /// transport, is the binding constraint (the Discord worker's HTTP transport backstop is
    /// 130s, above the AttemptTimeout, so the resilience pipeline fires first). Note the
    /// per-link floor below takes precedence, so messages with more than 18 links can
    /// exceed this target (and beyond ~24 links may approach the transport backstop).
    /// </summary>
    private static readonly TimeSpan s_maxContentWaitBudget = TimeSpan.FromSeconds( 90 );

    /// <summary>
    /// Floor for the scaled per-link wait budget so every link gets a usable wait window.
    /// Takes precedence over <see cref="s_maxContentWaitBudget"/> for very link-heavy messages.
    /// </summary>
    private static readonly TimeSpan s_minPerLinkWaitBudget = TimeSpan.FromSeconds( 5 );

    /// <inheritdoc/>
    public async IAsyncEnumerable<LookupResult> LookupByContentAsync( string content ) {
        if (string.IsNullOrWhiteSpace( content )) {
            yield break;
        }

        // Collect unique links for enabled providers up front so the per-link wait
        // budget can be scaled to the number of links
        HashSet<string> processedLinks = new( StringComparer.OrdinalIgnoreCase );
        List<(string Link, SupportedProviders Provider)> links = [];

        foreach (string link in ValidHttpsLink( ).GetGroupValues( content, "Url" )) {
            if (!processedLinks.Add( link )) {
                continue;
            }

            // Determine provider from URL
            SupportedProviders? provider = DetermineProviderFromUrl( link );
            if (provider is null || !_enabledProviders.Contains( provider.Value )) {
                continue;
            }

            links.Add( (link, provider.Value) );
        }

        if (links.Count == 0) {
            yield break;
        }

        // Links resolve sequentially, each waiting up to the interactive budget; scale
        // the per-link budget so the total stays under s_maxContentWaitBudget (a Discord
        // multi-link message must finish before the bot's HTTP transport gives up)
        TimeSpan perLinkBudget = TimeSpan.FromTicks(
            Math.Min( _interactiveWaitTimeout.Ticks, s_maxContentWaitBudget.Ticks / links.Count )
        );
        if (perLinkBudget < s_minPerLinkWaitBudget) {
            perLinkBudget = s_minPerLinkWaitBudget;
        }

        foreach ((string link, SupportedProviders provider) in links) {
            LookupResult result = await LookupByUrlAsync( link, provider, perLinkBudget );
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
        string lookupKey = LookupKeyBuilder.TypedKey( lookupType, provider, normalizedId );

        return await PerformLookupAsync(
            lookupKey: lookupKey,
            lookupType: lookupType,
            lookupValue: normalizedId,
            cacheCheck: ( ) => _cache.TryGetCachedResultByProviderIdAsync( normalizedId, provider, isAlbum ),
            isAlbum: isAlbum,
            initialProvider: provider
        );
    }

    private async Task<LookupResult> LookupByUrlAsync( string url, SupportedProviders provider, TimeSpan? waitBudget = null ) {
        string lookupKey = LookupKeyBuilder.UrlKey( url );

        return await PerformLookupAsync(
            lookupKey: lookupKey,
            lookupType: LookupRequestType.UriLookup,
            lookupValue: url,
            cacheCheck: ( ) => _cache.TryGetCachedResultAsync( url ),
            isAlbum: false, // Will be determined by the lookup
            initialProvider: provider,
            waitBudget: waitBudget
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
        SupportedProviders? initialProvider = null,
        TimeSpan? waitBudget = null
    ) {
        TimeSpan waitTimeout = waitBudget ?? _interactiveWaitTimeout;

        // Step 1: Check cache. Partial results are reported stale by the cache so they
        // fall through to the dedup/wait logic below instead of masquerading as final.
        (MediaLinkResult result, string recordUri, bool isStale)? cached = await cacheCheck( );

        if (cached.HasValue && !cached.Value.isStale) {
            LogCacheHit( _logger, lookupKey );
            return new LookupResult {
                Result = cached.Value.result,
                IsPartial = cached.Value.result.IsPartial
            };
        }

        // Step 2: Check deduplication - is another request in-flight?
        DeduplicationResult dedup = await _deduplicator.TryAcquireAsync( lookupKey, s_deduplicationLockDuration );

        if (!dedup.Acquired && dedup.AlreadyInFlight) {
            LogRequestAlreadyInFlight( _logger, lookupKey );

            string inFlightSagaId = ISagaStateManager.GenerateSagaId( lookupKey );
            DateTimeOffset deadline = DateTimeOffset.UtcNow + waitTimeout;

            // When the in-flight saga already stored a result, skip the blind wait and go
            // straight to the final-result wait, which honors the rate-limit escape hatch
            // and time budget (during a rate-limit window no publish would ever arrive)
            LookupSagaState? inFlightSaga = await _sagaManager.GetAsync( inFlightSagaId );
            string? storedResultUri = inFlightSaga?.FinalResultUri ?? inFlightSaga?.PartialResultUri;
            if (!string.IsNullOrEmpty( storedResultUri )) {
                LogInFlightSagaHasStoredResult( _logger, inFlightSagaId );
                return await WaitForFinalResultAsync( lookupKey, inFlightSagaId, storedResultUri, deadline );
            }

            // Wait for the other instance to complete
            string? resultUri = await _deduplicator.WaitForCompletionAsync( lookupKey, waitTimeout );

            // Check for rate-limited sentinel - return any stored partial data
            // alongside the rate-limit info instead of dropping it
            if (resultUri == LookupConstants.RateLimitedSentinel) {
                return await BuildRateLimitedPartialAsync( inFlightSagaId );
            }

            if (!string.IsNullOrEmpty( resultUri )) {
                // The published result may be partial (secondary lookups pending) -
                // keep waiting for the final with the remaining time budget
                return await WaitForFinalResultAsync( lookupKey, inFlightSagaId, resultUri, deadline );
            }

            // Timeout or failure - check cache again, might have been populated
            cached = await cacheCheck( );
            if (cached.HasValue) {
                return new LookupResult {
                    Result = cached.Value.result,
                    IsPartial = cached.Value.result.IsPartial,
                    SagaId = cached.Value.result.IsPartial ? inFlightSagaId : null
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
                initialProvider,
                waitTimeout
            );
        } catch (Exception ex) {
            LogLookupError( _logger, ex, lookupKey );

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
        SupportedProviders? initialProvider,
        TimeSpan waitTimeout
    ) {
        // Generate deterministic saga ID from lookup key
        string sagaId = ISagaStateManager.GenerateSagaId( lookupKey );

        // Create saga (or resume an in-progress one, e.g. when a cached partial sent us back here)
        LookupSagaState saga = await _sagaManager.GetOrCreateAsync( sagaId, lookupKey, lookupType, lookupValue );

        DateTimeOffset deadline = DateTimeOffset.UtcNow + waitTimeout;

        // Resumed saga that already stored a result: resolve it directly instead of re-queueing
        string? knownResultUri = saga.FinalResultUri ?? saga.PartialResultUri;
        if (!string.IsNullOrEmpty( knownResultUri )) {
            LogSagaResumedWithResult( _logger, sagaId );

            // Release the just-acquired in-flight lock before waiting: this path enqueues
            // no work, so nothing else would release it (the coordinator runs in another
            // process and its release is ownership-checked). Publishing the known URI
            // hands the stored result to callers already blocked in WaitForCompletionAsync.
            await _deduplicator.ReleaseAsync( lookupKey, knownResultUri );

            return await WaitForFinalResultAsync( lookupKey, sagaId, knownResultUri, deadline );
        }

        // Determine which provider to queue first
        SupportedProviders firstProvider = initialProvider ?? _enabledProviders.First();

        // For direct lookups (ISRC/UPC without initialProvider), initialize all enabled providers
        // For URL lookups (with initialProvider), only initialize the first provider
        // The saga coordinator adds the remaining providers to the SAME saga once the
        // initial result yields an external ID for secondary lookups
        List<SupportedProviders> providersToInitialize = initialProvider.HasValue
            ? [firstProvider]
            : [.. _enabledProviders];

        await _sagaManager.InitializeProviderStatesAsync( sagaId, providersToInitialize );

        // If we have an initial provider (from URL lookup), set it
        if (initialProvider.HasValue) {
            await _sagaManager.SetInitialProviderAsync( sagaId, initialProvider.Value );
        }

        // Queue the initial provider lookup at Interactive priority,
        // unless the resumed saga already has this provider queued or completed
        if (!saga.ProviderStates.ContainsKey( firstProvider )) {
            QueuedLookupRequest request = new( ) {
                RequestId = Guid.NewGuid( ).ToString( "N" ),
                Provider = firstProvider,
                LookupType = lookupType,
                LookupValue = lookupValue,
                SagaId = sagaId,
                IsAlbum = isAlbum,
                Title = title,
                Artist = artist,
                // Interactive origin is persisted into the saga so secondary lookups
                // spawned by the coordinator inherit the caller's urgency
                OriginPriority = QueuePriority.Interactive
            };

            IRequestQueue<QueuedLookupRequest> queue = _queueResolver.GetQueue( firstProvider );
            await queue.EnqueueAsync( request, QueuePriority.Interactive );

            LogSagaCreated( _logger, sagaId, firstProvider, lookupType, lookupValue );
        }

        // Wait for initial result via deduplicator subscription
        string? resultUri = await _deduplicator.WaitForCompletionAsync( lookupKey, waitTimeout );

        // Check for rate-limited sentinel - return any stored partial data alongside the
        // rate-limit info (the partial write may land moments after the sentinel)
        if (resultUri == LookupConstants.RateLimitedSentinel) {
            return await BuildRateLimitedPartialAsync( sagaId );
        }

        if (!string.IsNullOrEmpty( resultUri )) {
            // Initial lookup completed - the published result may be partial (secondary
            // lookups pending), so keep waiting for the final with the remaining budget
            return await WaitForFinalResultAsync( lookupKey, sagaId, resultUri, deadline );
        }

        // Timeout - check if we have any result. ISRC/UPC direct lookups never write a
        // partial, so the final result URI must be honored too.
        LookupSagaState? saga2 = await _sagaManager.GetAsync( sagaId );

        string? storedResultUri = saga2?.FinalResultUri ?? saga2?.PartialResultUri;
        if (storedResultUri is not null) {
            MediaLinkResult? storedResult = await _atProtoStorage.GetMediaLinkResultAsync( storedResultUri );
            return !string.IsNullOrEmpty( saga2!.FinalResultUri )
                ? new LookupResult { Result = storedResult, IsPartial = false }
                : CreatePartialResult( storedResult, sagaId, saga2 );
        }

        // No result yet
        return new LookupResult {
            Result = null,
            IsPartial = true,
            SagaId = sagaId,
            RateLimitedProviders = GetActiveRateLimits( saga2 )
        };
    }

    /// <summary>
    /// Resolves a completion notification into a final result, re-waiting with the remaining
    /// time budget while the published result is only partial (secondary lookups pending).
    /// </summary>
    /// <remarks>
    /// Returns a partial result immediately when every pending provider is rate-limited
    /// (waiting cannot help) and honestly flags the result partial when the budget runs out.
    /// </remarks>
    /// <param name="lookupKey">The deduplication lookup key (completion channel suffix).</param>
    /// <param name="sagaId">The deterministic saga ID for the lookup.</param>
    /// <param name="resultUri">The result URI received from the completion channel.</param>
    /// <param name="deadline">Absolute point in time at which waiting stops.</param>
    private async Task<LookupResult> WaitForFinalResultAsync(
        string lookupKey,
        string sagaId,
        string resultUri,
        DateTimeOffset deadline
    ) {
        while (true) {
            LookupSagaState? saga = await _sagaManager.GetAsync( sagaId );

            // Prefer the final result URI once available (it may differ from the partial's)
            if (!string.IsNullOrEmpty( saga?.FinalResultUri )) {
                resultUri = saga.FinalResultUri;
            }

            MediaLinkResult? result = await _atProtoStorage.GetMediaLinkResultAsync( resultUri );

            // Saga state missing (expired/deleted) - trust the stored result's own flag
            if (saga is null) {
                return new LookupResult {
                    Result = result,
                    IsPartial = result?.IsPartial ?? false,
                    SagaId = result?.IsPartial == true ? sagaId : null
                };
            }

            bool isFinal = !string.IsNullOrEmpty( saga.FinalResultUri )
                        || (saga.IsComplete && !saga.IsPartial);

            if (isFinal) {
                return new LookupResult {
                    Result = result,
                    IsPartial = false
                };
            }

            // When every pending provider is rate-limited, waiting cannot help -
            // return the partial with rate-limit info immediately (approved escape hatch).
            // Expired rate-limit entries are ignored so an imminent full result is still
            // waited for instead of returning a stale partial.
            if (AllPendingProvidersRateLimited( saga )) {
                LogReturningRateLimitedPartial( _logger, sagaId );
                return CreatePartialResult( result, sagaId, saga );
            }

            TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) {
                LogWaitBudgetExhausted( _logger, sagaId );
                return CreatePartialResult( result, sagaId, saga );
            }

            LogWaitingForFinalResult( _logger, sagaId, remaining.TotalSeconds );

            // Subscribe before re-reading saga state (inside the deduplicator) so a final
            // result or rate-limit sentinel published in the gap is never missed
            string? nextUri = await _deduplicator.WaitForFinalCompletionAsync(
                lookupKey,
                remaining,
                async ( ) => {
                    LookupSagaState? gapSaga = await _sagaManager.GetAsync( sagaId );
                    if (!string.IsNullOrEmpty( gapSaga?.FinalResultUri )) {
                        return gapSaga.FinalResultUri;
                    }

                    // A rate-limit condition arising in the gap is equally unrecoverable
                    // by waiting - surface the sentinel so the escape hatch applies
                    return gapSaga is not null && AllPendingProvidersRateLimited( gapSaga )
                        ? LookupConstants.RateLimitedSentinel
                        : null;
                }
            );

            if (nextUri == LookupConstants.RateLimitedSentinel) {
                return await BuildRateLimitedPartialAsync( sagaId, result );
            }

            if (string.IsNullOrEmpty( nextUri )) {
                // Timed out - re-check once in case the final landed without a notification
                saga = await _sagaManager.GetAsync( sagaId );
                if (!string.IsNullOrEmpty( saga?.FinalResultUri )) {
                    resultUri = saga.FinalResultUri;
                    continue;
                }

                LogWaitBudgetExhausted( _logger, sagaId );
                return CreatePartialResult( result, sagaId, saga );
            }

            resultUri = nextUri;
        }
    }

    /// <summary>
    /// Resolves a rate-limit sentinel into the best available partial response: the saga's
    /// stored result (final preferred over partial) is fetched so available provider links
    /// are returned alongside the rate-limit info instead of being dropped.
    /// </summary>
    /// <param name="sagaId">The deterministic saga ID for the lookup.</param>
    /// <param name="knownResult">An already-fetched result to reuse instead of re-fetching.</param>
    private async Task<LookupResult> BuildRateLimitedPartialAsync( string sagaId, MediaLinkResult? knownResult = null ) {
        LookupSagaState? saga = await _sagaManager.GetAsync( sagaId );

        MediaLinkResult? result = knownResult;
        string? storedResultUri = saga?.FinalResultUri ?? saga?.PartialResultUri;
        if (result is null && !string.IsNullOrEmpty( storedResultUri )) {
            result = await _atProtoStorage.GetMediaLinkResultAsync( storedResultUri );
        }

        LogReturningRateLimitedPartial( _logger, sagaId );
        return CreatePartialResult( result, sagaId, saga );
    }

    /// <summary>
    /// Creates a partial <see cref="LookupResult"/> carrying the saga's unexpired rate-limit info.
    /// </summary>
    private static LookupResult CreatePartialResult( MediaLinkResult? result, string sagaId, LookupSagaState? saga )
        => new( ) {
            Result = result,
            IsPartial = true,
            SagaId = sagaId,
            RateLimitedProviders = GetActiveRateLimits( saga )
        };

    /// <summary>
    /// Filters the saga's rate-limit info down to entries whose retry-after has not yet
    /// lapsed, so expired limits neither trigger the escape hatch nor produce stale
    /// "retry after" user messaging. Returns null when nothing is actively rate-limited.
    /// </summary>
    private static List<ProviderRateLimitInfo>? GetActiveRateLimits( LookupSagaState? saga ) {
        List<ProviderRateLimitInfo>? activeRateLimits = saga?.RateLimitInfo?
            .Where( r => r.RetryAfter > DateTimeOffset.UtcNow )
            .ToList( );

        return activeRateLimits is { Count: > 0 } ? activeRateLimits : null;
    }

    /// <summary>
    /// Determines whether every pending provider is covered by an unexpired rate limit,
    /// meaning waiting longer cannot produce additional results.
    /// </summary>
    private static bool AllPendingProvidersRateLimited( LookupSagaState saga ) {
        List<ProviderRateLimitInfo>? activeRateLimits = GetActiveRateLimits( saga );
        return activeRateLimits is not null
            && saga.PendingProviders.All( p => activeRateLimits.Any( r => r.Provider == p ) );
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

    #region LoggerMessage Definitions

    /// <summary>
    /// Logs a cache hit for a lookup key.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.LinkResolver.OrchestratorCacheHit,
        Level = LogLevel.Debug,
        Message = "Cache hit for {LookupKey}" )]
    private static partial void LogCacheHit( ILogger logger, string lookupKey );

    /// <summary>
    /// Logs that a request is already in-flight.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.LinkResolver.OrchestratorRequestAlreadyInFlight,
        Level = LogLevel.Debug,
        Message = "Request {LookupKey} already in-flight, waiting for completion" )]
    private static partial void LogRequestAlreadyInFlight( ILogger logger, string lookupKey );

    /// <summary>
    /// Logs an error performing a lookup.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.LinkResolver.OrchestratorLookupError,
        Level = LogLevel.Error,
        Message = "Error performing lookup for {LookupKey}" )]
    private static partial void LogLookupError( ILogger logger, Exception ex, string lookupKey );

    /// <summary>
    /// Logs that a saga was created and initial lookup queued.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.LinkResolver.OrchestratorSagaCreated,
        Level = LogLevel.Information,
        Message = "Created saga {SagaId} and queued initial lookup for {Provider} ({LookupType}:{LookupValue})" )]
    private static partial void LogSagaCreated( ILogger logger, string sagaId, SupportedProviders provider, LookupRequestType lookupType, string lookupValue );

    /// <summary>
    /// Logs that an in-flight saga already stored a result, so the final-result wait is used directly.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.LinkResolver.OrchestratorInFlightSagaHasStoredResult,
        Level = LogLevel.Debug,
        Message = "In-flight saga {SagaId} already stored a result, waiting for the final result directly" )]
    private static partial void LogInFlightSagaHasStoredResult( ILogger logger, string sagaId );

    /// <summary>
    /// Logs that an existing saga with a stored result was resumed.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.LinkResolver.OrchestratorSagaResumedWithResult,
        Level = LogLevel.Debug,
        Message = "Resumed saga {SagaId} with an existing stored result, resolving directly" )]
    private static partial void LogSagaResumedWithResult( ILogger logger, string sagaId );

    /// <summary>
    /// Logs that the orchestrator keeps waiting for the final result of a partial saga.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.LinkResolver.OrchestratorWaitingForFinalResult,
        Level = LogLevel.Debug,
        Message = "Saga {SagaId} is partial, waiting up to {RemainingSeconds:F1}s for the final result" )]
    private static partial void LogWaitingForFinalResult( ILogger logger, string sagaId, double remainingSeconds );

    /// <summary>
    /// Logs that a partial result is returned because all pending providers are rate-limited.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.LinkResolver.OrchestratorReturningRateLimitedPartial,
        Level = LogLevel.Information,
        Message = "All pending providers for saga {SagaId} are rate-limited, returning partial result" )]
    private static partial void LogReturningRateLimitedPartial( ILogger logger, string sagaId );

    /// <summary>
    /// Logs that the interactive wait budget was exhausted before the final result arrived.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.LinkResolver.OrchestratorWaitBudgetExhausted,
        Level = LogLevel.Information,
        Message = "Wait budget exhausted for saga {SagaId}, returning best available partial result" )]
    private static partial void LogWaitBudgetExhausted( ILogger logger, string sagaId );

    #endregion LoggerMessage Definitions
}
