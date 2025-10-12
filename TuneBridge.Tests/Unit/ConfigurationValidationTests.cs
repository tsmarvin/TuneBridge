using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TuneBridge.Configuration;

namespace TuneBridge.Tests.Unit;

/// <summary>
/// Unit tests for configuration validation and error handling.
/// Tests edge cases and invalid configurations.
/// </summary>
[TestClass]
public class ConfigurationValidationTests
{
    [TestMethod]
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
        var exception = Assert.ThrowsException<FileNotFoundException>(() =>
            services.AddTuneBridgeServices(configuration));
        Assert.IsTrue(exception.Message.Contains(".p8"));
    }

    [TestMethod]
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
            var exception = Assert.ThrowsException<InvalidDataException>(() =>
                services.AddTuneBridgeServices(configuration));
            Assert.IsTrue(exception.Message.Contains("missing contents"));
        }
        finally
        {
            if (File.Exists(emptyKeyPath))
            {
                File.Delete(emptyKeyPath);
            }
        }
    }

    [TestMethod]
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
        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            services.AddTuneBridgeServices(configuration));
        Assert.IsTrue(exception.Message.Contains("Required settings are missing"));
    }

    [TestMethod]
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
        Assert.IsNotNull(serviceProvider);
    }
}
