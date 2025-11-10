using Microsoft.Extensions.DependencyInjection;
using TuneBridge.Domain.Interfaces; // Added for IMediaLinkService
using TuneBridge.Tests.EndToEnd;

namespace TuneBridge.Tests.Unit;

/// <summary>
/// Unit tests for configuration validation and error handling.
/// Tests edge cases and invalid configurations.
/// </summary>
[TestClass]
public class ConfigurationValidationTests {
    [TestMethod]
    public void AddTuneBridgeServices_WithMissingAppleKeyFile_ShouldThrowFileNotFoundException( ) {
        // Arrange - provide Apple credentials with missing key file
        Dictionary<string, string?> overrides = new( ) {
            ["TuneBridge:AppleTeamId"] = "TEAM123456",
            ["TuneBridge:AppleKeyId"] = "KEY1234567",
            ["TuneBridge:AppleKeyPath"] = "/nonexistent/path/key.p8",
            ["TuneBridge:SpotifyClientId"] = string.Empty,
            ["TuneBridge:SpotifyClientSecret"] = string.Empty,
            ["TuneBridge:ConnectionString"] = "Data Source=Identity;Mode=Memory;Cache=Shared",
            ["TuneBridge:ApiKeySalt"] = "api_key_salt",
            ["TuneBridge:BlueskyPdsUrl"] = string.Empty,
            ["TuneBridge:BlueskyIdentifier"] = string.Empty,
            ["TuneBridge:BlueskyPassword"] = string.Empty,
            ["TuneBridge:CacheDbPath"] = "Data Source=LinkCache;Mode=Memory;Cache=Shared",
        };

        // Act & Assert
        FileNotFoundException ex = Assert.ThrowsException<FileNotFoundException>( () => {
            using CustomWebApplicationFactory factory = new( overrides );
            _ = factory.Services; // trigger creation
        } );
        Assert.IsTrue( ex.Message.Contains( ".p8" ) );
    }

    [TestMethod]
    public void AddTuneBridgeServices_WithEmptyAppleKeyFile_ShouldThrowInvalidDataException( ) {
        // Arrange - create empty temp key file
        string emptyKeyPath = Path.Combine( Path.GetTempPath( ), $"empty_key_{Guid.NewGuid()}.p8" );
        File.WriteAllText( emptyKeyPath, string.Empty );

        try {
            Dictionary<string, string?> overrides = new( ) {
                ["TuneBridge:AppleTeamId"] = "TEAM123456",
                ["TuneBridge:AppleKeyId"] = "KEY1234567",
                ["TuneBridge:AppleKeyPath"] = emptyKeyPath,
                ["TuneBridge:SpotifyClientId"] = string.Empty,
                ["TuneBridge:SpotifyClientSecret"] = string.Empty,
                ["TuneBridge:ConnectionString"] = "Data Source=Identity;Mode=Memory;Cache=Shared",
                ["TuneBridge:ApiKeySalt"] = "api_key_salt",
                ["TuneBridge:BlueskyPdsUrl"] = string.Empty,
                ["TuneBridge:BlueskyIdentifier"] = string.Empty,
                ["TuneBridge:BlueskyPassword"] = string.Empty,
                ["TuneBridge:CacheDbPath"] = "Data Source=LinkCache;Mode=Memory;Cache=Shared",
            };

            // Act & Assert
            InvalidDataException ex = Assert.ThrowsException<InvalidDataException>( () => {
                using CustomWebApplicationFactory factory = new( overrides );
                _ = factory.Services;
            } );
            Assert.IsTrue( ex.Message.Contains( "missing contents" ) );
        } finally {
            if (File.Exists( emptyKeyPath )) { File.Delete( emptyKeyPath ); }
        }
    }

    [TestMethod]
    public void AddTuneBridgeServices_WithNoProviders_ShouldThrowInvalidOperationException( ) {
        // Arrange - neither Apple nor Spotify nor Tidal credentials
        Dictionary<string, string?> overrides = new( ) {
            ["TuneBridge:AppleTeamId"] = string.Empty,
            ["TuneBridge:AppleKeyId"] = string.Empty,
            ["TuneBridge:AppleKeyPath"] = string.Empty,
            ["TuneBridge:SpotifyClientId"] = string.Empty,
            ["TuneBridge:SpotifyClientSecret"] = string.Empty,
            ["TuneBridge:DiscordToken"] = string.Empty,
            ["TuneBridge:ConnectionString"] = "Data Source=Identity;Mode=Memory",
            ["TuneBridge:ApiKeySalt"] = "api_key_salt",
            ["TuneBridge:BlueskyPdsUrl"] = string.Empty,
            ["TuneBridge:BlueskyIdentifier"] = string.Empty,
            ["TuneBridge:BlueskyPassword"] = string.Empty,
            ["TuneBridge:CacheDbPath"] = "Data Source=LinkCache;Mode=Memory;Cache=Shared",
        };

        // Act & Assert
        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>( () => {
            using CustomWebApplicationFactory factory = new( overrides );
            _ = factory.Services;
        } );
        Assert.IsTrue( ex.Message.Contains( "Required settings are missing" ) );
    }

    [TestMethod]
    public void AddTuneBridgeServices_WithOnlySpotifyCredentials_ShouldSucceed( ) {
        // Arrange - minimal Spotify config
        Dictionary<string, string?> overrides = new( ) {
            ["TuneBridge:NodeNumber"] = "1",
            ["TuneBridge:AppleTeamId"] = string.Empty,
            ["TuneBridge:AppleKeyId"] = string.Empty,
            ["TuneBridge:AppleKeyPath"] = string.Empty,
            ["TuneBridge:SpotifyClientId"] = "spotify_client_id",
            ["TuneBridge:SpotifyClientSecret"] = "spotify_secret",
            ["TuneBridge:DiscordToken"] = string.Empty,
            ["TuneBridge:ConnectionString"] = "Data Source=TuneBridge;Mode=Memory",
            ["TuneBridge:ApiKeySalt"] = "api_key_salt",
            ["TuneBridge:BlueskyPdsUrl"] = string.Empty,
            ["TuneBridge:BlueskyIdentifier"] = string.Empty,
            ["TuneBridge:BlueskyPassword"] = string.Empty,
            ["TuneBridge:CacheDbPath"] = "Data Source=TuneBridge;Mode=Memory",
        };

        // Act
        using CustomWebApplicationFactory factory = new( overrides );
        IServiceProvider sp = factory.Services;

        // Assert
        Assert.IsNotNull( sp );
        IMediaLinkService? mediaService = sp.GetService<IMediaLinkService>();
        Assert.IsNotNull( mediaService, "IMediaLinkService should be registered with Spotify credentials" );
    }
}
