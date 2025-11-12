using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TuneBridge.Configuration;
using TuneBridge.Domain.Interfaces; // Added for IMediaLinkService

namespace TuneBridge.Tests.Unit;

/// <summary>
/// Unit tests for configuration validation and error handling.
/// Tests edge cases and invalid configurations.
/// </summary>
[TestClass]
public class ConfigurationValidationTests {
    [TestMethod]
    public void AddTuneBridgeServices_WithMissingAppleKeyFile_ShouldThrowFileNotFoundException( ) {
        // Arrange - provide ALL Apple credentials (TeamId, KeyId, AND KeyPath) with missing key file
        // This ensures we hit the file validation logic
        Dictionary<string, string?> overrides = new( ) {
            ["TuneBridge:AppleTeamId"] = "TEAM123456",
            ["TuneBridge:AppleKeyId"] = "KEY1234567",
            ["TuneBridge:AppleKeyPath"] = "/nonexistent/path/key.p8",
            ["TuneBridge:SpotifyClientId"] = string.Empty,
            ["TuneBridge:SpotifyClientSecret"] = string.Empty,
            ["TuneBridge:TidalClientId"] = string.Empty,
            ["TuneBridge:TidalClientSecret"] = string.Empty,
            ["TuneBridge:DiscordToken"] = string.Empty,
            ["TuneBridge:IdentityConnectionString"] = "Data Source=Identity;Mode=Memory;Cache=Shared",
            ["TuneBridge:ApiKeySalt"] = "api_key_salt",
            ["TuneBridge:BlueskyPdsUrl"] = string.Empty,
            ["TuneBridge:BlueskyIdentifier"] = string.Empty,
            ["TuneBridge:BlueskyPassword"] = string.Empty,
            ["TuneBridge:LinkCacheConnectionString"] = "Data Source=LinkCache;Mode=Memory;Cache=Shared",
        };

        // Act & Assert
        FileNotFoundException ex = Assert.ThrowsExactly<FileNotFoundException>( () => {
            IServiceCollection services = new ServiceCollection( );
            IConfiguration config = new ConfigurationBuilder( ).AddInMemoryCollection( overrides! ).Build( );
            FakeWebHostBuilder builder = new( );
            _ = builder.ConfigureTuneBridgeServices( services, config );
        } );
        Assert.Contains( ".p8", ex.Message );
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
                ["TuneBridge:TidalClientId"] = string.Empty,
                ["TuneBridge:TidalClientSecret"] = string.Empty,
                ["TuneBridge:DiscordToken"] = string.Empty,
                ["TuneBridge:IdentityConnectionString"] = "Data Source=Identity;Mode=Memory;Cache=Shared",
                ["TuneBridge:ApiKeySalt"] = "api_key_salt",
                ["TuneBridge:BlueskyPdsUrl"] = string.Empty,
                ["TuneBridge:BlueskyIdentifier"] = string.Empty,
                ["TuneBridge:BlueskyPassword"] = string.Empty,
                ["TuneBridge:LinkCacheConnectionString"] = "Data Source=LinkCache;Mode=Memory;Cache=Shared",
            };

            // Act & Assert
            InvalidDataException ex = Assert.ThrowsExactly<InvalidDataException>( () => {
                IServiceCollection services = new ServiceCollection( );
                IConfiguration config = new ConfigurationBuilder( ).AddInMemoryCollection( overrides! ).Build( );
                FakeWebHostBuilder builder = new( );
                _ = builder.ConfigureTuneBridgeServices( services, config );
            } );
            Assert.Contains( "missing contents", ex.Message );
        } finally {
            if (File.Exists( emptyKeyPath )) { File.Delete( emptyKeyPath ); }
        }
    }

    [TestMethod]
    public void AddTuneBridgeServices_WithNoProviders_ShouldThrowInvalidOperationException( ) {
        // Arrange - no Apple, Spotify, or Tidal credentials
        // Ensure Discord is also disabled so we properly hit the provider validation
        Dictionary<string, string?> overrides = new( ) {
            ["TuneBridge:AppleTeamId"] = string.Empty,
            ["TuneBridge:AppleKeyId"] = string.Empty,
            ["TuneBridge:AppleKeyPath"] = string.Empty,
            ["TuneBridge:SpotifyClientId"] = string.Empty,
            ["TuneBridge:SpotifyClientSecret"] = string.Empty,
            ["TuneBridge:TidalClientId"] = string.Empty,
            ["TuneBridge:TidalClientSecret"] = string.Empty,
            ["TuneBridge:DiscordToken"] = string.Empty,
            ["TuneBridge:IdentityConnectionString"] = "Data Source=Identity;Mode=Memory",
            ["TuneBridge:ApiKeySalt"] = "api_key_salt",
            ["TuneBridge:BlueskyPdsUrl"] = string.Empty,
            ["TuneBridge:BlueskyIdentifier"] = string.Empty,
            ["TuneBridge:BlueskyPassword"] = string.Empty,
            ["TuneBridge:LinkCacheConnectionString"] = "Data Source=LinkCache;Mode=Memory;Cache=Shared",
        };

        // Act & Assert
        InvalidOperationException ex = Assert.ThrowsExactly<InvalidOperationException>( () => {
            IServiceCollection services = new ServiceCollection( );
            IConfiguration config = new ConfigurationBuilder( ).AddInMemoryCollection( overrides! ).Build( );
            FakeWebHostBuilder builder = new( );
            _ = builder.ConfigureTuneBridgeServices( services, config );
        } );
        Assert.Contains( "Required settings are missing", ex.Message );
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
            ["TuneBridge:TidalClientId"] = string.Empty,
            ["TuneBridge:TidalClientSecret"] = string.Empty,
            ["TuneBridge:DiscordToken"] = string.Empty,
            ["TuneBridge:IdentityConnectionString"] = "Data Source=TuneBridge;Mode=Memory",
            ["TuneBridge:ApiKeySalt"] = "api_key_salt",
            ["TuneBridge:BlueskyPdsUrl"] = string.Empty,
            ["TuneBridge:BlueskyIdentifier"] = string.Empty,
            ["TuneBridge:BlueskyPassword"] = string.Empty,
            ["TuneBridge:LinkCacheConnectionString"] = "Data Source=TuneBridge;Mode=Memory",
        };

        // Act
        IServiceCollection services = new ServiceCollection( );
        IConfiguration config = new ConfigurationBuilder( ).AddInMemoryCollection( overrides! ).Build( );
        FakeWebHostBuilder builder = new( );
        _ = builder.ConfigureTuneBridgeServices( services, config );
        ServiceProvider sp = services.BuildServiceProvider( );

        // Assert
        Assert.IsNotNull( sp );
        IMediaLinkService? mediaService = sp.GetService<IMediaLinkService>();
        Assert.IsNotNull( mediaService, "IMediaLinkService should be registered with Spotify credentials" );
    }

    // Fake IWebHostBuilder for testing ConfigureTuneBridgeServices without spinning up a full web application
    private class FakeWebHostBuilder : Microsoft.AspNetCore.Hosting.IWebHostBuilder {
        public Microsoft.AspNetCore.Hosting.IWebHost Build( ) => throw new NotImplementedException( );
        public Microsoft.AspNetCore.Hosting.IWebHostBuilder ConfigureAppConfiguration( Action<Microsoft.AspNetCore.Hosting.WebHostBuilderContext, IConfigurationBuilder> configureDelegate ) => this;
        public Microsoft.AspNetCore.Hosting.IWebHostBuilder ConfigureServices( Action<IServiceCollection> configureServices ) => this;
        public Microsoft.AspNetCore.Hosting.IWebHostBuilder ConfigureServices( Action<Microsoft.AspNetCore.Hosting.WebHostBuilderContext, IServiceCollection> configureServices ) => this;
        public string? GetSetting( string key ) => null;
        public Microsoft.AspNetCore.Hosting.IWebHostBuilder UseSetting( string key, string? value ) => this;
    }
}
