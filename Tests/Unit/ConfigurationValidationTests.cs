using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TuneBridge.Configuration;

namespace TuneBridge.Tests.Unit;

/// <summary>
/// Unit tests for configuration validation and error handling.
/// Tests edge cases and invalid configurations.
/// </summary>
[TestClass]
public class ConfigurationValidationTests {
    [TestMethod]
    public void AddTuneBridgeServices_WithMissingAppleKeyFile_ShouldThrowFileNotFoundException( ) {
        // Arrange
        Dictionary<string, string?> configData = new( ) {
            ["TuneBridge:AppleTeamId"] = "TEAM123456",
            ["TuneBridge:AppleKeyId"] = "KEY1234567",
            ["TuneBridge:AppleKeyPath"] = "/nonexistent/path/key.p8",
            ["TuneBridge:SpotifyClientId"] = "",
            ["TuneBridge:SpotifyClientSecret"] = ""
        };

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        ServiceCollection services = new();
        _ = services.AddSingleton<IConfiguration>( configuration );
        _ = services.AddLogging( );

        // Act & Assert
        FileNotFoundException exception = Assert.ThrowsException<FileNotFoundException>(() =>
            services.AddTuneBridgeServices(configuration));
        Assert.IsTrue( exception.Message.Contains( ".p8" ) );
    }

    [TestMethod]
    public void AddTuneBridgeServices_WithEmptyAppleKeyFile_ShouldThrowInvalidDataException( ) {
        // Arrange
        string emptyKeyPath = Path.Combine(Path.GetTempPath(), $"empty_key_{Guid.NewGuid()}.p8");
        File.WriteAllText( emptyKeyPath, "" );

        try {
            Dictionary<string, string?> configData = new( ) {
                ["TuneBridge:AppleTeamId"] = "TEAM123456",
                ["TuneBridge:AppleKeyId"] = "KEY1234567",
                ["TuneBridge:AppleKeyPath"] = emptyKeyPath,
                ["TuneBridge:SpotifyClientId"] = "",
                ["TuneBridge:SpotifyClientSecret"] = ""
            };

            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();

            ServiceCollection services = new();
            _ = services.AddSingleton<IConfiguration>( configuration );
            _ = services.AddLogging( );

            // Act & Assert
            InvalidDataException exception = Assert.ThrowsException<InvalidDataException>(() =>
                services.AddTuneBridgeServices(configuration));
            Assert.IsTrue( exception.Message.Contains( "missing contents" ) );
        } finally {
            if (File.Exists( emptyKeyPath )) {
                File.Delete( emptyKeyPath );
            }
        }
    }

    [TestMethod]
    public void AddTuneBridgeServices_WithNoProviders_ShouldThrowInvalidOperationException( ) {
        // Arrange
        Dictionary<string, string?> configData = new( ) {
            ["TuneBridge:AppleTeamId"] = "",
            ["TuneBridge:AppleKeyId"] = "",
            ["TuneBridge:AppleKeyPath"] = "",
            ["TuneBridge:SpotifyClientId"] = "",
            ["TuneBridge:SpotifyClientSecret"] = "",
            ["TuneBridge:DiscordToken"] = ""
        };

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        ServiceCollection services = new();
        _ = services.AddSingleton<IConfiguration>( configuration );
        _ = services.AddLogging( );

        // Act & Assert
        InvalidOperationException exception = Assert.ThrowsException<InvalidOperationException>(() =>
            services.AddTuneBridgeServices(configuration));
        Assert.IsTrue( exception.Message.Contains( "Required settings are missing" ) );
    }

    [TestMethod]
    public void AddTuneBridgeServices_WithOnlySpotifyCredentials_ShouldSucceed( ) {
        // Arrange
        Dictionary<string, string?> configData = new( ) {
            ["TuneBridge:AppleTeamId"] = "",
            ["TuneBridge:AppleKeyId"] = "",
            ["TuneBridge:AppleKeyPath"] = "",
            ["TuneBridge:SpotifyClientId"] = "spotify_client_id",
            ["TuneBridge:SpotifyClientSecret"] = "spotify_secret",
            ["TuneBridge:DiscordToken"] = ""
        };

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        ServiceCollection services = new();
        _ = services.AddSingleton<IConfiguration>( configuration );
        _ = services.AddLogging( );

        // Act - Should not throw
        _ = services.AddTuneBridgeServices( configuration );
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        // Assert
        Assert.IsNotNull( serviceProvider );
    }
}
