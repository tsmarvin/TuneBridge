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
    public static IServiceCollection AddAllProviderQueues<T>( this IServiceCollection services )
        where T : class, IQueueableRequest {
        foreach (SupportedProviders provider in Enum.GetValues<SupportedProviders>( )) {
            _ = services.AddProviderQueue<T>( provider );
        }

        // Also register a resolver to get queues by provider
        _ = services.AddSingleton<IProviderQueueResolver<T>>( sp => {
            IEnumerable<ProviderQueueRegistration<T>> registrations =
                sp.GetServices<ProviderQueueRegistration<T>>( );

            Dictionary<SupportedProviders, IRequestQueue<T>> queues =
                registrations.ToDictionary( r => r.Provider, r => r.Queue );

            return new ProviderQueueResolver<T>( queues );
        } );

        return services;
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
