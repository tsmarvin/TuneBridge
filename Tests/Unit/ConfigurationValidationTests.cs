using BridgeBeats.Configuration;
using BridgeBeats.Domain.Interfaces; // Added for IMediaLinkService
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for configuration validation and error handling.
/// Tests edge cases and invalid configurations.
/// </summary>
[TestClass]
public class ConfigurationValidationTests {
    [TestMethod]
    public void AddBridgeBeatsServices_WithMissingAppleKeyFile_ShouldThrowFileNotFoundException( ) {
        // Arrange - provide ALL Apple credentials (TeamId, KeyId, AND KeyPath) with missing key file
        // This ensures we hit the file validation logic
        Dictionary<string, string?> overrides = new( ) {
            ["BridgeBeats:AppleTeamId"] = "TEAM123456",
            ["BridgeBeats:AppleKeyId"] = "KEY1234567",
            ["BridgeBeats:AppleKeyPath"] = "/nonexistent/path/key.p8",
            ["BridgeBeats:SpotifyClientId"] = string.Empty,
            ["BridgeBeats:SpotifyClientSecret"] = string.Empty,
            ["BridgeBeats:TidalClientId"] = string.Empty,
            ["BridgeBeats:TidalClientSecret"] = string.Empty,
            ["BridgeBeats:DiscordToken"] = string.Empty,
            ["BridgeBeats:IdentityConnectionString"] = "Data Source=Identity;Mode=Memory;Cache=Shared",
            ["BridgeBeats:ApiKeySalt"] = "api_key_salt",
            ["BridgeBeats:ATProtoIdentifier"] = string.Empty,
            ["BridgeBeats:ATProtoPassword"] = string.Empty,
            ["BridgeBeats:LinkCacheConnectionString"] = "Data Source=LinkCache;Mode=Memory;Cache=Shared",
        };

        // Act & Assert
        FileNotFoundException ex = Assert.ThrowsExactly<FileNotFoundException>( () => {
            IServiceCollection services = new ServiceCollection( );
            IConfiguration config = new ConfigurationBuilder( ).AddInMemoryCollection( overrides ).Build( );
            _ = services.AddBridgeBeatsServices( config, "Testing" );
        } );
        Assert.Contains( ".p8", ex.Message );
    }

    [TestMethod]
    public void AddBridgeBeatsServices_WithEmptyAppleKeyFile_ShouldThrowInvalidDataException( ) {
        // Arrange - create empty temp key file
        string emptyKeyPath = Path.Combine( Path.GetTempPath( ), $"empty_key_{Guid.NewGuid()}.p8" );
        File.WriteAllText( emptyKeyPath, string.Empty );

        try {
            Dictionary<string, string?> overrides = new( ) {
                ["BridgeBeats:AppleTeamId"] = "TEAM123456",
                ["BridgeBeats:AppleKeyId"] = "KEY1234567",
                ["BridgeBeats:AppleKeyPath"] = emptyKeyPath,
                ["BridgeBeats:SpotifyClientId"] = string.Empty,
                ["BridgeBeats:SpotifyClientSecret"] = string.Empty,
                ["BridgeBeats:TidalClientId"] = string.Empty,
                ["BridgeBeats:TidalClientSecret"] = string.Empty,
                ["BridgeBeats:DiscordToken"] = string.Empty,
                ["BridgeBeats:IdentityConnectionString"] = "Data Source=Identity;Mode=Memory;Cache=Shared",
                ["BridgeBeats:ApiKeySalt"] = "api_key_salt",
                ["BridgeBeats:ATProtoIdentifier"] = string.Empty,
                ["BridgeBeats:ATProtoPassword"] = string.Empty,
                ["BridgeBeats:LinkCacheConnectionString"] = "Data Source=LinkCache;Mode=Memory;Cache=Shared",
            };

            // Act & Assert
            InvalidDataException ex = Assert.ThrowsExactly<InvalidDataException>( () => {
                IServiceCollection services = new ServiceCollection( );
                IConfiguration config = new ConfigurationBuilder( ).AddInMemoryCollection( overrides! ).Build( );
                _ = services.AddBridgeBeatsServices( config, "Testing" );
            } );
            Assert.Contains( "missing contents", ex.Message );
        } finally {
            if (File.Exists( emptyKeyPath )) { File.Delete( emptyKeyPath ); }
        }
    }

    [TestMethod]
    public void AddBridgeBeatsServices_WithNoProviders_ShouldThrowInvalidOperationException( ) {
        // Arrange - no Apple, Spotify, or Tidal credentials
        // Ensure Discord is also disabled so we properly hit the provider validation
        Dictionary<string, string?> overrides = new( ) {
            ["BridgeBeats:AppleTeamId"] = string.Empty,
            ["BridgeBeats:AppleKeyId"] = string.Empty,
            ["BridgeBeats:AppleKeyPath"] = string.Empty,
            ["BridgeBeats:SpotifyClientId"] = string.Empty,
            ["BridgeBeats:SpotifyClientSecret"] = string.Empty,
            ["BridgeBeats:TidalClientId"] = string.Empty,
            ["BridgeBeats:TidalClientSecret"] = string.Empty,
            ["BridgeBeats:DiscordToken"] = string.Empty,
            ["BridgeBeats:IdentityConnectionString"] = "Data Source=Identity;Mode=Memory",
            ["BridgeBeats:ApiKeySalt"] = "api_key_salt",
            ["BridgeBeats:ATProtoIdentifier"] = string.Empty,
            ["BridgeBeats:ATProtoPassword"] = string.Empty,
            ["BridgeBeats:LinkCacheConnectionString"] = "Data Source=LinkCache;Mode=Memory;Cache=Shared",
        };

        // Act & Assert
        InvalidOperationException ex = Assert.ThrowsExactly<InvalidOperationException>( () => {
            IServiceCollection services = new ServiceCollection( );
            IConfiguration config = new ConfigurationBuilder( ).AddInMemoryCollection( overrides! ).Build( );
            _ = services.AddBridgeBeatsServices( config, "Testing" );
        } );
        Assert.Contains( "Required settings are missing", ex.Message );
    }

    [TestMethod]
    public void AddBridgeBeatsServices_WithOnlySpotifyCredentials_ShouldSucceed( ) {
        // Arrange - minimal Spotify config
        Dictionary<string, string?> overrides = new( ) {
            ["BridgeBeats:NodeNumber"] = "1",
            ["BridgeBeats:AppleTeamId"] = string.Empty,
            ["BridgeBeats:AppleKeyId"] = string.Empty,
            ["BridgeBeats:AppleKeyPath"] = string.Empty,
            ["BridgeBeats:SpotifyClientId"] = "spotify_client_id",
            ["BridgeBeats:SpotifyClientSecret"] = "spotify_secret",
            ["BridgeBeats:TidalClientId"] = string.Empty,
            ["BridgeBeats:TidalClientSecret"] = string.Empty,
            ["BridgeBeats:DiscordToken"] = string.Empty,
            ["BridgeBeats:IdentityConnectionString"] = "Data Source=BridgeBeats;Mode=Memory",
            ["BridgeBeats:ApiKeySalt"] = "api_key_salt",
            ["BridgeBeats:ATProtoIdentifier"] = string.Empty,
            ["BridgeBeats:ATProtoPassword"] = string.Empty,
            ["BridgeBeats:LinkCacheConnectionString"] = "Data Source=BridgeBeats;Mode=Memory",
        };

        // Act
        IServiceCollection services = new ServiceCollection( );
        IConfiguration config = new ConfigurationBuilder( ).AddInMemoryCollection( overrides! ).Build( );
        _ = services.AddBridgeBeatsServices( config, "Testing" );
        ServiceProvider sp = services.BuildServiceProvider( );

        // Assert
        Assert.IsNotNull( sp );
        IMediaLinkService? mediaService = sp.GetService<IMediaLinkService>();
        Assert.IsNotNull( mediaService, "IMediaLinkService should be registered with Spotify credentials" );
    }

    [TestMethod]
    public void AddBridgeBeatsServices_WithZeroCardCacheExpirationHours_ShouldThrowInvalidOperationException( ) {
        // Arrange - valid Spotify credentials but invalid card cache expiration
        Dictionary<string, string?> overrides = new( ) {
            ["BridgeBeats:SpotifyClientId"] = "spotify_client_id",
            ["BridgeBeats:SpotifyClientSecret"] = "spotify_secret",
            ["BridgeBeats:IdentityConnectionString"] = "Data Source=Identity;Mode=Memory",
            ["BridgeBeats:ApiKeySalt"] = "api_key_salt",
            ["BridgeBeats:LinkCacheConnectionString"] = "Data Source=LinkCache;Mode=Memory",
            ["BridgeBeats:CardCacheExpirationHours"] = "0",
            ["BridgeBeats:CardCacheCleanupInterval"] = "500",
        };

        // Act & Assert
        InvalidOperationException ex = Assert.ThrowsExactly<InvalidOperationException>( () => {
            IServiceCollection services = new ServiceCollection( );
            IConfiguration config = new ConfigurationBuilder( ).AddInMemoryCollection( overrides! ).Build( );
            _ = services.AddBridgeBeatsServices( config, "Testing" );
        } );
        Assert.Contains( "CardCacheExpirationHours must be greater than zero", ex.Message );
    }

    [TestMethod]
    public void AddBridgeBeatsServices_WithNegativeCardCacheExpirationHours_ShouldThrowInvalidOperationException( ) {
        // Arrange - valid Spotify credentials but invalid card cache expiration
        Dictionary<string, string?> overrides = new( ) {
            ["BridgeBeats:SpotifyClientId"] = "spotify_client_id",
            ["BridgeBeats:SpotifyClientSecret"] = "spotify_secret",
            ["BridgeBeats:IdentityConnectionString"] = "Data Source=Identity;Mode=Memory",
            ["BridgeBeats:ApiKeySalt"] = "api_key_salt",
            ["BridgeBeats:LinkCacheConnectionString"] = "Data Source=LinkCache;Mode=Memory",
            ["BridgeBeats:CardCacheExpirationHours"] = "-1",
            ["BridgeBeats:CardCacheCleanupInterval"] = "500",
        };

        // Act & Assert
        InvalidOperationException ex = Assert.ThrowsExactly<InvalidOperationException>( () => {
            IServiceCollection services = new ServiceCollection( );
            IConfiguration config = new ConfigurationBuilder( ).AddInMemoryCollection( overrides! ).Build( );
            _ = services.AddBridgeBeatsServices( config, "Testing" );
        } );
        Assert.Contains( "CardCacheExpirationHours must be greater than zero", ex.Message );
    }

    [TestMethod]
    public void AddBridgeBeatsServices_WithZeroCardCacheCleanupInterval_ShouldThrowInvalidOperationException( ) {
        // Arrange - valid Spotify credentials but invalid cleanup interval
        Dictionary<string, string?> overrides = new( ) {
            ["BridgeBeats:SpotifyClientId"] = "spotify_client_id",
            ["BridgeBeats:SpotifyClientSecret"] = "spotify_secret",
            ["BridgeBeats:IdentityConnectionString"] = "Data Source=Identity;Mode=Memory",
            ["BridgeBeats:ApiKeySalt"] = "api_key_salt",
            ["BridgeBeats:LinkCacheConnectionString"] = "Data Source=LinkCache;Mode=Memory",
            ["BridgeBeats:CardCacheExpirationHours"] = "1",
            ["BridgeBeats:CardCacheCleanupInterval"] = "0",
        };

        // Act & Assert
        InvalidOperationException ex = Assert.ThrowsExactly<InvalidOperationException>( () => {
            IServiceCollection services = new ServiceCollection( );
            IConfiguration config = new ConfigurationBuilder( ).AddInMemoryCollection( overrides! ).Build( );
            _ = services.AddBridgeBeatsServices( config, "Testing" );
        } );
        Assert.Contains( "CardCacheCleanupInterval must be greater than zero", ex.Message );
    }

    [TestMethod]
    public void AddBridgeBeatsServices_WithNegativeCardCacheCleanupInterval_ShouldThrowInvalidOperationException( ) {
        // Arrange - valid Spotify credentials but invalid cleanup interval
        Dictionary<string, string?> overrides = new( ) {
            ["BridgeBeats:SpotifyClientId"] = "spotify_client_id",
            ["BridgeBeats:SpotifyClientSecret"] = "spotify_secret",
            ["BridgeBeats:IdentityConnectionString"] = "Data Source=Identity;Mode=Memory",
            ["BridgeBeats:ApiKeySalt"] = "api_key_salt",
            ["BridgeBeats:LinkCacheConnectionString"] = "Data Source=LinkCache;Mode=Memory",
            ["BridgeBeats:CardCacheExpirationHours"] = "1",
            ["BridgeBeats:CardCacheCleanupInterval"] = "-1",
        };

        // Act & Assert
        InvalidOperationException ex = Assert.ThrowsExactly<InvalidOperationException>( () => {
            IServiceCollection services = new ServiceCollection( );
            IConfiguration config = new ConfigurationBuilder( ).AddInMemoryCollection( overrides! ).Build( );
            _ = services.AddBridgeBeatsServices( config, "Testing" );
        } );
        Assert.Contains( "CardCacheCleanupInterval must be greater than zero", ex.Message );
    }
}
