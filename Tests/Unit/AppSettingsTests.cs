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
            ["BridgeBeats:AppleKeyPath"] = "/path/to/key.p8",
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
        Assert.AreEqual( "/path/to/key.p8", settings.AppleKeyPath );
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
    }

    /// <summary>
    /// Verifies that a freshly constructed <see cref="AppSettings"/> defaults its credential string
    /// properties (Apple team/key/path, Spotify client id/secret) to empty strings rather than null.
    /// </summary>
    [TestMethod]
    public void AppSettings_DefaultValues_AreCorrect( ) {
        // Arrange & Act
        AppSettings settings = new();

        // Assert
        Assert.AreEqual( string.Empty, settings.AppleTeamId );
        Assert.AreEqual( string.Empty, settings.AppleKeyId );
        Assert.AreEqual( string.Empty, settings.AppleKeyPath );
        Assert.AreEqual( string.Empty, settings.SpotifyClientId );
        Assert.AreEqual( string.Empty, settings.SpotifyClientSecret );
    }

    /// <summary>
    /// Verifies that the <see cref="AppSettings.Domain"/> property defaults to an empty string on a
    /// newly constructed instance.
    /// </summary>
    [TestMethod]
    public void AppSettings_Domain_DefaultsToEmpty( ) {
        // Arrange & Act
        AppSettings settings = new();

        // Assert
        Assert.AreEqual( string.Empty, settings.Domain );
    }
}
