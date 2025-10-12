using Microsoft.Extensions.Configuration;
using TuneBridge.Configuration;

namespace TuneBridge.Tests.Unit;

/// <summary>
/// Unit tests for AppSettings configuration binding and validation.
/// </summary>
public class AppSettingsTests
{
    [Fact]
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
        Assert.Equal(5, settings.NodeNumber);
        Assert.Equal("TEAM123456", settings.AppleTeamId);
        Assert.Equal("KEY1234567", settings.AppleKeyId);
        Assert.Equal("/path/to/key.p8", settings.AppleKeyPath);
        Assert.Equal("spotify_client_id", settings.SpotifyClientId);
        Assert.Equal("spotify_secret", settings.SpotifyClientSecret);
        Assert.Equal("discord_token_here", settings.DiscordToken);
    }

    [Fact]
    public void AppSettings_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var settings = new AppSettings();

        // Assert
        Assert.Equal(0, settings.NodeNumber);
        Assert.Equal(string.Empty, settings.AppleTeamId);
        Assert.Equal(string.Empty, settings.AppleKeyId);
        Assert.Equal(string.Empty, settings.AppleKeyPath);
        Assert.Equal(string.Empty, settings.SpotifyClientId);
        Assert.Equal(string.Empty, settings.SpotifyClientSecret);
        Assert.Equal(string.Empty, settings.DiscordToken);
    }

    [Fact]
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
        Assert.Equal(0, settings.NodeNumber);
    }
}
