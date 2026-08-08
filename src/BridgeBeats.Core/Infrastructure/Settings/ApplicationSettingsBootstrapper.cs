using BridgeBeats.Contracts.Exceptions;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Extensions;
using BridgeBeats.Core.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BridgeBeats.Core.Infrastructure.Settings;

/// <summary>
/// Opens the bootstrap-only database and key-ring locations, applies migrations, and returns the
/// authoritative application-settings snapshot used to compose the process topology.
/// </summary>
public static class ApplicationSettingsBootstrapper {
    /// <summary>The opt-in configuration key that permits first-run startup seeding.</summary>
    public const string SeedSettingsKey = "BridgeBeats:Bootstrap:SeedSettings";

    /// <summary>Applies database migrations and loads the current runtime settings snapshot.</summary>
    /// <param name="connectionString">Bootstrap-only SQLite connection string.</param>
    /// <param name="dataProtectionKeyPath">Persistent Data Protection key-ring directory.</param>
    /// <param name="cancellationToken">Token that cancels migration or loading.</param>
    /// <returns>The stored settings, or <see langword="null"/> before first-run setup.</returns>
    public static async Task<ApplicationSettingsSnapshot?> MigrateAndLoadAsync(
        string connectionString,
        string dataProtectionKeyPath,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( connectionString );
        ArgumentException.ThrowIfNullOrWhiteSpace( dataProtectionKeyPath );

        ServiceCollection services = new( );
        _ = services.AddBridgeBeatsApplicationSettingsStore( connectionString, dataProtectionKeyPath );

        await using ServiceProvider provider = services.BuildServiceProvider( );
        IDbContextFactory<ApplicationDbContext> contextFactory = provider
            .GetRequiredService<IDbContextFactory<ApplicationDbContext>>( );
        await using (ApplicationDbContext context = await contextFactory.CreateDbContextAsync( cancellationToken )) {
            await context.Database.MigrateAsync( cancellationToken );
        }

        return await provider
            .GetRequiredService<IApplicationSettingsRuntimeReader>( )
            .GetRuntimeSettingsAsync( cancellationToken );
    }

    /// <summary>
    /// Applies migrations, seeds an empty settings store from ordinary .NET configuration when
    /// explicitly enabled, and returns the authoritative persisted snapshot. Existing settings are
    /// never updated from startup configuration.
    /// </summary>
    /// <param name="connectionString">Bootstrap-only SQLite connection string.</param>
    /// <param name="dataProtectionKeyPath">Persistent Data Protection key-ring directory.</param>
    /// <param name="configuration">Configuration populated by user-secrets, environment variables, or command line.</param>
    /// <param name="cancellationToken">Token that cancels migration, seeding, or loading.</param>
    /// <returns>The stored settings, or <see langword="null"/> when the store is empty and seeding is disabled.</returns>
    public static async Task<ApplicationSettingsSnapshot?> MigrateSeedAndLoadAsync(
        string connectionString,
        string dataProtectionKeyPath,
        IConfiguration configuration,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( connectionString );
        ArgumentException.ThrowIfNullOrWhiteSpace( dataProtectionKeyPath );
        ArgumentNullException.ThrowIfNull( configuration );

        ServiceCollection services = new( );
        _ = services.AddBridgeBeatsApplicationSettingsStore( connectionString, dataProtectionKeyPath );

        await using ServiceProvider provider = services.BuildServiceProvider( );
        IDbContextFactory<ApplicationDbContext> contextFactory = provider
            .GetRequiredService<IDbContextFactory<ApplicationDbContext>>( );
        await using (ApplicationDbContext context = await contextFactory.CreateDbContextAsync( cancellationToken )) {
            await context.Database.MigrateAsync( cancellationToken );
        }

        IApplicationSettingsRuntimeReader reader = provider
            .GetRequiredService<IApplicationSettingsRuntimeReader>( );
        ApplicationSettingsSnapshot? existing = await reader.GetRuntimeSettingsAsync( cancellationToken );
        if (existing is not null || !configuration.GetValue<bool>( SeedSettingsKey )) {
            return existing;
        }

        IApplicationSettingsService service = provider.GetRequiredService<IApplicationSettingsService>( );
        try {
            _ = await service.UpdateAsync(
                ApplicationSettingsSeedLoader.CreateUpdate( configuration ),
                cancellationToken
            );
        } catch (ApplicationSettingsConcurrencyException) {
            // A concurrent startup may have won the singleton insert. The persisted row remains
            // authoritative, so reload it rather than retrying startup configuration as an update.
        }

        return await reader.GetRuntimeSettingsAsync( cancellationToken );
    }
}
