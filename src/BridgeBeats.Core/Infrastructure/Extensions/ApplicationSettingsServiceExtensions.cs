using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Infrastructure.Settings;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BridgeBeats.Core.Infrastructure.Extensions;

/// <summary>Dependency-injection registration for database-backed application settings.</summary>
public static class ApplicationSettingsServiceExtensions {
    /// <summary>
    /// Adds the complete host-independent application-settings store. Production, tests, and
    /// administrative hosts use the same persistence and encryption composition.
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    /// <param name="connectionString">The SQLite application database connection string.</param>
    /// <param name="dataProtectionKeyPath">The persistent Data Protection key-ring path.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddBridgeBeatsApplicationSettingsStore(
        this IServiceCollection services,
        string connectionString,
        string dataProtectionKeyPath
    ) {
        services.TryAddSingleton( TimeProvider.System );
        _ = services.AddBridgeBeatsDataProtection( dataProtectionKeyPath );
        _ = services.AddBridgeBeatsApplicationDatabase( connectionString );
        return services.AddDatabaseApplicationSettings( );
    }

    /// <summary>Adds the typed database-backed application-settings service.</summary>
    /// <param name="services">The service collection to populate.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddDatabaseApplicationSettings( this IServiceCollection services ) {
        _ = services.AddSingleton<DatabaseApplicationSettingsService>( );
        _ = services.AddSingleton<IApplicationSettingsService>( serviceProvider =>
            serviceProvider.GetRequiredService<DatabaseApplicationSettingsService>( )
        );
        _ = services.AddSingleton<IApplicationSettingsRuntimeReader>( serviceProvider =>
            serviceProvider.GetRequiredService<DatabaseApplicationSettingsService>( )
        );
        return services;
    }
}
