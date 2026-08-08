using BridgeBeats.Web.Configuration;
using Microsoft.Extensions.Configuration;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Tests configuration binding and default values for <see cref="AppSettings"/>, the strongly typed
/// view of the <c>BridgeBeats</c> configuration section.
/// </summary>
[TestClass]
public class AppSettingsTests {
    /// <summary>
    /// Verifies that binding an in-memory configuration under the <c>BridgeBeats</c> section populates
    /// every <see cref="AppSettings"/> property with the configured value, including the numeric
    /// <c>RateLimitRequestsPerHour</c> and <c>CacheDays</c> and the empty-string ATProto fields.
    /// </summary>
    [TestMethod]
    public void AppSettings_BindsCorrectly_FromConfiguration( ) {
        // Arrange
        Dictionary<string, string?> configData = new( ) {
            ["BridgeBeats:AppleTeamId"] = "TEAM123456",
            ["BridgeBeats:AppleKeyId"] = "KEY1234567",
            ["BridgeBeats:ApplePrivateKey"] = "test-private-key",
            ["BridgeBeats:SpotifyClientId"] = "spotify_client_id",
            ["BridgeBeats:SpotifyClientSecret"] = "spotify_secret",
            ["BridgeBeats:TidalClientId"] = "tidal_client_id",
            ["BridgeBeats:TidalClientSecret"] = "tidal_secret",
            ["BridgeBeats:IdentityConnectionString"] = "Data Source=bridgebeats.db",
            ["BridgeBeats:ApiKeySalt"] = "api_key_salt",
            ["BridgeBeats:RateLimitRequestsPerHour"] = "10",
            ["BridgeBeats:ATProtoIdentifier"] = string.Empty,
            ["BridgeBeats:ATProtoPassword"] = string.Empty,
            ["BridgeBeats:ATProtoUserDID"] = string.Empty,
            ["BridgeBeats:CacheDays"] = "7",
            ["BridgeBeats:Domain"] = "localhost",
            ["BridgeBeats:LogDirPath"] = "./logs",
            ["BridgeBeats:SettingsRevision"] = "f6ab8a9e5a734a99ba51cc7bf35fd2b1",
        };

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Act
        AppSettings settings = new();
        configuration.GetRequiredSection( "BridgeBeats" ).Bind( settings );

        // Assert
        Assert.AreEqual( "TEAM123456", settings.AppleTeamId );
        Assert.AreEqual( "KEY1234567", settings.AppleKeyId );
        Assert.AreEqual( "test-private-key", settings.ApplePrivateKey );
        Assert.AreEqual( "spotify_client_id", settings.SpotifyClientId );
        Assert.AreEqual( "spotify_secret", settings.SpotifyClientSecret );
        Assert.AreEqual( "tidal_client_id", settings.TidalClientId );
        Assert.AreEqual( "tidal_secret", settings.TidalClientSecret );
        Assert.AreEqual( "Data Source=bridgebeats.db", settings.IdentityConnectionString );
        Assert.AreEqual( "api_key_salt", settings.ApiKeySalt );
        Assert.AreEqual( 10, settings.RateLimitRequestsPerHour );
        Assert.AreEqual( string.Empty, settings.ATProtoIdentifier );
        Assert.AreEqual( string.Empty, settings.ATProtoPassword );
        Assert.AreEqual( string.Empty, settings.ATProtoUserDID );
        Assert.AreEqual( 7, settings.CacheDays );
        Assert.AreEqual( "localhost", settings.Domain );
        Assert.AreEqual( "./logs", settings.LogDirPath );
        Assert.AreEqual( "f6ab8a9e5a734a99ba51cc7bf35fd2b1", settings.SettingsRevision );
    }

    /// <summary>
    /// Verifies that a freshly constructed <see cref="AppSettings"/> defaults its credential string
    /// properties (Apple team/key/private key, Spotify client id/secret) to empty strings rather than null.
    /// </summary>
    [TestMethod]
    public void AppSettings_DefaultValues_AreCorrect( ) {
        // Arrange & Act
        AppSettings settings = new();

        // Assert
        Assert.AreEqual( string.Empty, settings.AppleTeamId );
        Assert.AreEqual( string.Empty, settings.AppleKeyId );
        Assert.AreEqual( string.Empty, settings.ApplePrivateKey );
        Assert.AreEqual( string.Empty, settings.SpotifyClientId );
        Assert.AreEqual( string.Empty, settings.SpotifyClientSecret );
    }

    /// <summary>
    /// Verifies standalone development preserves the historical localhost domain default.
    /// </summary>
    [TestMethod]
    public void AppSettings_Domain_DefaultsToLocalhost( ) {
        // Arrange & Act
        AppSettings settings = new();

        // Assert
        Assert.AreEqual( "localhost", settings.Domain );
    }

    /// <summary>Verifies setup mode can project an empty revision without a numeric conversion failure.</summary>
    [TestMethod]
    public void AppSettings_EmptyRevision_BindsAsString( ) {
        IConfiguration configuration = new ConfigurationBuilder( )
            .AddInMemoryCollection( new Dictionary<string, string?> {
                ["BridgeBeats:SettingsRevision"] = string.Empty
            } )
            .Build( );
        AppSettings settings = new( );

        configuration.GetRequiredSection( "BridgeBeats" ).Bind( settings );

        Assert.AreEqual( string.Empty, settings.SettingsRevision );
    }
}
