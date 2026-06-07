using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Queue;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BridgeBeats.Core.Infrastructure.Extensions;

/// <summary>
/// Extension methods for registering queue infrastructure services.
/// </summary>
public static class QueueServiceExtensions {

    /// <summary>
    /// Adds the core queue infrastructure services (deduplicator, rate limit tracker, saga manager).
    /// These services are shared across all providers.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="registerMetrics">Whether to register queue depth metrics. Default is true.</param>
    /// <returns>The configured service collection.</returns>
    public static IServiceCollection AddQueueInfrastructure( this IServiceCollection services, bool registerMetrics = true ) {
        // Register singleton services that are shared across all providers
        _ = services.AddSingleton<IRequestDeduplicator>( sp => new RedisRequestDeduplicator(
            sp.GetRequiredService<IConnectionMultiplexer>( ),
            sp.GetRequiredService<ILogger<RedisRequestDeduplicator>>( )
        ) );

        _ = services.AddSingleton<IRateLimitTracker>( sp => new RedisRateLimitTracker(
            sp.GetRequiredService<IConnectionMultiplexer>( ),
            sp.GetRequiredService<ILogger<RedisRateLimitTracker>>( )
        ) );

        _ = services.AddSingleton<ISagaStateManager>( sp => new RedisSagaStateManager(
            sp.GetRequiredService<IConnectionMultiplexer>( ),
            sp.GetRequiredService<ILogger<RedisSagaStateManager>>( ),
            sp.GetRequiredService<IOptions<QueueSettings>>( )
        ) );

        // Register queue depth metrics if requested
        if (registerMetrics) {
            _ = services.AddSingleton( sp => {
                IConnectionMultiplexer redis = sp.GetRequiredService<IConnectionMultiplexer>( );
                QueueMetrics.RegisterQueueDepthGauges( redis );
                return new QueueMetricsRegistration( );
            } );
        }

        return services;
    }

    /// <summary>
    /// Adds a provider-specific request queue.
    /// Call this method for each provider that needs queue support.
    /// </summary>
    /// <typeparam name="T">The type of request to queue.</typeparam>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="provider">The music provider this queue serves.</param>
    /// <returns>The configured service collection.</returns>
    public static IServiceCollection AddProviderQueue<T>( this IServiceCollection services, SupportedProviders provider )
        where T : class, IQueueableRequest {
        // Use a keyed service pattern - register a factory that creates provider-specific queues
        _ = services.AddSingleton( sp => {
            RedisRequestQueue<T> queue = new(
                sp.GetRequiredService<IConnectionMultiplexer>( ),
                sp.GetRequiredService<ILogger<RedisRequestQueue<T>>>( ),
                sp.GetRequiredService<IOptions<QueueSettings>>( ),
                provider
            );
            return new ProviderQueueRegistration<T>( provider, queue );
        } );

        return services;
    }

    /// <summary>
    /// Adds request queues for all supported providers.
    /// </summary>
    /// <typeparam name="T">The type of request to queue.</typeparam>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The configured service collection.</returns>
    /// <remarks>
    /// When <typeparamref name="T"/> is <see cref="QueuedLookupRequest"/>, the Spotify queue
    /// registration is automatically wrapped with <see cref="SpotifyBulkQueueDecorator"/> so
    /// that <see cref="LookupRequestType.SongIdLookup"/> and
    /// <see cref="LookupRequestType.AlbumIdLookup"/> requests are routed to the type-specific
    /// bulk streams. No additional registration step is required in any host.
    /// </remarks>
    public static IServiceCollection AddAllProviderQueues<T>( this IServiceCollection services )
        where T : class, IQueueableRequest {
        foreach (SupportedProviders provider in Enum.GetValues<SupportedProviders>( )) {
            _ = services.AddProviderQueue<T>( provider );
        }

        // Register a resolver to get queues by provider. For QueuedLookupRequest, the Spotify
        // queue is wrapped with SpotifyBulkQueueDecorator via ApplySpotifyBulkDecorator so all
        // hosts that produce Spotify ID lookups automatically route to the type-specific bulk
        // streams without any per-host opt-in call.
        _ = services.AddSingleton<IProviderQueueResolver<T>>( sp => {
            IEnumerable<ProviderQueueRegistration<T>> registrations =
                sp.GetServices<ProviderQueueRegistration<T>>( );

            IConnectionMultiplexer redis = sp.GetRequiredService<IConnectionMultiplexer>( );
            ILogger<SpotifyBulkQueueDecorator> decoratorLogger =
                sp.GetRequiredService<ILogger<SpotifyBulkQueueDecorator>>( );

            Dictionary<SupportedProviders, IRequestQueue<T>> queues = registrations.ToDictionary(
                r => r.Provider,
                r => ApplySpotifyBulkDecorator( r.Queue, r.Provider, redis, decoratorLogger )
            );

            return new ProviderQueueResolver<T>( queues );
        } );

        return services;
    }

    /// <summary>
    /// Creates an <see cref="IRequestQueue{QueuedLookupRequest}"/> for the given provider,
    /// delegating the Spotify-wrap decision entirely to <see cref="ApplySpotifyBulkDecorator"/>.
    /// </summary>
    /// <remarks>
    /// This method constructs a raw <see cref="RedisRequestQueue{T}"/> and passes it to
    /// <see cref="ApplySpotifyBulkDecorator"/>, which holds the single <c>provider == Spotify</c>
    /// conditional. Callers of this method — both <c>AddQueueProcessor</c> overloads in
    /// <c>ServiceExtensions</c> — obtain a correctly wrapped queue without duplicating the
    /// wrap decision.
    /// </remarks>
    internal static IRequestQueue<QueuedLookupRequest> CreateProviderQueue(
        IServiceProvider sp,
        SupportedProviders provider
    ) {
        RedisRequestQueue<QueuedLookupRequest> raw = new(
            sp.GetRequiredService<IConnectionMultiplexer>( ),
            sp.GetRequiredService<ILogger<RedisRequestQueue<QueuedLookupRequest>>>( ),
            sp.GetRequiredService<IOptions<QueueSettings>>( ),
            provider
        );
        return ApplySpotifyBulkDecorator(
            (IRequestQueue<QueuedLookupRequest>)raw,
            provider,
            sp.GetRequiredService<IConnectionMultiplexer>( ),
            sp.GetRequiredService<ILogger<SpotifyBulkQueueDecorator>>( )
        );
    }

    /// <summary>
    /// Conditionally wraps <paramref name="raw"/> with <see cref="SpotifyBulkQueueDecorator"/>
    /// when <paramref name="provider"/> is <see cref="SupportedProviders.Spotify"/> and
    /// <typeparamref name="T"/> is <see cref="QueuedLookupRequest"/>; otherwise returns
    /// <paramref name="raw"/> unchanged.
    /// </summary>
    /// <remarks>
    /// The double type-erasing cast is safe because the <c>typeof(T)</c> check is performed
    /// before casting. This is the only location in the codebase that performs this cast.
    /// </remarks>
    private static IRequestQueue<T> ApplySpotifyBulkDecorator<T>(
        IRequestQueue<T> raw,
        SupportedProviders provider,
        IConnectionMultiplexer redis,
        ILogger<SpotifyBulkQueueDecorator> decoratorLogger
    ) where T : class, IQueueableRequest {
        if (provider == SupportedProviders.Spotify && typeof( T ) == typeof( QueuedLookupRequest )) {
            // Safe: typeof(T) == typeof(QueuedLookupRequest) verified above.
            return (IRequestQueue<T>)(object)new SpotifyBulkQueueDecorator(
                (IRequestQueue<QueuedLookupRequest>)(object)raw, redis, decoratorLogger );
        }
        return raw;
    }

    /// <summary>
    /// Configures QueueSettings from configuration.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configureAction">Action to configure queue settings.</param>
    /// <returns>The configured service collection.</returns>
    public static IServiceCollection ConfigureQueueSettings(
        this IServiceCollection services,
        Action<QueueSettings> configureAction
    ) {
        _ = services.Configure( configureAction );
        return services;
    }

}


/// <summary>
/// Internal registration record for provider-specific queues.
/// </summary>
/// <typeparam name="T">The type of request handled by the queue.</typeparam>
/// <param name="Provider">The provider.</param>
/// <param name="Queue">The queue instance.</param>
internal sealed record ProviderQueueRegistration<T>( SupportedProviders Provider, IRequestQueue<T> Queue )
    where T : class, IQueueableRequest;

/// <summary>
/// Default implementation of <see cref="IProviderQueueResolver{T}"/>.
/// </summary>
/// <typeparam name="T">The type of request handled by the queues.</typeparam>
internal sealed class ProviderQueueResolver<T>(
    Dictionary<SupportedProviders,
    IRequestQueue<T>> queues
) : IProviderQueueResolver<T> where T : class, IQueueableRequest {
    private readonly IReadOnlyDictionary<SupportedProviders, IRequestQueue<T>> _queues = queues;

    public IRequestQueue<T> GetQueue( SupportedProviders provider ) {
        return !_queues.TryGetValue( provider, out IRequestQueue<T>? queue )
            ? throw new InvalidOperationException( $"No queue registered for provider {provider}" )
            : queue;
    }

    public IReadOnlyDictionary<SupportedProviders, IRequestQueue<T>> GetAllQueues( ) => _queues;
}

/// <summary>
/// Marker class to indicate queue metrics have been registered.
/// Used as a singleton dependency to ensure metrics are registered exactly once.
/// </summary>
internal sealed class QueueMetricsRegistration;
