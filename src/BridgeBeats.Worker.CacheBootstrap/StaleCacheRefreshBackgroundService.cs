using System.Globalization;
using System.Text.Json;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Domain.Utilities;
using BridgeBeats.Core.Infrastructure.Utilities;
using BridgeBeats.Worker.CacheBootstrap.Logging;
using StackExchange.Redis;

namespace BridgeBeats.Worker.CacheBootstrap;

/// <summary>
/// Periodically selects the oldest stale cached media-link records and bulk-enqueues them for
/// re-lookup, keeping the cache from drifting permanently out of date for records that are never
/// re-requested by users. The sweep schedule is durable across restarts via a Redis last-run
/// marker; a fixed startup grace period prevents hammering the PDS scan on rapid restart cycles.
/// </summary>
/// <remarks>
/// The service operates as an independent failure domain from
/// <see cref="CacheBootstrapBackgroundService"/>. Before each pass it reads the
/// <see cref="CacheBootstrapStatus"/> document from Redis; when <c>IsRunning</c> is
/// <see langword="true"/> it skips that pass to avoid compounding a degraded-cache window. A missing
/// or unreadable status is treated as not-running, so the sweep proceeds. Per-record failures are
/// counted and skipped; the remaining records are still enqueued. A fatal error during a pass is
/// logged and the loop continues to the next scheduled pass.
/// </remarks>
/// <param name="atProtoStorage">Streams every stored record from the user's ATProto PDS.</param>
/// <param name="sagaManager">Creates and initializes sagas for re-lookup jobs.</param>
/// <param name="queueResolver">Resolves the per-provider queue used to enqueue re-lookup requests.</param>
/// <param name="redis">The Redis connection used to read the bootstrap status and durable schedule marker.</param>
/// <param name="enabledProviders">The set of providers that participate in re-lookups.</param>
/// <param name="settings">Configuration: PDS URI, user DID, cache freshness window, refresh interval, max records per run, and pacing intervals.</param>
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

    /// <summary>Redis key that records the UTC instant the most recent refresh pass started.</summary>
    private const string LastRunMarkerKey = "cache:refresh:last-run";

    /// <summary>Fixed grace period at startup before the first scan is allowed to run.</summary>
    private static readonly TimeSpan s_startupGrace = TimeSpan.FromSeconds( 120 );

    /// <summary>Upper bound of the per-pass uniform jitter applied to de-synchronize the sweep from the bootstrap scan.</summary>
    private static readonly TimeSpan s_maxJitter = TimeSpan.FromSeconds( 300 );

    /// <summary>Serializer options used when reading the bootstrap status document from Redis.</summary>
    private static readonly JsonSerializerOptions s_jsonReadOptions = new( ) { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Startup grace duration used by <see cref="ExecuteAsync"/>. Defaults to
    /// <see cref="s_startupGrace"/> (120 s). Tests set this to <see cref="TimeSpan.Zero"/> to drive
    /// <see cref="ExecuteAsync"/> without waiting.
    /// </summary>
    internal TimeSpan _startupGrace = s_startupGrace;

    /// <summary>
    /// Maximum per-pass jitter used by <see cref="ExecuteAsync"/>. Defaults to
    /// <see cref="s_maxJitter"/> (300 s). Tests set this to <see cref="TimeSpan.Zero"/> to make
    /// schedule waits deterministic.
    /// </summary>
    internal TimeSpan _maxJitter = s_maxJitter;

    /// <summary>
    /// Waits a fixed startup grace, then loops on the durable schedule: reads the last-run marker
    /// to determine elapsed time, waits the remainder plus jitter, writes the marker before
    /// selection, and runs the refresh pass. Cancellation ends the loop cleanly; per-pass errors
    /// are logged and do not stop the loop.
    /// </summary>
    /// <param name="stoppingToken">Signals when the host is shutting down.</param>
    /// <returns>A task that completes when the service stops.</returns>
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        try {
            await Task.Delay( _startupGrace, stoppingToken );
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
            LogRefreshShuttingDown( logger );
            return;
        }

        while (!stoppingToken.IsCancellationRequested) {
            try {
                DateTimeOffset? lastRun = await ReadLastRunMarkerAsync( );
                TimeSpan elapsed = lastRun.HasValue
                    ? DateTimeOffset.UtcNow - lastRun.Value
                    : TimeSpan.MaxValue;

                TimeSpan jitter = TimeSpan.FromMilliseconds(
                    Random.Shared.NextDouble( ) * _maxJitter.TotalMilliseconds );

                TimeSpan wait = elapsed >= settings.RefreshInterval
                    ? jitter
                    : (settings.RefreshInterval - elapsed) + jitter;

                if (wait > TimeSpan.Zero) {
                    await Task.Delay( wait, stoppingToken );
                }

                await WriteLastRunMarkerAsync( );
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
    /// bulk-enqueues each one through the two-layer pacer. Per-record failures are counted and skipped.
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

        int enqueued = 0;
        int skipped = 0;
        int errors = 0;

        // Carries the two mutable pacer states across records in the pass.
        PassPacerState pacer = new( );

        foreach ((string atUri, MediaLinkResult result) in selected) {
            try {
                IReadOnlyList<RefreshLeg> legs = DeriveRefreshLegs( result, enabledProviders );

                if (legs.Count == 0) {
                    LogRefreshRecordSkipped( logger, atUri );
                    skipped++;
                    continue;
                }

                bool didEnqueue = await EnqueueRecordAsync( atUri, result, legs, pacer, cancellationToken );
                if (didEnqueue) {
                    enqueued++;
                } else {
                    skipped++;
                }
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            } catch (Exception ex) {
                errors++;
                LogRefreshEnqueueError( logger, ex, atUri );
            }
        }

        LogRefreshCompleted( logger, enqueued, skipped, errors );
    }

    /// <summary>
    /// Streams the PDS and returns the oldest stale records (up to
    /// <see cref="CacheBootstrapSettings.MaxRecordsPerRun"/>). A record is stale when its
    /// <see cref="MediaLinkResult.LookedUpAt"/> is older than the configured cache window (age-only;
    /// partial-ness is not a staleness trigger). The selection uses a size-bounded sorted set so the
    /// full corpus is never materialized in memory.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the enumeration.</param>
    /// <returns>The oldest stale records, sorted oldest-first.</returns>
    private async Task<List<(string AtUri, MediaLinkResult Result)>> SelectStalestRecordsAsync(
        CancellationToken cancellationToken
    ) {
        int maxN = settings.MaxRecordsPerRun;
        DateTime utcNow = DateTime.UtcNow;

        // A SortedSet sorted ascending by (LookedUpAt, AtUri): the Max entry is the newest stale
        // record and is evicted when the set exceeds maxN, leaving only the oldest maxN entries.
        SortedSet<(DateTime LookedUpAt, string AtUri, MediaLinkResult Result)> oldest =
            new( OldestFirstComparer.Instance );

        try {
            await foreach ((string atUri, MediaLinkResult result) in
                atProtoStorage.ListAllRecordsAsync( settings.PdsUri, settings.UserDid, cancellationToken )) {
                if (!CacheFreshness.IsStale( result.LookedUpAt, settings.CacheDays, utcNow )) {
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
    /// Seeds one saga keyed on the record's external id, initializes exactly the enqueued-leg
    /// providers, and enqueues every leg to its own provider queue through the two-layer pacer.
    /// Returns <see langword="true"/> when all legs were enqueued; <see langword="false"/> when the
    /// leg list is empty (the record carries no usable identifier and was already logged/counted as
    /// skipped by the caller).
    /// </summary>
    /// <param name="atUri">The AT-URI of the record being refreshed (used for logging).</param>
    /// <param name="result">The stale media-link result.</param>
    /// <param name="legs">The pre-derived non-empty leg list from <see cref="DeriveRefreshLegs"/>.</param>
    /// <param name="pacer">Shared pass-wide pacer state (global drip + Tidal gate).</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>
    /// <see langword="true"/> when all legs were enqueued; <see langword="false"/> when the leg list
    /// is empty.
    /// </returns>
    private async Task<bool> EnqueueRecordAsync(
        string atUri,
        MediaLinkResult result,
        IReadOnlyList<RefreshLeg> legs,
        PassPacerState pacer,
        CancellationToken cancellationToken
    ) {
        if (legs.Count == 0) {
            LogRefreshRecordSkipped( logger, atUri );
            return false;
        }

        // Determine saga identity from the record's external id so this saga dedups with an
        // interactive lookup for the same entity and the coordinator's secondary fan-out is
        // suppressed (the IsrcLookup/UpcLookup origin type is the suppression key).
        string sagaLookupKey;
        LookupRequestType sagaLookupType;
        string sagaLookupValue;

        if (TryResolveAnchor( result, out string recordExternalId, out bool recordIsAlbum, out _, out _ )
            && !string.IsNullOrEmpty( recordExternalId )) {
            if (recordIsAlbum) {
                sagaLookupValue = recordExternalId;
                sagaLookupKey = $"{LookupRequestType.UpcLookup}:{sagaLookupValue}";
                sagaLookupType = LookupRequestType.UpcLookup;
            } else {
                sagaLookupValue = recordExternalId.ToUpperInvariant( );
                sagaLookupKey = $"{LookupRequestType.IsrcLookup}:{sagaLookupValue}";
                sagaLookupType = LookupRequestType.IsrcLookup;
            }
        } else {
            // No external id: key the saga on the first native leg in deterministic provider order
            // so the id is stable across passes for the same record.
            RefreshLeg firstLeg = legs
                .OrderBy( l => (int)l.Provider )
                .First( );
            sagaLookupKey = LookupKeyBuilder.TypedKey( firstLeg.LookupType, firstLeg.Provider, firstLeg.LookupValue );
            sagaLookupType = firstLeg.LookupType;
            sagaLookupValue = firstLeg.LookupValue;
        }

        string sagaId = ISagaStateManager.GenerateSagaId( sagaLookupKey );

        _ = await sagaManager.GetOrCreateAsync(
            sagaId,
            sagaLookupKey,
            sagaLookupType,
            sagaLookupValue,
            originPriority: QueuePriority.Bulk,
            cancellationToken: cancellationToken
        );

        // Initialize exactly the providers we are enqueuing legs for — the mode-B fix.
        await sagaManager.InitializeProviderStatesAsync(
            sagaId,
            legs.Select( l => l.Provider ).Distinct( ),
            cancellationToken
        );

        foreach (RefreshLeg leg in legs) {
            // Global drip: one leg per RefreshEnqueuePacing interval across the whole pass.
            // The very first leg of the pass is released immediately; every subsequent leg waits.
            if (pacer.AnyLegEnqueued && settings.RefreshEnqueuePacing > TimeSpan.Zero) {
                await Task.Delay( settings.RefreshEnqueuePacing, cancellationToken );
            }

            // Tidal burst-0 gate: Tidal legs may not be released faster than TidalRefreshMinInterval.
            // nextTidalReleaseAt is a pass-wide ceiling; no accumulation (credit is not carried forward).
            if (leg.Provider == SupportedProviders.Tidal
                && settings.TidalRefreshMinInterval > TimeSpan.Zero) {
                TimeSpan tidalWait = pacer.NextTidalReleaseAt - DateTimeOffset.UtcNow;
                if (tidalWait > TimeSpan.Zero) {
                    await Task.Delay( tidalWait, cancellationToken );
                }
                pacer.NextTidalReleaseAt = DateTimeOffset.UtcNow + settings.TidalRefreshMinInterval;
            }

            QueuedLookupRequest request = new( ) {
                RequestId = Guid.NewGuid( ).ToString( "N" ),
                Provider = leg.Provider,
                LookupType = leg.LookupType,
                LookupValue = leg.LookupValue,
                SagaId = sagaId,
                IsAlbum = leg.IsAlbum,
                Title = leg.Title,
                Artist = leg.Artist,
                OriginPriority = QueuePriority.Bulk
            };

            IRequestQueue<QueuedLookupRequest> queue = queueResolver.GetQueue( leg.Provider );
            await queue.EnqueueAsync( request, QueuePriority.Bulk, cancellationToken );
            pacer.AnyLegEnqueued = true;
        }

        return true;
    }

    /// <summary>
    /// Decomposes a stale record into the set of provider legs that will refresh it: a native-id
    /// leg for each present provider whose URL yields a parseable native id, plus an external-id
    /// fallback leg for every enabled provider that is missing from the record or whose URL is
    /// unparseable. Returns an empty list when no leg can be derived (record is skipped).
    /// </summary>
    /// <param name="record">The stale record to decompose.</param>
    /// <param name="providers">The set of enabled providers.</param>
    /// <returns>
    /// The ordered leg list, or an empty list when the record has no parseable URL and no external
    /// id to fall back on.
    /// </returns>
    internal static IReadOnlyList<RefreshLeg> DeriveRefreshLegs(
        MediaLinkResult record,
        IReadOnlyCollection<SupportedProviders> providers
    ) {
        List<RefreshLeg> legs = [];

        _ = TryResolveAnchor( record, out string recordExternalId, out bool recordIsAlbum,
            out string? anchorTitle, out string? anchorArtist );

        // Step 2: native-id leg per present provider that is still enabled.
        // Providers absent from enabledProviders are skipped entirely: no native leg is emitted
        // and no fallback is registered — the provider drops off the record on the next write.
        foreach ((SupportedProviders provider, MusicLookupResult providerResult) in record.Results) {
            if (!providers.Contains( provider )) {
                continue;
            }

            string? id = ProviderUrlParser.ExtractId( provider, providerResult.URL ?? string.Empty );
            if (!string.IsNullOrWhiteSpace( id )) {
                bool isAlbum = providerResult.IsAlbum ?? false;
                legs.Add( new RefreshLeg(
                    provider,
                    isAlbum ? LookupRequestType.AlbumIdLookup : LookupRequestType.SongIdLookup,
                    id.Trim( ),
                    isAlbum,
                    providerResult.Title,
                    providerResult.Artist
                ) );
            }
        }

        // Step 3: track which providers got a native-id leg.
        HashSet<SupportedProviders> covered = [.. legs.Select( l => l.Provider )];

        // Step 4: external-id fallback for missing and unparseable-present providers.
        foreach (SupportedProviders provider in providers) {
            if (covered.Contains( provider )) {
                continue;
            }

            if (!string.IsNullOrEmpty( recordExternalId )) {
                LookupRequestType fallbackType;
                string fallbackValue;
                if (recordIsAlbum) {
                    fallbackType = LookupRequestType.UpcLookup;
                    fallbackValue = recordExternalId;
                } else {
                    fallbackType = LookupRequestType.IsrcLookup;
                    fallbackValue = recordExternalId.ToUpperInvariant( );
                }
                legs.Add( new RefreshLeg(
                    provider,
                    fallbackType,
                    fallbackValue,
                    recordIsAlbum,
                    anchorTitle,
                    anchorArtist
                ) );
            }
            // If recordExternalId is blank, no fallback can be emitted for this provider.
        }

        // Step 5: empty list means no usable identifier; caller logs and counts the skip.
        return legs;
    }

    /// <summary>
    /// Resolves the record-level external id and album discriminant from the first provider result
    /// that carries a non-blank <see cref="MusicLookupResult.ExternalId"/>, matching the precedence
    /// <c>RecordKeyGenerator.GenerateRkey</c> uses. Returns <see langword="true"/> when an anchor
    /// was found.
    /// </summary>
    private static bool TryResolveAnchor(
        MediaLinkResult record,
        out string recordExternalId,
        out bool recordIsAlbum,
        out string? anchorTitle,
        out string? anchorArtist
    ) {
        foreach (MusicLookupResult r in record.Results.Values) {
            if (!string.IsNullOrWhiteSpace( r.ExternalId )) {
                recordExternalId = r.ExternalId.Trim( );
                recordIsAlbum = r.IsAlbum ?? false;
                anchorTitle = r.Title;
                anchorArtist = r.Artist;
                return true;
            }
        }

        recordExternalId = string.Empty;
        recordIsAlbum = false;
        anchorTitle = null;
        anchorArtist = null;
        return false;
    }

    /// <summary>
    /// Reads the durable last-run marker from Redis. Returns <see langword="null"/> when the key is
    /// absent or the read fails (treated as due-now by the caller).
    /// </summary>
    private async Task<DateTimeOffset?> ReadLastRunMarkerAsync( ) {
        try {
            IDatabase db = redis.GetDatabase( );
            RedisValue value = await db.StringGetAsync( LastRunMarkerKey );
            if (value.IsNullOrEmpty) {
                return null;
            }

            if (DateTimeOffset.TryParse(
                    value.ToString( ),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset parsed )) {
                return parsed;
            }

            return null;
        } catch (Exception ex) {
            LogRefreshMarkerReadError( logger, ex );
            return null;
        }
    }

    /// <summary>
    /// Writes the current UTC instant as the durable last-run marker. A write failure is logged at
    /// Warning and the pass proceeds; on the next start the stale marker causes the service to treat
    /// itself as due-now, which may produce one extra pass.
    /// </summary>
    private async Task WriteLastRunMarkerAsync( ) {
        try {
            IDatabase db = redis.GetDatabase( );
            _ = await db.StringSetAsync(
                LastRunMarkerKey,
                DateTimeOffset.UtcNow.ToString( "O" )
            );
        } catch (Exception ex) {
            LogRefreshMarkerWriteError( logger, ex );
        }
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

    /// <summary>
    /// A worker-internal record struct describing one provider leg of a refresh operation. Never
    /// serialized and never crosses a process boundary.
    /// </summary>
    internal readonly record struct RefreshLeg(
        SupportedProviders Provider,
        LookupRequestType LookupType,
        string LookupValue,
        bool IsAlbum,
        string? Title,
        string? Artist
    );

    /// <summary>
    /// Mutable pass-wide state for the two-layer enqueue pacer. Allocated once per pass and
    /// threaded through all <see cref="EnqueueRecordAsync"/> calls so both counters are shared
    /// across records (async methods cannot take ref parameters).
    /// </summary>
    private sealed class PassPacerState {
        /// <summary>
        /// Set to <see langword="true"/> after the first leg of the pass is enqueued. The global
        /// drip delay is skipped before the very first leg.
        /// </summary>
        public bool AnyLegEnqueued { get; set; }

        /// <summary>
        /// Earliest instant the next Tidal leg may be released. Initialized to the pass start time
        /// so the first Tidal leg is always released immediately.
        /// </summary>
        public DateTimeOffset NextTidalReleaseAt { get; set; } = DateTimeOffset.UtcNow;
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

    [LoggerMessage(
        EventId = LogEventIds.RefreshMarkerWriteError,
        Level = LogLevel.Warning,
        Message = "Failed to write durable last-run marker to Redis; pass will proceed and marker may be stale" )]
    private static partial void LogRefreshMarkerWriteError( ILogger logger, Exception ex );

    [LoggerMessage(
        EventId = LogEventIds.RefreshMarkerReadError,
        Level = LogLevel.Warning,
        Message = "Failed to read durable last-run marker from Redis; treating as due-now and running" )]
    private static partial void LogRefreshMarkerReadError( ILogger logger, Exception ex );

    #endregion
}
