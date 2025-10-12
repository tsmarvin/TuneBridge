using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TuneBridge.Configuration;

namespace TuneBridge.Tests.Unit;

/// <summary>
/// Unit tests for configuration validation and error handling.
/// Tests edge cases and invalid configurations.
/// </summary>
public class ConfigurationValidationTests
{
    [Fact]
    public void AddTuneBridgeServices_WithMissingAppleKeyFile_ShouldThrowFileNotFoundException()
    {
        // Arrange
        var configData = new Dictionary<string, string?>
        {
            ["TuneBridge:AppleTeamId"] = "TEAM123456",
            ["TuneBridge:AppleKeyId"] = "KEY1234567",
            ["TuneBridge:AppleKeyPath"] = "/nonexistent/path/key.p8",
            ["TuneBridge:SpotifyClientId"] = "",
            ["TuneBridge:SpotifyClientSecret"] = ""
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        // Act & Assert
        var exception = Assert.Throws<FileNotFoundException>(() =>
            services.AddTuneBridgeServices(configuration));
        Assert.Contains(".p8", exception.Message);
    }

    [Fact]
    public void AddTuneBridgeServices_WithEmptyAppleKeyFile_ShouldThrowInvalidDataException()
    {
        // Arrange
        var emptyKeyPath = Path.Combine(Path.GetTempPath(), $"empty_key_{Guid.NewGuid()}.p8");
        File.WriteAllText(emptyKeyPath, "");

        try
        {
            var configData = new Dictionary<string, string?>
            {
                ["TuneBridge:AppleTeamId"] = "TEAM123456",
                ["TuneBridge:AppleKeyId"] = "KEY1234567",
                ["TuneBridge:AppleKeyPath"] = emptyKeyPath,
                ["TuneBridge:SpotifyClientId"] = "",
                ["TuneBridge:SpotifyClientSecret"] = ""
            };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddLogging();

            // Act & Assert
            var exception = Assert.Throws<InvalidDataException>(() =>
                services.AddTuneBridgeServices(configuration));
            Assert.Contains("missing contents", exception.Message);
        }
        finally
        {
            if (File.Exists(emptyKeyPath))
            {
                File.Delete(emptyKeyPath);
            }
        }
    }

    [Fact]
    public void AddTuneBridgeServices_WithNoProviders_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var configData = new Dictionary<string, string?>
        {
            ["TuneBridge:AppleTeamId"] = "",
            ["TuneBridge:AppleKeyId"] = "",
            ["TuneBridge:AppleKeyPath"] = "",
            ["TuneBridge:SpotifyClientId"] = "",
            ["TuneBridge:SpotifyClientSecret"] = "",
            ["TuneBridge:DiscordToken"] = ""
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddTuneBridgeServices(configuration));
        Assert.Contains("Required settings are missing", exception.Message);
    }

    [Fact]
    public void AddTuneBridgeServices_WithOnlySpotifyCredentials_ShouldSucceed()
    {
        // Arrange
        var configData = new Dictionary<string, string?>
        {
            ["TuneBridge:AppleTeamId"] = "",
            ["TuneBridge:AppleKeyId"] = "",
            ["TuneBridge:AppleKeyPath"] = "",
            ["TuneBridge:SpotifyClientId"] = "spotify_client_id",
            ["TuneBridge:SpotifyClientSecret"] = "spotify_secret",
            ["TuneBridge:DiscordToken"] = ""
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        // Act - Should not throw
        services.AddTuneBridgeServices(configuration);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(serviceProvider);
    }
}
