using BridgeBeats.Web.Configuration;
using Microsoft.Extensions.Configuration;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for AppSettings configuration binding and validation.
/// </summary>
[TestClass]
public class AppSettingsTests {
    [TestMethod]
    public void AppSettings_BindsCorrectly_FromConfiguration( ) {
        // Arrange
        Dictionary<string, string?> configData = new( ) {
            ["BridgeBeats:NodeNumber"] = "5",
            ["BridgeBeats:AppleTeamId"] = "TEAM123456",
            ["BridgeBeats:AppleKeyId"] = "KEY1234567",
            ["BridgeBeats:AppleKeyPath"] = "/path/to/key.p8",
            ["BridgeBeats:SpotifyClientId"] = "spotify_client_id",
            ["BridgeBeats:SpotifyClientSecret"] = "spotify_secret",
            ["BridgeBeats:TidalClientId"] = "tidal_client_id",
            ["BridgeBeats:TidalClientSecret"] = "tidal_secret",
            ["BridgeBeats:DiscordToken"] = "discord_token_here",
            ["BridgeBeats:IdentityConnectionString"] = "Data Source=bridgebeats.db",
            ["BridgeBeats:ApiKeySalt"] = "api_key_salt",
            ["BridgeBeats:RateLimitRequestsPerHour"] = "10",
            ["BridgeBeats:ATProtoIdentifier"] = string.Empty,
            ["BridgeBeats:ATProtoPassword"] = string.Empty,
            ["BridgeBeats:ATProtoUserDID"] = string.Empty,
            ["BridgeBeats:CacheDays"] = "7",
            ["BridgeBeats:LinkCacheConnectionString"] = "Data Source=bridgebeats.db",
            ["BridgeBeats:BaseUrl"] = "localhost",
            ["BridgeBeats:LogFilePath"] = "./logs/bridgebeats-.log",
        };

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Act
        AppSettings settings = new();
        configuration.GetRequiredSection( "BridgeBeats" ).Bind( settings );

        // Assert
        Assert.AreEqual( 5, settings.NodeNumber );
        Assert.AreEqual( "TEAM123456", settings.AppleTeamId );
        Assert.AreEqual( "KEY1234567", settings.AppleKeyId );
        Assert.AreEqual( "/path/to/key.p8", settings.AppleKeyPath );
        Assert.AreEqual( "spotify_client_id", settings.SpotifyClientId );
        Assert.AreEqual( "spotify_secret", settings.SpotifyClientSecret );
        Assert.AreEqual( "tidal_client_id", settings.TidalClientId );
        Assert.AreEqual( "tidal_secret", settings.TidalClientSecret );
        Assert.AreEqual( "discord_token_here", settings.DiscordToken );
        Assert.AreEqual( "Data Source=bridgebeats.db", settings.IdentityConnectionString );
        Assert.AreEqual( "api_key_salt", settings.ApiKeySalt );
        Assert.AreEqual( 10, settings.RateLimitRequestsPerHour );
        Assert.AreEqual( string.Empty, settings.ATProtoIdentifier );
        Assert.AreEqual( string.Empty, settings.ATProtoPassword );
        Assert.AreEqual( string.Empty, settings.ATProtoUserDID );
        Assert.AreEqual( 7, settings.CacheDays );
        Assert.AreEqual( "Data Source=bridgebeats.db", settings.LinkCacheConnectionString );
        Assert.AreEqual( "localhost", settings.BaseUrl );
        Assert.AreEqual( "./logs/bridgebeats-.log", settings.LogFilePath );
    }

    [TestMethod]
    public void AppSettings_DefaultValues_AreCorrect( ) {
        // Arrange & Act
        AppSettings settings = new();

        // Assert
        Assert.AreEqual( 0, settings.NodeNumber );
        Assert.AreEqual( string.Empty, settings.AppleTeamId );
        Assert.AreEqual( string.Empty, settings.AppleKeyId );
        Assert.AreEqual( string.Empty, settings.AppleKeyPath );
        Assert.AreEqual( string.Empty, settings.SpotifyClientId );
        Assert.AreEqual( string.Empty, settings.SpotifyClientSecret );
        Assert.AreEqual( string.Empty, settings.DiscordToken );
    }

    [TestMethod]
    public void AppSettings_NodeNumber_CanBeZero( ) {
        // Arrange
        Dictionary<string, string?> configData = new( ) {
            ["BridgeBeats:NodeNumber"] = "0"
        };

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Act
        AppSettings settings = new();
        configuration.GetRequiredSection( "BridgeBeats" ).Bind( settings );

        // Assert
        Assert.AreEqual( 0, settings.NodeNumber );
    }
}
