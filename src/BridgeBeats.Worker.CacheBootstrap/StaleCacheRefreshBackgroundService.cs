using System.Text.Json;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Worker.CacheBootstrap.Logging;
using StackExchange.Redis;

namespace BridgeBeats.Worker.CacheBootstrap;

/// <summary>
/// Periodically selects the oldest stale or expired cached media-link records and bulk-enqueues them
/// for re-lookup, keeping the cache from drifting permanently out of date for records that are never
/// re-requested by users. Runs on a configurable interval with the first tick deferred until after one
/// full interval; does not run immediately at startup.
/// </summary>
/// <remarks>
/// The service operates as an independent failure domain from
/// <see cref="CacheBootstrapBackgroundService"/>. Before each pass it reads the
/// <see cref="CacheBootstrapStatus"/> document from Redis; when <c>IsRunning</c> is
/// <see langword="true"/> it skips that pass to avoid compounding a degraded-cache window. A missing
/// or unreadable status is treated as not-running, so the sweep proceeds. Per-record failures are
/// counted and skipped; the remaining records are still enqueued. A fatal error during a pass is
/// logged and the loop continues to the next tick.
/// </remarks>
/// <param name="atProtoStorage">Streams every stored record from the user's ATProto PDS.</param>
/// <param name="sagaManager">Creates and initializes sagas for re-lookup jobs.</param>
/// <param name="queueResolver">Resolves the per-provider queue used to enqueue re-lookup requests.</param>
/// <param name="redis">The Redis connection used to read the bootstrap status document.</param>
/// <param name="enabledProviders">The set of providers that participate in re-lookups.</param>
/// <param name="settings">Configuration: PDS URI, user DID, cache freshness window, refresh interval, and max records per run.</param>
/// <param name="logger">The logger for this service.</param>
public sealed partial class StaleCacheRefreshBackgroundService(
    IATProtoStorageService atProtoStorage,
    ISagaStateManager sagaManager,
    IProviderQueueResolver<QueuedLookupRequest> queueResolver,
    IConnectionMultiplexer redis,
    HashSet<SupportedProviders> enabledProviders,
    CacheBootstrapSettings settings,
    ILogger<StaleCacheRefreshBackgroundService> logger
) : BackgroundService {

    /// <summary>Serializer options used when reading the bootstrap status document from Redis.</summary>
    private static readonly JsonSerializerOptions s_jsonReadOptions = new( ) { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Waits one full refresh interval, then loops: on each tick selects the oldest stale records
    /// and bulk-enqueues them for re-lookup. Cancellation ends the loop cleanly; per-tick errors are
    /// logged and do not stop the loop.
    /// </summary>
    /// <param name="stoppingToken">Signals when the host is shutting down.</param>
    /// <returns>A task that completes when the service stops.</returns>
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        using PeriodicTimer timer = new( settings.RefreshInterval );

        while (!stoppingToken.IsCancellationRequested) {
            try {
                _ = await timer.WaitForNextTickAsync( stoppingToken );
                await RunRefreshPassAsync( stoppingToken );
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                LogRefreshPeriodicError( logger, ex );
            }
        }

        LogRefreshShuttingDown( logger );
    }

    /// <summary>
    /// Performs one stale-cache refresh pass: checks whether the bootstrap is running (skips if so),
    /// streams the PDS to identify the oldest stale records up to the configured maximum, and
    /// bulk-enqueues each one. Per-record failures are counted and skipped.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the pass.</param>
    /// <returns>A task that completes when the pass finishes or aborts.</returns>
    internal async Task RunRefreshPassAsync( CancellationToken cancellationToken ) {
        if (enabledProviders.Count == 0) {
            LogRefreshNoEnabledProviders( logger );
            return;
        }

        CacheBootstrapStatus? status = await GetBootstrapStatusAsync( );
        if (status?.IsRunning == true) {
            LogRefreshSkippedBootstrapRunning( logger );
            return;
        }

        List<(string AtUri, MediaLinkResult Result)> selected = await SelectStalestRecordsAsync( cancellationToken );

        if (selected.Count == 0) {
            return;
        }

        LogRefreshStarting( logger, selected.Count );

        SupportedProviders firstProvider = enabledProviders.First( );
        int enqueued = 0;
        int skipped = 0;
        int errors = 0;

        foreach ((string atUri, MediaLinkResult result) in selected) {
            try {
                bool didEnqueue = await EnqueueRecordAsync( atUri, result, firstProvider, cancellationToken );
                if (didEnqueue) {
                    enqueued++;
                } else {
                    skipped++;
                }
            } catch (Exception ex) {
                errors++;
                LogRefreshEnqueueError( logger, ex, atUri );
            }
        }

        LogRefreshCompleted( logger, enqueued, skipped, errors );
    }

    /// <summary>
    /// Streams the PDS and returns the oldest stale records (up to
    /// <see cref="CacheBootstrapSettings.MaxRecordsPerRun"/>). A record is stale when it is partial
    /// or its <see cref="MediaLinkResult.LookedUpAt"/> is older than the configured cache window. The
    /// selection uses a size-bounded sorted set so the full corpus is never materialized in memory.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the enumeration.</param>
    /// <returns>The oldest stale records, sorted oldest-first.</returns>
    private async Task<List<(string AtUri, MediaLinkResult Result)>> SelectStalestRecordsAsync(
        CancellationToken cancellationToken
    ) {
        int maxN = settings.MaxRecordsPerRun;
        DateTime expirationDate = DateTime.UtcNow.AddDays( -settings.CacheDays );

        // A SortedSet sorted ascending by (LookedUpAt, AtUri): the Max entry is the newest stale
        // record and is evicted when the set exceeds maxN, leaving only the oldest maxN entries.
        SortedSet<(DateTime LookedUpAt, string AtUri, MediaLinkResult Result)> oldest =
            new( OldestFirstComparer.Instance );

        try {
            await foreach ((string atUri, MediaLinkResult result) in
                atProtoStorage.ListAllRecordsAsync( settings.PdsUri, settings.UserDid, cancellationToken )) {
                bool isStale = result.IsPartial || result.LookedUpAt < expirationDate;
                if (!isStale) {
                    continue;
                }

                _ = oldest.Add( (result.LookedUpAt, atUri, result) );

                if (oldest.Count > maxN) {
                    _ = oldest.Remove( oldest.Max );
                }
            }
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            LogRefreshCancelled( logger );
            throw;
        } catch (Exception ex) {
            LogRefreshFatalError( logger, ex );
            return [];
        }

        return [.. oldest.Select( e => (e.AtUri, e.Result) )];
    }

    /// <summary>
    /// Derives the lookup parameters for a single stale record and enqueues one re-lookup request.
    /// Returns <see langword="true"/> when the request was enqueued and <see langword="false"/> when
    /// the record carries no usable lookup identifier and was skipped.
    /// </summary>
    /// <param name="atUri">The AT-URI of the record being refreshed.</param>
    /// <param name="result">The stale media-link result.</param>
    /// <param name="firstProvider">The provider queue to enqueue the request to.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>
    /// <see langword="true"/> when the record was enqueued; <see langword="false"/> when it was
    /// skipped because no lookup identifier could be derived.
    /// </returns>
    private async Task<bool> EnqueueRecordAsync(
        string atUri,
        MediaLinkResult result,
        SupportedProviders firstProvider,
        CancellationToken cancellationToken
    ) {
        (LookupRequestType lookupType, string lookupKey, string lookupValue, bool isAlbum,
            string? title, string? artist) = DeriveEntryParams( result );

        if (string.IsNullOrWhiteSpace( lookupKey )) {
            LogRefreshRecordSkipped( logger, atUri );
            return false;
        }

        string sagaId = ISagaStateManager.GenerateSagaId( lookupKey );

        _ = await sagaManager.GetOrCreateAsync(
            sagaId,
            lookupKey,
            lookupType,
            lookupValue,
            originPriority: QueuePriority.Bulk,
            cancellationToken: cancellationToken
        );

        await sagaManager.InitializeProviderStatesAsync( sagaId, enabledProviders, cancellationToken );

        QueuedLookupRequest request = new( ) {
            RequestId = Guid.NewGuid( ).ToString( "N" ),
            Provider = firstProvider,
            LookupType = lookupType,
            LookupValue = lookupValue,
            SagaId = sagaId,
            IsAlbum = isAlbum,
            Title = title,
            Artist = artist,
            OriginPriority = QueuePriority.Bulk
        };

        IRequestQueue<QueuedLookupRequest> queue = queueResolver.GetQueue( firstProvider );
        await queue.EnqueueAsync( request, QueuePriority.Bulk, cancellationToken );

        return true;
    }

    /// <summary>
    /// Derives lookup type, key, value, album flag, and optional metadata from a stale record's
    /// provider results. Uses external-id precedence: the first result with a non-empty
    /// <see cref="MusicLookupResult.ExternalId"/> drives an ISRC or UPC lookup; when none has an
    /// external id but any has both <see cref="MusicLookupResult.Title"/> and
    /// <see cref="MusicLookupResult.Artist"/>, a song-title lookup is used; otherwise the record is
    /// un-enqueuable and an empty <c>lookupKey</c> is returned.
    /// </summary>
    /// <param name="result">The stale <see cref="MediaLinkResult"/> whose provider results are inspected.</param>
    /// <returns>
    /// A tuple of lookup type, lookup key (empty string when no identifier is derivable), lookup
    /// value, album flag, and optional title/artist metadata.
    /// </returns>
    internal static (LookupRequestType LookupType, string LookupKey, string LookupValue, bool IsAlbum,
        string? Title, string? Artist) DeriveEntryParams( MediaLinkResult result ) {

        // External-id path: first result with a non-empty ExternalId wins
        foreach (MusicLookupResult providerResult in result.Results.Values) {
            if (!string.IsNullOrWhiteSpace( providerResult.ExternalId )) {
                bool isAlbum = providerResult.IsAlbum ?? false;
                if (isAlbum) {
                    string normalizedUpc = providerResult.ExternalId.Trim( );
                    string lookupKey = $"{LookupRequestType.UpcLookup}:{normalizedUpc}";
                    return (LookupRequestType.UpcLookup, lookupKey, normalizedUpc, true,
                        providerResult.Title, providerResult.Artist);
                } else {
                    string normalizedIsrc = providerResult.ExternalId.Trim( ).ToUpperInvariant( );
                    string lookupKey = $"{LookupRequestType.IsrcLookup}:{normalizedIsrc}";
                    return (LookupRequestType.IsrcLookup, lookupKey, normalizedIsrc, false,
                        providerResult.Title, providerResult.Artist);
                }
            }
        }

        // Metadata fallback: first result with non-empty Title and Artist
        foreach (MusicLookupResult providerResult in result.Results.Values) {
            if (!string.IsNullOrWhiteSpace( providerResult.Title ) &&
                !string.IsNullOrWhiteSpace( providerResult.Artist )) {
                string lookupKey = $"{LookupRequestType.SongLookup}:{providerResult.Title.Trim( ).ToUpperInvariant( )}:{providerResult.Artist.Trim( ).ToUpperInvariant( )}";
                string lookupValue = $"{providerResult.Title}|{providerResult.Artist}";
                return (LookupRequestType.SongLookup, lookupKey, lookupValue, false,
                    providerResult.Title, providerResult.Artist);
            }
        }

        // No usable identifier — skip this record
        return (LookupRequestType.SongLookup, string.Empty, string.Empty, false, null, null);
    }

    /// <summary>
    /// Reads the last-published bootstrap status from Redis. Failures are logged and treated as
    /// no status (proceed with the refresh pass).
    /// </summary>
    /// <returns>
    /// The previously published <see cref="CacheBootstrapStatus"/>, or <see langword="null"/> if
    /// none exists or the read failed.
    /// </returns>
    private async Task<CacheBootstrapStatus?> GetBootstrapStatusAsync( ) {
        try {
            IDatabase db = redis.GetDatabase( );
            RedisValue value = await db.StringGetAsync( CacheBootstrapStatus.RedisKey );
            return value.IsNullOrEmpty
                ? null
                : JsonSerializer.Deserialize<CacheBootstrapStatus>( value.ToString( ), s_jsonReadOptions );
        } catch (Exception ex) {
            LogRefreshStatusReadError( logger, ex );
            return null;
        }
    }

    /// <summary>
    /// Compares stale-record entries by <c>LookedUpAt</c> ascending then by AT-URI as a tiebreaker.
    /// When stored in a <see cref="SortedSet{T}"/>, removing <c>Max</c> always evicts the newest
    /// entry, retaining the oldest N items under bounded-size conditions.
    /// </summary>
    private sealed class OldestFirstComparer : IComparer<(DateTime LookedUpAt, string AtUri, MediaLinkResult Result)> {
        /// <summary>The singleton instance of this comparer.</summary>
        internal static readonly OldestFirstComparer Instance = new( );

        /// <inheritdoc/>
        public int Compare(
            (DateTime LookedUpAt, string AtUri, MediaLinkResult Result) x,
            (DateTime LookedUpAt, string AtUri, MediaLinkResult Result) y
        ) {
            int cmp = x.LookedUpAt.CompareTo( y.LookedUpAt );
            return cmp != 0 ? cmp : string.Compare( x.AtUri, y.AtUri, StringComparison.Ordinal );
        }
    }

    #region LoggerMessage Methods

    [LoggerMessage(
        EventId = LogEventIds.RefreshShuttingDown,
        Level = LogLevel.Information,
        Message = "Stale-cache refresh service is shutting down" )]
    private static partial void LogRefreshShuttingDown( ILogger logger );

    [LoggerMessage(
        EventId = LogEventIds.RefreshPeriodicError,
        Level = LogLevel.Error,
        Message = "Error during stale-cache refresh pass, will retry at next interval" )]
    private static partial void LogRefreshPeriodicError( ILogger logger, Exception ex );

    [LoggerMessage(
        EventId = LogEventIds.RefreshSkippedBootstrapRunning,
        Level = LogLevel.Information,
        Message = "Stale-cache refresh skipped: full bootstrap run is currently in progress" )]
    private static partial void LogRefreshSkippedBootstrapRunning( ILogger logger );

    [LoggerMessage(
        EventId = LogEventIds.RefreshStarting,
        Level = LogLevel.Information,
        Message = "Stale-cache refresh starting: {SelectedCount} records selected for re-lookup" )]
    private static partial void LogRefreshStarting( ILogger logger, int selectedCount );

    [LoggerMessage(
        EventId = LogEventIds.RefreshEnqueueError,
        Level = LogLevel.Warning,
        Message = "Failed to enqueue stale record for re-lookup: {AtUri}" )]
    private static partial void LogRefreshEnqueueError( ILogger logger, Exception ex, string atUri );

    [LoggerMessage(
        EventId = LogEventIds.RefreshCompleted,
        Level = LogLevel.Information,
        Message = "Stale-cache refresh completed: {Enqueued} enqueued, {Skipped} skipped (no identifier), {Errors} errors" )]
    private static partial void LogRefreshCompleted( ILogger logger, int enqueued, int skipped, int errors );

    [LoggerMessage(
        EventId = LogEventIds.RefreshRecordSkipped,
        Level = LogLevel.Debug,
        Message = "Stale record skipped (no usable lookup identifier): {AtUri}" )]
    private static partial void LogRefreshRecordSkipped( ILogger logger, string atUri );

    [LoggerMessage(
        EventId = LogEventIds.RefreshCancelled,
        Level = LogLevel.Information,
        Message = "Stale-cache refresh was cancelled" )]
    private static partial void LogRefreshCancelled( ILogger logger );

    [LoggerMessage(
        EventId = LogEventIds.RefreshFatalError,
        Level = LogLevel.Error,
        Message = "Fatal error during stale-cache refresh pass" )]
    private static partial void LogRefreshFatalError( ILogger logger, Exception ex );

    [LoggerMessage(
        EventId = LogEventIds.RefreshNoEnabledProviders,
        Level = LogLevel.Warning,
        Message = "Stale-cache refresh skipped: no enabled providers are configured" )]
    private static partial void LogRefreshNoEnabledProviders( ILogger logger );

    [LoggerMessage(
        EventId = LogEventIds.RefreshStatusReadError,
        Level = LogLevel.Warning,
        Message = "Failed to read bootstrap status from Redis; treating as not-running and proceeding" )]
    private static partial void LogRefreshStatusReadError( ILogger logger, Exception ex );

    #endregion
}
