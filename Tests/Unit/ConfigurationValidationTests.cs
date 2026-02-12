using BridgeBeats.Contracts.Interfaces; // Added for IMediaLinkService
using BridgeBeats.Web.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for configuration validation and error handling.
/// Tests edge cases and invalid configurations.
/// </summary>
[TestClass]
public class ConfigurationValidationTests {
    /// <summary>
    /// Verifies that AddBridgeBeatsServices throws <see cref="FileNotFoundException"/> when Apple key file is missing.
    /// </summary>
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
        };
        IServiceCollection services = new ServiceCollection( );
        IConfiguration config = new ConfigurationBuilder( ).AddInMemoryCollection( overrides ).Build( );

        // Act & Assert
        FileNotFoundException ex = Assert.ThrowsExactly<FileNotFoundException>( () => {
            _ = services.AddBridgeBeatsServices( config );
        } );
        Assert.Contains( ".p8", ex.Message );
    }

    /// <summary>
    /// Verifies that AddBridgeBeatsServices throws <see cref="InvalidDataException"/> when Apple key file is empty.
    /// </summary>
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
            };
            IServiceCollection services = new ServiceCollection( );
            IConfiguration config = new ConfigurationBuilder( ).AddInMemoryCollection( overrides ).Build( );

            // Act & Assert
            InvalidDataException ex = Assert.ThrowsExactly<InvalidDataException>( () => {
                _ = services.AddBridgeBeatsServices( config );
            } );
            Assert.Contains( "missing contents", ex.Message );
        } finally {
            if (File.Exists( emptyKeyPath )) { File.Delete( emptyKeyPath ); }
        }
    }

    /// <summary>
    /// Verifies that AddBridgeBeatsServices throws <see cref="InvalidOperationException"/> when no providers are configured.
    /// </summary>
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
        };
        IServiceCollection services = new ServiceCollection( );
        IConfiguration config = new ConfigurationBuilder( ).AddInMemoryCollection( overrides ).Build( );

        // Act & Assert
        InvalidOperationException ex = Assert.ThrowsExactly<InvalidOperationException>( () => {
            _ = services.AddBridgeBeatsServices( config );
        } );
        Assert.Contains( "Required settings are missing", ex.Message );
    }

    /// <summary>
    /// Verifies that AddBridgeBeatsServices succeeds when only Spotify credentials are configured.
    /// </summary>
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
        };

        // Act
        IServiceCollection services = new ServiceCollection( );
        IConfiguration config = new ConfigurationBuilder( ).AddInMemoryCollection( overrides ).Build( );
        _ = services.AddBridgeBeatsServices( config );
        ServiceProvider sp = services.BuildServiceProvider( );

        // Assert
        Assert.IsNotNull( sp );
        IMediaLinkService? mediaService = sp.GetService<IMediaLinkService>();
        Assert.IsNotNull( mediaService, "IMediaLinkService should be registered with Spotify credentials" );
    }

    /// <summary>
    /// Verifies that AddBridgeBeatsServices throws <see cref="InvalidOperationException"/> when card cache expiration hours is zero.
    /// </summary>
    [TestMethod]
    public void AddBridgeBeatsServices_WithZeroCardCacheExpirationHours_ShouldThrowInvalidOperationException( ) {
        // Arrange - valid Spotify credentials but invalid card cache expiration
        Dictionary<string, string?> overrides = new( ) {
            ["BridgeBeats:SpotifyClientId"] = "spotify_client_id",
            ["BridgeBeats:SpotifyClientSecret"] = "spotify_secret",
            ["BridgeBeats:IdentityConnectionString"] = "Data Source=Identity;Mode=Memory",
            ["BridgeBeats:ApiKeySalt"] = "api_key_salt",
            ["BridgeBeats:CardCacheExpirationHours"] = "0",
            ["BridgeBeats:CardCacheCleanupInterval"] = "500",
        };
        IServiceCollection services = new ServiceCollection( );
        IConfiguration config = new ConfigurationBuilder( ).AddInMemoryCollection( overrides ).Build( );

        // Act & Assert
        InvalidOperationException ex = Assert.ThrowsExactly<InvalidOperationException>( () => {
            _ = services.AddBridgeBeatsServices( config );
        } );
        Assert.Contains( "CardCacheExpirationHours must be greater than zero", ex.Message );
    }

    /// <summary>
    /// Verifies that AddBridgeBeatsServices throws <see cref="InvalidOperationException"/> when card cache expiration hours is negative.
    /// </summary>
    [TestMethod]
    public void AddBridgeBeatsServices_WithNegativeCardCacheExpirationHours_ShouldThrowInvalidOperationException( ) {
        // Arrange - valid Spotify credentials but invalid card cache expiration
        Dictionary<string, string?> overrides = new( ) {
            ["BridgeBeats:SpotifyClientId"] = "spotify_client_id",
            ["BridgeBeats:SpotifyClientSecret"] = "spotify_secret",
            ["BridgeBeats:IdentityConnectionString"] = "Data Source=Identity;Mode=Memory",
            ["BridgeBeats:ApiKeySalt"] = "api_key_salt",
            ["BridgeBeats:CardCacheExpirationHours"] = "-1",
            ["BridgeBeats:CardCacheCleanupInterval"] = "500",
        };
        IServiceCollection services = new ServiceCollection( );
        IConfiguration config = new ConfigurationBuilder( ).AddInMemoryCollection( overrides ).Build( );

        // Act & Assert
        InvalidOperationException ex = Assert.ThrowsExactly<InvalidOperationException>( () => {
            _ = services.AddBridgeBeatsServices( config );
        } );
        Assert.Contains( "CardCacheExpirationHours must be greater than zero", ex.Message );
    }

    /// <summary>
    /// Verifies that AddBridgeBeatsServices throws <see cref="InvalidOperationException"/> when card cache cleanup interval is zero.
    /// </summary>
    [TestMethod]
    public void AddBridgeBeatsServices_WithZeroCardCacheCleanupInterval_ShouldThrowInvalidOperationException( ) {
        // Arrange - valid Spotify credentials but invalid cleanup interval
        Dictionary<string, string?> overrides = new( ) {
            ["BridgeBeats:SpotifyClientId"] = "spotify_client_id",
            ["BridgeBeats:SpotifyClientSecret"] = "spotify_secret",
            ["BridgeBeats:IdentityConnectionString"] = "Data Source=Identity;Mode=Memory",
            ["BridgeBeats:ApiKeySalt"] = "api_key_salt",
            ["BridgeBeats:CardCacheExpirationHours"] = "1",
            ["BridgeBeats:CardCacheCleanupInterval"] = "0",
        };
        IServiceCollection services = new ServiceCollection( );
        IConfiguration config = new ConfigurationBuilder( ).AddInMemoryCollection( overrides ).Build( );

        // Act & Assert
        InvalidOperationException ex = Assert.ThrowsExactly<InvalidOperationException>( () => {
            _ = services.AddBridgeBeatsServices( config );
        } );
        Assert.Contains( "CardCacheCleanupInterval must be greater than zero", ex.Message );
    }

    /// <summary>
    /// Verifies that AddBridgeBeatsServices throws <see cref="InvalidOperationException"/> when card cache cleanup interval is negative.
    /// </summary>
    [TestMethod]
    public void AddBridgeBeatsServices_WithNegativeCardCacheCleanupInterval_ShouldThrowInvalidOperationException( ) {
        // Arrange - valid Spotify credentials but invalid cleanup interval
        Dictionary<string, string?> overrides = new( ) {
            ["BridgeBeats:SpotifyClientId"] = "spotify_client_id",
            ["BridgeBeats:SpotifyClientSecret"] = "spotify_secret",
            ["BridgeBeats:IdentityConnectionString"] = "Data Source=Identity;Mode=Memory",
            ["BridgeBeats:ApiKeySalt"] = "api_key_salt",
            ["BridgeBeats:CardCacheExpirationHours"] = "1",
            ["BridgeBeats:CardCacheCleanupInterval"] = "-1",
        };
        IServiceCollection services = new ServiceCollection( );
        IConfiguration config = new ConfigurationBuilder( ).AddInMemoryCollection( overrides ).Build( );

        // Act & Assert
        InvalidOperationException ex = Assert.ThrowsExactly<InvalidOperationException>( () => {
            _ = services.AddBridgeBeatsServices( config );
        } );
        Assert.Contains( "CardCacheCleanupInterval must be greater than zero", ex.Message );
    }
}
