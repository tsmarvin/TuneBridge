using Microsoft.Extensions.Configuration;

namespace TuneBridge.Tests.Helpers;

/// <summary>
/// Helper class for managing test configuration including secrets from environment variables.
/// Handles the mapping of GitHub secrets and variables to appsettings as per issue requirements:
/// - AppleTeamId from APPLETEAMID secret
/// - AppleKeyId from APPLEKEYID secret
/// - AppleKeyPath from file written from APPLEPRIVATEKEY secret
/// - NodeNumber from NODENUMBER variable
/// - SpotifyClientId from SPOTIFYCLIENTID secret
/// - SpotifyClientSecret from SPOTIFYCLIENTSECRET secret
/// - DiscordToken from DISCORDTOKEN secret
/// </summary>
public static class TestConfiguration
{
    private static string? _testAppleKeyPath;

    /// <summary>
    /// Creates a configuration for tests, prioritizing environment variables for secrets.
    /// This is designed to work both in CI/CD with GitHub secrets and locally with environment variables.
    /// </summary>
    public static IConfiguration CreateConfiguration()
    {
        var configBuilder = new ConfigurationBuilder();
        
        // Add environment variables first (highest priority for secrets)
        configBuilder.AddEnvironmentVariables();
        
        // Build a temporary config to read values
        var tempConfig = configBuilder.Build();
        
        // Handle Apple private key - write from environment variable to file if needed
        var applePrivateKey = tempConfig["APPLEPRIVATEKEY"];
        if (!string.IsNullOrEmpty(applePrivateKey))
        {
            _testAppleKeyPath = Path.Combine(Path.GetTempPath(), $"test_apple_key_{Guid.NewGuid()}.p8");
            File.WriteAllText(_testAppleKeyPath, applePrivateKey);
        }
        else
        {
            _testAppleKeyPath = tempConfig["APPLEKEYPATH"];
        }
        
        // Create in-memory configuration with mapped values
        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["TuneBridge:NodeNumber"] = tempConfig["NODENUMBER"] ?? "0",
            ["TuneBridge:AppleTeamId"] = tempConfig["APPLETEAMID"] ?? "",
            ["TuneBridge:AppleKeyId"] = tempConfig["APPLEKEYID"] ?? "",
            ["TuneBridge:AppleKeyPath"] = _testAppleKeyPath ?? "",
            ["TuneBridge:SpotifyClientId"] = tempConfig["SPOTIFYCLIENTID"] ?? "",
            ["TuneBridge:SpotifyClientSecret"] = tempConfig["SPOTIFYCLIENTSECRET"] ?? "",
            ["TuneBridge:DiscordToken"] = tempConfig["DISCORDTOKEN"] ?? "",
            ["Logging:LogLevel:Default"] = "Warning",
            ["Logging:LogLevel:Microsoft.Hosting.Lifetime"] = "Warning",
            ["AllowedHosts"] = "*"
        };
        
        return new ConfigurationBuilder()
            .AddInMemoryCollection(inMemoryConfig)
            .Build();
    }
    
    /// <summary>
    /// Checks if all required secrets are available for running integration tests.
    /// </summary>
    public static bool AreSecretsAvailable()
    {
        var config = CreateConfiguration();
        var appleTeamId = config["TuneBridge:AppleTeamId"];
        var appleKeyId = config["TuneBridge:AppleKeyId"];
        var appleKeyPath = config["TuneBridge:AppleKeyPath"];
        var spotifyClientId = config["TuneBridge:SpotifyClientId"];
        var spotifyClientSecret = config["TuneBridge:SpotifyClientSecret"];
        
        return !string.IsNullOrWhiteSpace(appleTeamId)
               && !string.IsNullOrWhiteSpace(appleKeyId)
               && !string.IsNullOrWhiteSpace(appleKeyPath)
               && !string.IsNullOrWhiteSpace(spotifyClientId)
               && !string.IsNullOrWhiteSpace(spotifyClientSecret);
    }
    
    /// <summary>
    /// Cleans up any temporary files created during testing (like the Apple key file).
    /// </summary>
    public static void Cleanup()
    {
        if (!string.IsNullOrEmpty(_testAppleKeyPath) && File.Exists(_testAppleKeyPath))
        {
            try
            {
                File.Delete(_testAppleKeyPath);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
