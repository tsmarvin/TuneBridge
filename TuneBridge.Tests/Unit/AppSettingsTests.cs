using Microsoft.Extensions.Configuration;
using TuneBridge.Configuration;

namespace TuneBridge.Tests.Unit;

/// <summary>
/// Unit tests for AppSettings configuration binding and validation.
/// </summary>
[TestClass]
public class AppSettingsTests
{
    [TestMethod]
    public void AppSettings_BindsCorrectly_FromConfiguration()
    {
        // Arrange
        var configData = new Dictionary<string, string?>
        {
            ["TuneBridge:NodeNumber"] = "5",
            ["TuneBridge:AppleTeamId"] = "TEAM123456",
            ["TuneBridge:AppleKeyId"] = "KEY1234567",
            ["TuneBridge:AppleKeyPath"] = "/path/to/key.p8",
            ["TuneBridge:SpotifyClientId"] = "spotify_client_id",
            ["TuneBridge:SpotifyClientSecret"] = "spotify_secret",
            ["TuneBridge:DiscordToken"] = "discord_token_here"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Act
        var settings = new AppSettings();
        configuration.GetRequiredSection("TuneBridge").Bind(settings);

        // Assert
        Assert.AreEqual(5, settings.NodeNumber);
        Assert.AreEqual("TEAM123456", settings.AppleTeamId);
        Assert.AreEqual("KEY1234567", settings.AppleKeyId);
        Assert.AreEqual("/path/to/key.p8", settings.AppleKeyPath);
        Assert.AreEqual("spotify_client_id", settings.SpotifyClientId);
        Assert.AreEqual("spotify_secret", settings.SpotifyClientSecret);
        Assert.AreEqual("discord_token_here", settings.DiscordToken);
    }

    [TestMethod]
    public void AppSettings_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var settings = new AppSettings();

        // Assert
        Assert.AreEqual(0, settings.NodeNumber);
        Assert.AreEqual(string.Empty, settings.AppleTeamId);
        Assert.AreEqual(string.Empty, settings.AppleKeyId);
        Assert.AreEqual(string.Empty, settings.AppleKeyPath);
        Assert.AreEqual(string.Empty, settings.SpotifyClientId);
        Assert.AreEqual(string.Empty, settings.SpotifyClientSecret);
        Assert.AreEqual(string.Empty, settings.DiscordToken);
    }

    [TestMethod]
    public void AppSettings_NodeNumber_CanBeZero()
    {
        // Arrange
        var configData = new Dictionary<string, string?>
        {
            ["TuneBridge:NodeNumber"] = "0"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Act
        var settings = new AppSettings();
        configuration.GetRequiredSection("TuneBridge").Bind(settings);

        // Assert
        Assert.AreEqual(0, settings.NodeNumber);
    }
}
