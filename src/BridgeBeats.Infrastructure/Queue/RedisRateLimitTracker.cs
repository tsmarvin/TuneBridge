using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BridgeBeats.Infrastructure.Queue;

/// <summary>
/// Redis-based implementation of <see cref="IRateLimitTracker"/> that tracks per-endpoint
/// rate limit state with TTL-based automatic expiration.
/// </summary>
/// <remarks>
/// <para>
/// Key patterns:
/// <list type="bullet">
///   <item><c>ratelimit:{provider}:{endpoint}</c> - String with RetryAfter timestamp, TTL = time until RetryAfter</item>
/// </list>
/// </para>
/// <para>
/// Rate limits automatically expire when their TTL elapses, so no manual cleanup is required
/// for normal operation. The <see cref="ClearAsync"/> method is provided for explicit clearing
/// after a successful request (optional optimization).
/// </para>
/// </remarks>
public sealed class RedisRateLimitTracker : IRateLimitTracker {

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisRateLimitTracker> _logger;

    private const string RateLimitPrefix = "ratelimit:";

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisRateLimitTracker"/> class.
    /// </summary>
    /// <param name="redis">The Redis connection multiplexer.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    public RedisRateLimitTracker(
        IConnectionMultiplexer redis,
        ILogger<RedisRateLimitTracker> logger
    ) {
        _redis = redis ?? throw new ArgumentNullException( nameof( redis ) );
        _logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
    }

    /// <inheritdoc/>
    public async Task<RateLimitState> GetStateAsync(
        SupportedProviders provider,
        string endpoint,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( endpoint );

        string key = GetKey( provider, endpoint );
        IDatabase db = _redis.GetDatabase( );

        RedisValue value = await db.StringGetAsync( key );

        if (value.IsNullOrEmpty) {
            return new RateLimitState(
                IsRateLimited: false,
                RetryAfter: null,
                TimeRemaining: null
            );
        }

        if (!DateTimeOffset.TryParse( value.ToString( ), out DateTimeOffset retryAfter )) {
            // Invalid value, clean it up
            _ = await db.KeyDeleteAsync( key );
            _logger.LogWarning(
                "Invalid rate limit value for {Provider}:{Endpoint}, removed",
                provider,
                endpoint
            );

            return new RateLimitState(
                IsRateLimited: false,
                RetryAfter: null,
                TimeRemaining: null
            );
        }

        TimeSpan remaining = retryAfter - DateTimeOffset.UtcNow;

        if (remaining <= TimeSpan.Zero) {
            // Expired but TTL hasn't cleaned up yet, remove manually
            _ = await db.KeyDeleteAsync( key );

            return new RateLimitState(
                IsRateLimited: false,
                RetryAfter: null,
                TimeRemaining: null
            );
        }

        return new RateLimitState(
            IsRateLimited: true,
            RetryAfter: retryAfter,
            TimeRemaining: remaining
        );
    }

    /// <inheritdoc/>
    public async Task SetRateLimitedAsync(
        SupportedProviders provider,
        string endpoint,
        DateTimeOffset retryAfter,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( endpoint );

        TimeSpan ttl = retryAfter - DateTimeOffset.UtcNow;

        if (ttl <= TimeSpan.Zero) {
            _logger.LogDebug(
                "Rate limit for {Provider}:{Endpoint} already expired, not setting",
                provider,
                endpoint
            );
            return;
        }

        string key = GetKey( provider, endpoint );
        IDatabase db = _redis.GetDatabase( );

        _ = await db.StringSetAsync( key, retryAfter.ToString( "O" ), ttl );

        // Record rate limit metrics
        QueueMetrics.RecordRateLimitEvent( provider, endpoint, ttl.TotalSeconds );

        _logger.LogInformation(
            "Set rate limit for {Provider}:{Endpoint} until {RetryAfter} (TTL: {Ttl})",
            provider,
            endpoint,
            retryAfter,
            ttl
        );
    }

    /// <inheritdoc/>
    public async Task ClearAsync(
        SupportedProviders provider,
        string endpoint,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( endpoint );

        string key = GetKey( provider, endpoint );
        IDatabase db = _redis.GetDatabase( );

        bool deleted = await db.KeyDeleteAsync( key );

        if (deleted) {
            _logger.LogDebug(
                "Cleared rate limit for {Provider}:{Endpoint}",
                provider,
                endpoint
            );
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RateLimitedEndpoint>> GetAllRateLimitedAsync(
        SupportedProviders provider,
        CancellationToken cancellationToken = default
    ) {
        IDatabase db = _redis.GetDatabase( );
        IServer server = _redis.GetServers( ).FirstOrDefault( )
            ?? throw new InvalidOperationException( "No Redis servers available" );

        string pattern = $"{RateLimitPrefix}{provider}:*";
        List<RateLimitedEndpoint> endpoints = [];

        // Use SCAN to find all rate limit keys for this provider
        await foreach (RedisKey redisKey in server.KeysAsync( pattern: pattern )) {
            string key = redisKey.ToString( );
            RedisValue value = await db.StringGetAsync( key );

            if (value.IsNullOrEmpty) {
                continue;
            }

            if (!DateTimeOffset.TryParse( value.ToString( ), out DateTimeOffset retryAfter )) {
                continue;
            }

            if (retryAfter <= DateTimeOffset.UtcNow) {
                // Expired, skip (will be cleaned up by TTL)
                continue;
            }

            // Extract endpoint from key: ratelimit:{provider}:{endpoint}
            string prefix = $"{RateLimitPrefix}{provider}:";
            if (key.StartsWith( prefix, StringComparison.OrdinalIgnoreCase )) {
                string endpoint = key[prefix.Length..];
                endpoints.Add( new RateLimitedEndpoint( endpoint, retryAfter ) );
            }
        }

        return endpoints;
    }

    private static string GetKey( SupportedProviders provider, string endpoint ) =>
        $"{RateLimitPrefix}{provider}:{endpoint.Trim( ).ToLowerInvariant( )}";
}
