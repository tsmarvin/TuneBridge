using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TuneBridge.Configuration;
using TuneBridge.Domain.Interfaces;

namespace TuneBridge.Tests.Integration;

/// <summary>
/// Integration tests for music lookup services (Apple Music and Spotify).
/// These tests require valid API credentials in appsettings.json.
/// </summary>
[TestClass]
public class MusicLookupServiceTests
{
    private IServiceProvider _serviceProvider = null!;

    [TestInitialize]
    public void Initialize()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .Build();
        
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        
        // Add TuneBridge services - will throw if credentials are missing/invalid
        services.AddTuneBridgeServices(configuration);
        
        _serviceProvider = services.BuildServiceProvider();
    }

    [TestMethod]
    public void ServiceRegistration_WithValidSecrets_ShouldRegisterMediaLinkService()
    {

        // Act
        var mediaLinkService = _serviceProvider.GetService<IMediaLinkService>();

        // Assert
        Assert.IsNotNull(mediaLinkService);
    }

    [TestMethod]
    public async Task GetInfoByISRC_WithValidISRC_ShouldReturnResult()
    {

        // Arrange
        var mediaLinkService = _serviceProvider.GetRequiredService<IMediaLinkService>();
        // Using a well-known ISRC for "Bohemian Rhapsody" by Queen
        var isrc = "GBUM71029604";

        // Act
        var result = await mediaLinkService.GetInfoByISRCAsync(isrc);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Results.Count > 0, "result.Results should not be empty");
        var firstResult = result.Results.First().Value;
        Assert.IsFalse(firstResult.IsAlbum ?? true);
        Assert.IsNotNull(firstResult.Title);
        Assert.IsNotNull(firstResult.Artist);
    }

    [TestMethod]
    public async Task GetInfoByUPC_WithValidUPC_ShouldReturnResult()
    {



        // Arrange
        var mediaLinkService = _serviceProvider.GetRequiredService<IMediaLinkService>();
        // Using a well-known UPC for "A Night at the Opera" by Queen
        var upc = "00602547202307";

        // Act
        var result = await mediaLinkService.GetInfoByUPCAsync(upc);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Results.Count > 0, "result.Results should not be empty");
        var firstResult = result.Results.First().Value;
        Assert.IsTrue(firstResult.IsAlbum ?? false);
        Assert.IsNotNull(firstResult.Title);
        Assert.IsNotNull(firstResult.Artist);
    }

    [TestMethod]
    public async Task GetInfoByTitle_WithValidTitleAndArtist_ShouldReturnResult()
    {



        // Arrange
        var mediaLinkService = _serviceProvider.GetRequiredService<IMediaLinkService>();
        var title = "Bohemian Rhapsody";
        var artist = "Queen";

        // Act
        var result = await mediaLinkService.GetInfoAsync(title, artist);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Results.Count > 0, "result.Results should not be empty");
        var firstResult = result.Results.First().Value;
        Assert.IsNotNull(firstResult.Title);
        Assert.IsNotNull(firstResult.Artist);
        Assert.IsTrue(firstResult.Title?.Contains("Bohemian", StringComparison.OrdinalIgnoreCase) == true, "Title should contain Bohemian");
    }

    [TestMethod]
    public async Task GetInfoByUrl_WithAppleMusicUrl_ShouldReturnResult()
    {



        // Arrange
        var mediaLinkService = _serviceProvider.GetRequiredService<IMediaLinkService>();
        var appleUrl = "https://music.apple.com/us/album/bohemian-rhapsody/1440806041?i=1440806326";

        // Act
        var results = new List<Domain.Contracts.DTOs.MediaLinkResult>();
        await foreach (var result in mediaLinkService.GetInfoAsync(appleUrl))
        {
            results.Add(result);
        }

        // Assert
        Assert.IsTrue(results.Count > 0, "Results collection should not be empty");
        var firstResult = results[0];
        Assert.IsTrue(firstResult.Results.Count > 0, "firstResult.Results should not be empty");
        var firstLookup = firstResult.Results.First().Value;
        Assert.IsNotNull(firstLookup.Title);
        Assert.IsNotNull(firstLookup.Artist);
    }

    [TestMethod]
    public async Task GetInfoByUrl_WithSpotifyUrl_ShouldReturnResult()
    {



        // Arrange
        var mediaLinkService = _serviceProvider.GetRequiredService<IMediaLinkService>();
        var spotifyUrl = "https://open.spotify.com/track/4u7EnebtmKWzUH433cf5Qv";

        // Act
        var results = new List<Domain.Contracts.DTOs.MediaLinkResult>();
        await foreach (var result in mediaLinkService.GetInfoAsync(spotifyUrl))
        {
            results.Add(result);
        }

        // Assert
        Assert.IsTrue(results.Count > 0, "Results collection should not be empty");
        var firstResult = results[0];
        Assert.IsTrue(firstResult.Results.Count > 0, "firstResult.Results should not be empty");
        var firstLookup = firstResult.Results.First().Value;
        Assert.IsNotNull(firstLookup.Title);
        Assert.IsNotNull(firstLookup.Artist);
    }

    [TestMethod]
    public async Task GetInfoByISRC_WithInvalidISRC_ShouldReturnNull()
    {



        // Arrange
        var mediaLinkService = _serviceProvider.GetRequiredService<IMediaLinkService>();
        var invalidIsrc = "INVALID12345";

        // Act
        var result = await mediaLinkService.GetInfoByISRCAsync(invalidIsrc);

        // Assert - Should handle gracefully, either null or empty results
        Assert.IsTrue(result == null || result.Results.Count == 0);
    }

    [TestCleanup]
    public void Cleanup()
    {
        (_serviceProvider as IDisposable)?.Dispose();
    }
}
