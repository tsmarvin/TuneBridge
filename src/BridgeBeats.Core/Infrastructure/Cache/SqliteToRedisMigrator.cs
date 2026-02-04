using BridgeBeats.Contracts.Enums;
using BridgeBeats.Core.Infrastructure.Cache.Entities;
using BridgeBeats.Core.Infrastructure.Logging;
using BridgeBeats.Core.Infrastructure.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace BridgeBeats.Core.Infrastructure.Cache;

/// <summary>
/// Hosted service that migrates existing SQLite media link cache data to Redis.
/// Runs once at startup and sets a migration completion flag in Redis.
/// After migration, falls back to Redis for all operations.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="SqliteToRedisMigrator"/> class.
/// </remarks>
/// <param name="dbContextFactory">Factory for creating SQLite database contexts. Can be null if SQLite is not configured.</param>
/// <param name="redis">The Redis connection multiplexer.</param>
/// <param name="logger">Logger for diagnostic information.</param>
/// <param name="cacheDays">Number of days to consider cache entries fresh.</param>
public sealed partial class SqliteToRedisMigrator(
    IDbContextFactory<MediaLinkCacheDbContext>? dbContextFactory,
    IConnectionMultiplexer redis,
    ILogger<SqliteToRedisMigrator> logger,
    int cacheDays
) : IHostedService {

    private readonly IConnectionMultiplexer _redis = redis
                                                   ?? throw new ArgumentNullException( nameof( redis ) );
    private readonly ILogger<SqliteToRedisMigrator> _logger = logger
                                                            ?? throw new ArgumentNullException( nameof( logger ) );

    private const string MigrationCompleteKey = "migration:sqlite:complete";
    private const string LookupIsrcPrefix = "lookup:isrc:";
    private const string LookupUpcPrefix = "lookup:upc:";
    private const string LookupUrlPrefix = "lookup:url:";
    private const string LookupCardPrefix = "lookup:card:";
    private const string LookupProviderPrefix = "lookup:provider:";
    private const string LookupMetadataPrefix = "lookup:metadata:";

    /// <inheritdoc/>
    public async Task StartAsync( CancellationToken cancellationToken ) {
        LogStarting( _logger );

        try {
            IDatabase db = _redis.GetDatabase( );

            // Check if migration is already complete
            if (await db.KeyExistsAsync( MigrationCompleteKey )) {
                LogAlreadyComplete( _logger );
                return;
            }

            // Check if SQLite database is available
            if (dbContextFactory is null) {
                LogNoSqlite( _logger );
                _ = await db.StringSetAsync( MigrationCompleteKey, DateTime.UtcNow.ToString( "O" ) );
                return;
            }

            await MigrateDataAsync( db, cancellationToken );

            // Mark migration as complete (permanent - no TTL)
            _ = await db.StringSetAsync( MigrationCompleteKey, DateTime.UtcNow.ToString( "O" ) );
            LogCompleted( _logger );
        } catch (Microsoft.Data.Sqlite.SqliteException sqlEx) when (sqlEx.Message.Contains( "no such table" )) {
            LogNoTables( _logger );
            // Mark migration as complete since there's nothing to migrate
            try {
                IDatabase db = _redis.GetDatabase( );
                _ = await db.StringSetAsync( MigrationCompleteKey, DateTime.UtcNow.ToString( "O" ) );
            } catch {
                // Ignore - just a best effort to mark as complete
            }
        } catch (Exception ex) {
            LogFailed( _logger, ex );
            // Don't throw - allow application to continue even if migration fails
        }
    }

    /// <inheritdoc/>
    public Task StopAsync( CancellationToken cancellationToken ) => Task.CompletedTask;

    private async Task MigrateDataAsync( IDatabase db, CancellationToken cancellationToken ) {
        await using MediaLinkCacheDbContext dbContext = await dbContextFactory!.CreateDbContextAsync( cancellationToken );

        // Get total count of cache entries for logging without loading them all into memory
        int totalCount = await dbContext.CacheEntries.CountAsync( cancellationToken );

        LogFoundEntries( _logger, totalCount );

        int successCount = 0;
        int failCount = 0;
        TimeSpan expiry = TimeSpan.FromDays( cacheDays );

        // Stream cache entries to avoid loading all into memory at once
        await foreach (MediaLinkCacheEntry entry in dbContext.CacheEntries
            .Include( c => c.LookupEntries )
            .Include( c => c.ProviderEntries )
            .AsNoTracking( )
            .AsAsyncEnumerable( )
            .WithCancellation( cancellationToken )) {
            cancellationToken.ThrowIfCancellationRequested( );

            try {
                await MigrateCacheEntryAsync( db, entry, expiry );
                successCount++;
            } catch (Exception ex) {
                failCount++;
                LogEntryFailed( _logger, ex, entry.Rkey );
            }
        }

        LogMigrationComplete( _logger, successCount, failCount );
    }

    private async Task MigrateCacheEntryAsync( IDatabase db, MediaLinkCacheEntry entry, TimeSpan expiry ) {
        string recordUri = entry.RecordUri;
        int keysAdded = 0;
        int keysSkipped = 0;

        // Migrate card ID lookup (only if not already present or different)
        string cardKey = $"{LookupCardPrefix}{entry.CardId}";
        if (await SetIfNewOrDifferentAsync( db, cardKey, recordUri, expiry )) {
            keysAdded++;
        } else {
            keysSkipped++;
        }

        // Migrate lookup entries
        foreach (MediaLookupEntry lookup in entry.LookupEntries) {
            string? key = lookup.LookupType switch {
                LookupEntryType.Url => $"{LookupUrlPrefix}{HashUtility.HashUrl( lookup.LookupValue )}",
                LookupEntryType.ExternalId => lookup.IsAlbum
                    ? $"{LookupUpcPrefix}{lookup.LookupValue.Trim().ToUpperInvariant()}"
                    : $"{LookupIsrcPrefix}{lookup.LookupValue.Trim().ToUpperInvariant()}",
                LookupEntryType.Metadata => $"{LookupMetadataPrefix}{HashUtility.ComputeSha256Base32( lookup.LookupValue )}",
                _ => null
            };

            if (key != null) {
                if (await SetIfNewOrDifferentAsync( db, key, recordUri, expiry )) {
                    keysAdded++;
                } else {
                    keysSkipped++;
                }
            }
        }

        // Migrate provider entries
        foreach (MediaProviderEntry provider in entry.ProviderEntries) {
            string providerKey = $"{LookupProviderPrefix}{provider.Provider}:{provider.ProviderId}";
            if (await SetIfNewOrDifferentAsync( db, providerKey, recordUri, expiry )) {
                keysAdded++;
            } else {
                keysSkipped++;
            }
        }

        LogProcessedEntry( _logger, entry.Rkey, keysAdded, keysSkipped );
    }

    /// <summary>
    /// Sets a key in Redis only if it doesn't exist or has a different value.
    /// Returns true if the key was set (new or updated), false if skipped (already exists with same value).
    /// </summary>
    private static async Task<bool> SetIfNewOrDifferentAsync( IDatabase db, string key, string value, TimeSpan expiry ) {
        string? existingValue = await db.StringGetAsync( key );

        // Key doesn't exist - set it
        if (existingValue is null) {
            _ = await db.StringSetAsync( key, value, expiry );
            return true;
        }

        // Key exists with same value - skip (but refresh TTL)
        if (existingValue == value) {
            _ = await db.KeyExpireAsync( key, expiry );
            return false;
        }

        // Key exists with different value - update it
        _ = await db.StringSetAsync( key, value, expiry );
        return true;
    }

    #region LoggerMessage Methods

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.SqliteToRedisMigratorStarting,
        Level = LogLevel.Information,
        Message = "SqliteToRedisMigrator: Starting migration check..." )]
    internal static partial void LogStarting( ILogger logger );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.SqliteToRedisMigratorAlreadyComplete,
        Level = LogLevel.Information,
        Message = "SqliteToRedisMigrator: Migration already complete, skipping." )]
    internal static partial void LogAlreadyComplete( ILogger logger );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.SqliteToRedisMigratorNoSqlite,
        Level = LogLevel.Information,
        Message = "SqliteToRedisMigrator: SQLite not configured, marking migration as complete." )]
    internal static partial void LogNoSqlite( ILogger logger );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.SqliteToRedisMigratorCompleted,
        Level = LogLevel.Information,
        Message = "SqliteToRedisMigrator: Migration completed successfully." )]
    internal static partial void LogCompleted( ILogger logger );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.SqliteToRedisMigratorNoTables,
        Level = LogLevel.Information,
        Message = "SqliteToRedisMigrator: No existing SQLite cache tables found. Nothing to migrate." )]
    internal static partial void LogNoTables( ILogger logger );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.SqliteToRedisMigratorFailed,
        Level = LogLevel.Error,
        Message = "SqliteToRedisMigrator: Migration failed. Application will continue but cache may be incomplete." )]
    internal static partial void LogFailed( ILogger logger, Exception ex );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.SqliteToRedisMigratorFoundEntries,
        Level = LogLevel.Information,
        Message = "SqliteToRedisMigrator: Found {Count} cache entries to migrate." )]
    internal static partial void LogFoundEntries( ILogger logger, int count );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.SqliteToRedisMigratorEntryFailed,
        Level = LogLevel.Warning,
        Message = "SqliteToRedisMigrator: Failed to migrate entry with rkey: {Rkey}" )]
    internal static partial void LogEntryFailed( ILogger logger, Exception ex, string rkey );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.SqliteToRedisMigratorMigrationComplete,
        Level = LogLevel.Information,
        Message = "SqliteToRedisMigrator: Migration complete. Processed: {SuccessCount}, Failed: {FailCount}" )]
    internal static partial void LogMigrationComplete( ILogger logger, int successCount, int failCount );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.SqliteToRedisMigratorProcessedEntry,
        Level = LogLevel.Debug,
        Message = "SqliteToRedisMigrator: Processed entry with rkey: {Rkey}, added: {KeysAdded}, skipped: {KeysSkipped}" )]
    internal static partial void LogProcessedEntry( ILogger logger, string rkey, int keysAdded, int keysSkipped );

    #endregion
}
