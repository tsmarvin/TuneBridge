using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Infrastructure.Cache.Entities;
using BridgeBeats.Infrastructure.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BridgeBeats.Infrastructure.Cache;

/// <summary>
/// Hosted service that migrates existing SQLite media link cache data to Redis.
/// Runs once at startup and sets a migration completion flag in Redis.
/// After migration, falls back to Redis for all operations.
/// </summary>
public sealed class SqliteToRedisMigrator : IHostedService {

    private readonly IDbContextFactory<MediaLinkCacheDbContext>? _dbContextFactory;
    private readonly IConnectionMultiplexer _redis;
    private readonly IATProtoStorageService _atprotoStorage;
    private readonly ILogger<SqliteToRedisMigrator> _logger;
    private readonly int _cacheDays;
    private readonly string _userDID;

    private const string MigrationCompleteKey = "migration:sqlite:complete";
    private const string LookupIsrcPrefix = "lookup:isrc:";
    private const string LookupUpcPrefix = "lookup:upc:";
    private const string LookupUrlPrefix = "lookup:url:";
    private const string LookupCardPrefix = "lookup:card:";
    private const string LookupProviderPrefix = "lookup:provider:";
    private const string LookupMetadataPrefix = "lookup:metadata:";

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteToRedisMigrator"/> class.
    /// </summary>
    /// <param name="dbContextFactory">Factory for creating SQLite database contexts. Can be null if SQLite is not configured.</param>
    /// <param name="redis">The Redis connection multiplexer.</param>
    /// <param name="atprotoStorage">Service for retrieving results from ATProto PDS.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="cacheDays">Number of days to consider cache entries fresh.</param>
    /// <param name="userDID">The user DID for ATProto storage.</param>
    public SqliteToRedisMigrator(
        IDbContextFactory<MediaLinkCacheDbContext>? dbContextFactory,
        IConnectionMultiplexer redis,
        IATProtoStorageService atprotoStorage,
        ILogger<SqliteToRedisMigrator> logger,
        int cacheDays,
        string userDID
    ) {
        _dbContextFactory = dbContextFactory;
        _redis = redis ?? throw new ArgumentNullException( nameof( redis ) );
        _atprotoStorage = atprotoStorage ?? throw new ArgumentNullException( nameof( atprotoStorage ) );
        _logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
        _cacheDays = cacheDays;
        _userDID = userDID ?? throw new ArgumentNullException( nameof( userDID ) );
    }

    /// <inheritdoc/>
    public async Task StartAsync( CancellationToken cancellationToken ) {
        _logger.LogInformation( "SqliteToRedisMigrator: Starting migration check..." );

        try {
            IDatabase db = _redis.GetDatabase( );

            // Check if migration is already complete
            if (await db.KeyExistsAsync( MigrationCompleteKey )) {
                _logger.LogInformation( "SqliteToRedisMigrator: Migration already complete, skipping." );
                return;
            }

            // Check if SQLite database is available
            if (_dbContextFactory is null) {
                _logger.LogInformation( "SqliteToRedisMigrator: SQLite not configured, marking migration as complete." );
                _ = await db.StringSetAsync( MigrationCompleteKey, DateTime.UtcNow.ToString( "O" ) );
                return;
            }

            await MigrateDataAsync( db, cancellationToken );

            // Mark migration as complete (permanent - no TTL)
            _ = await db.StringSetAsync( MigrationCompleteKey, DateTime.UtcNow.ToString( "O" ) );
            _logger.LogInformation( "SqliteToRedisMigrator: Migration completed successfully." );
        } catch (Microsoft.Data.Sqlite.SqliteException sqlEx) when (sqlEx.Message.Contains( "no such table" )) {
            _logger.LogInformation( "SqliteToRedisMigrator: No existing SQLite cache tables found. Nothing to migrate." );
            // Mark migration as complete since there's nothing to migrate
            try {
                IDatabase db = _redis.GetDatabase( );
                _ = await db.StringSetAsync( MigrationCompleteKey, DateTime.UtcNow.ToString( "O" ) );
            } catch {
                // Ignore - just a best effort to mark as complete
            }
        } catch (Exception ex) {
            _logger.LogError( ex, "SqliteToRedisMigrator: Migration failed. Application will continue but cache may be incomplete." );
            // Don't throw - allow application to continue even if migration fails
        }
    }

    /// <inheritdoc/>
    public Task StopAsync( CancellationToken cancellationToken ) => Task.CompletedTask;

    private async Task MigrateDataAsync( IDatabase db, CancellationToken cancellationToken ) {
        await using MediaLinkCacheDbContext dbContext = await _dbContextFactory!.CreateDbContextAsync( cancellationToken );

        // Get total count of cache entries for logging without loading them all into memory
        int totalCount = await dbContext.CacheEntries.CountAsync( cancellationToken );

        _logger.LogInformation( "SqliteToRedisMigrator: Found {Count} cache entries to migrate.", totalCount );

        int successCount = 0;
        int failCount = 0;
        TimeSpan expiry = TimeSpan.FromDays( _cacheDays );

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
                _logger.LogWarning( ex, "SqliteToRedisMigrator: Failed to migrate entry with rkey: {Rkey}", entry.Rkey );
            }
        }

        _logger.LogInformation(
            "SqliteToRedisMigrator: Migration complete. Migrated: {SuccessCount}, Failed: {FailCount}",
            successCount, failCount
        );
    }

    private async Task MigrateCacheEntryAsync( IDatabase db, MediaLinkCacheEntry entry, TimeSpan expiry ) {
        string recordUri = entry.RecordUri;

        // Migrate card ID lookup
        string cardKey = $"{LookupCardPrefix}{entry.CardId}";
        _ = await db.StringSetAsync( cardKey, recordUri, expiry );

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
                _ = await db.StringSetAsync( key, recordUri, expiry );
            }
        }

        // Migrate provider entries
        foreach (MediaProviderEntry provider in entry.ProviderEntries) {
            string providerKey = $"{LookupProviderPrefix}{provider.Provider}:{provider.ProviderId}";
            _ = await db.StringSetAsync( providerKey, recordUri, expiry );
        }

        _logger.LogDebug(
            "SqliteToRedisMigrator: Migrated entry with rkey: {Rkey}, {LookupCount} lookups, {ProviderCount} providers",
            entry.Rkey, entry.LookupEntries.Count, entry.ProviderEntries.Count
        );
    }
}
