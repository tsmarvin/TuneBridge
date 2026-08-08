using System.Text.Json;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Extensions;
using BridgeBeats.Worker.Spotify;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BridgeBeats.Tests.Unit;

/// <summary>Tests distribution of the authoritative queue settings snapshot.</summary>
[TestClass]
public sealed class QueueSettingsSnapshotTests {
    /// <summary>Database and worker defaults must remain identical across their assembly boundary.</summary>
    [TestMethod]
    public void SpotifyBatchSettings_DatabaseAndWorkerDefaultsRemainInParity( ) {
        ApplicationSpotifyBatchSettings database = new( );
        SpotifyBatchSettings worker = new( );

        Assert.AreEqual( database.LingerMs, worker.LingerMs );
        Assert.AreEqual( database.RequestFailureCooldownSeconds, worker.RequestFailureCooldownSeconds );
    }

    /// <summary>Verifies nested values and provider dictionaries survive process projection.</summary>
    [TestMethod]
    public void AddQueueSettingsSnapshot_RegistersExactDatabaseSnapshot( ) {
        QueueSettings expected = new( ) {
            InteractiveWaitSeconds = 41,
            ProviderConcurrency = new Dictionary<SupportedProviders, int> {
                [SupportedProviders.Spotify] = 7
            }
        };
        IConfiguration configuration = new ConfigurationBuilder( )
            .AddInMemoryCollection( new Dictionary<string, string?> {
                ["BridgeBeats:QueueSnapshot"] = JsonSerializer.Serialize( expected )
            } )
            .Build( );
        ServiceCollection services = new( );

        _ = services.AddQueueSettingsSnapshot( configuration );

        using ServiceProvider provider = services.BuildServiceProvider( );
        QueueSettings actual = provider.GetRequiredService<IOptions<QueueSettings>>( ).Value;
        Assert.AreEqual( expected.InteractiveWaitSeconds, actual.InteractiveWaitSeconds );
        Assert.AreEqual( 7, actual.ProviderConcurrency[SupportedProviders.Spotify] );
    }

    /// <summary>Verifies camel-case database JSON populates the worker's Pascal-case settings type.</summary>
    [TestMethod]
    public void AddSettingsSnapshot_RegistersNonDefaultSpotifyValues( ) {
        IConfiguration configuration = new ConfigurationBuilder( )
            .AddInMemoryCollection( new Dictionary<string, string?> {
                ["BridgeBeats:SpotifyBatchSnapshot"] = """
                    {"lingerMs":12345,"requestFailureCooldownSeconds":17}
                    """
            } )
            .Build( );
        ServiceCollection services = new( );

        _ = services.AddSettingsSnapshot<SpotifyBatchSettings>(
            configuration,
            "BridgeBeats:SpotifyBatchSnapshot",
            SpotifyBatchSettings.SectionKey
        );

        using ServiceProvider provider = services.BuildServiceProvider( );
        SpotifyBatchSettings actual = provider.GetRequiredService<IOptions<SpotifyBatchSettings>>( ).Value;
        Assert.AreEqual( 12345, actual.LingerMs );
        Assert.AreEqual( 17, actual.RequestFailureCooldownSeconds );
    }

    /// <summary>Verifies standalone hosts retain ordinary configuration binding as a test seam.</summary>
    [TestMethod]
    public void AddSettingsSnapshot_WithoutSnapshot_BindsFallbackSection( ) {
        IConfiguration configuration = new ConfigurationBuilder( )
            .AddInMemoryCollection( new Dictionary<string, string?> {
                [$"{SpotifyBatchSettings.SectionKey}:LingerMs"] = "4567"
            } )
            .Build( );
        ServiceCollection services = new( );

        _ = services.AddSettingsSnapshot<SpotifyBatchSettings>(
            configuration,
            "BridgeBeats:SpotifyBatchSnapshot",
            SpotifyBatchSettings.SectionKey
        );

        using ServiceProvider provider = services.BuildServiceProvider( );
        Assert.AreEqual(
            4567,
            provider.GetRequiredService<IOptions<SpotifyBatchSettings>>( ).Value.LingerMs
        );
    }

    /// <summary>Verifies malformed projected JSON fails fast with the projection key redacted from values.</summary>
    [TestMethod]
    public void AddSettingsSnapshot_WithMalformedJson_Throws( ) {
        IConfiguration configuration = new ConfigurationBuilder( )
            .AddInMemoryCollection( new Dictionary<string, string?> {
                ["BridgeBeats:SpotifyBatchSnapshot"] = "{not-json"
            } )
            .Build( );
        ServiceCollection services = new( );

        InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>( ( ) =>
            services.AddSettingsSnapshot<SpotifyBatchSettings>(
                configuration,
                "BridgeBeats:SpotifyBatchSnapshot",
                SpotifyBatchSettings.SectionKey
            )
        );

        Assert.Contains( "BridgeBeats:SpotifyBatchSnapshot", exception.Message );
        Assert.DoesNotContain( "not-json", exception.Message );
    }
}
