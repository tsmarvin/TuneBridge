using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Infrastructure.Queue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BridgeBeats.Services.Queue;

/// <summary>
/// Extension methods for registering queue processor services in worker applications.
/// </summary>
public static class QueueProcessorServiceExtensions {

    /// <summary>
    /// Adds the queue processor background service and related infrastructure to a provider worker.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method registers:
    /// <list type="bullet">
    ///   <item>Queue settings configuration</item>
    ///   <item>Provider-specific request queue</item>
    ///   <item>Shared queue infrastructure (deduplicator, rate limit tracker, saga manager)</item>
    ///   <item>Queue processor background service</item>
    /// </list>
    /// </para>
    /// <para>
    /// The lookup service must be registered separately before calling this method.
    /// </para>
    /// </remarks>
    /// <typeparam name="TLookupService">The concrete lookup service type for the provider.</typeparam>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="provider">The music provider this worker serves.</param>
    /// <param name="configureSettings">Optional action to configure queue settings.</param>
    /// <returns>The configured service collection.</returns>
    public static IServiceCollection AddQueueProcessor<TLookupService>(
        this IServiceCollection services,
        SupportedProviders provider,
        Action<QueueSettings>? configureSettings = null
    ) where TLookupService : class, IMusicLookupService {
        // Configure queue settings with defaults from the record definition
        // The QueueSettings record already has sensible defaults via init properties
        if (configureSettings is not null) {
            _ = services.Configure( configureSettings );
        }

        // Register shared queue infrastructure
        _ = services.AddQueueInfrastructure( );

        // Register the provider-specific queue
        _ = services.AddSingleton<IRequestQueue<QueuedLookupRequest>>( sp => new RedisRequestQueue<QueuedLookupRequest>(
            sp.GetRequiredService<IConnectionMultiplexer>( ),
            sp.GetRequiredService<ILogger<RedisRequestQueue<QueuedLookupRequest>>>( ),
            sp.GetRequiredService<IOptions<QueueSettings>>( ),
            provider
        ) );

        // Register the background service
        _ = services.AddHostedService( sp => new QueueProcessorBackgroundService(
            sp.GetRequiredService<IConnectionMultiplexer>( ),
            sp.GetRequiredService<IRequestQueue<QueuedLookupRequest>>( ),
            sp.GetRequiredService<IRateLimitTracker>( ),
            sp.GetRequiredService<ISagaStateManager>( ),
            sp.GetRequiredService<TLookupService>( ),
            provider,
            sp.GetRequiredService<ILogger<QueueProcessorBackgroundService>>( )
        ) );

        return services;
    }

    /// <summary>
    /// Adds the queue processor background service using a pre-resolved lookup service instance.
    /// </summary>
    /// <remarks>
    /// Use this overload when the lookup service is resolved via a factory or needs special handling.
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="provider">The music provider this worker serves.</param>
    /// <param name="lookupServiceFactory">Factory to create the lookup service instance.</param>
    /// <param name="configureSettings">Optional action to configure queue settings.</param>
    /// <returns>The configured service collection.</returns>
    public static IServiceCollection AddQueueProcessor(
        this IServiceCollection services,
        SupportedProviders provider,
        Func<IServiceProvider, IMusicLookupService> lookupServiceFactory,
        Action<QueueSettings>? configureSettings = null
    ) {
        // Configure queue settings with defaults from the record definition
        // The QueueSettings record already has sensible defaults via init properties
        if (configureSettings is not null) {
            _ = services.Configure( configureSettings );
        }

        // Register shared queue infrastructure
        _ = services.AddQueueInfrastructure( );

        // Register the provider-specific queue
        _ = services.AddSingleton<IRequestQueue<QueuedLookupRequest>>( sp => new RedisRequestQueue<QueuedLookupRequest>(
            sp.GetRequiredService<IConnectionMultiplexer>( ),
            sp.GetRequiredService<ILogger<RedisRequestQueue<QueuedLookupRequest>>>( ),
            sp.GetRequiredService<IOptions<QueueSettings>>( ),
            provider
        ) );

        // Register the background service using the factory
        _ = services.AddHostedService( sp => new QueueProcessorBackgroundService(
            sp.GetRequiredService<IConnectionMultiplexer>( ),
            sp.GetRequiredService<IRequestQueue<QueuedLookupRequest>>( ),
            sp.GetRequiredService<IRateLimitTracker>( ),
            sp.GetRequiredService<ISagaStateManager>( ),
            lookupServiceFactory( sp ),
            provider,
            sp.GetRequiredService<ILogger<QueueProcessorBackgroundService>>( )
        ) );

        return services;
    }
}
