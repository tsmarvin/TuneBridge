using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TuneBridge.Configuration;
using TuneBridge.Domain.Interfaces;

namespace TuneBridge.Tests.Integration;

/// <summary>
/// Integration tests for music lookup services (Apple Music and Spotify).
/// These tests require valid API credentials in appsettings.json.
/// </summary>
public class MusicLookupServiceTests : IDisposable
{
    private readonly IServiceProvider? _serviceProvider;
    private readonly bool _secretsAvailable;

    public MusicLookupServiceTests()
    {
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true)
                .Build();
            
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddLogging();
            
            // Try to add TuneBridge services - will fail if credentials are missing
            services.AddTuneBridgeServices(configuration);
            
            _serviceProvider = services.BuildServiceProvider();
            _secretsAvailable = true;
        }
        catch
        {
            _secretsAvailable = false;
        }
    }

    [Fact]
    public void ServiceRegistration_WithValidSecrets_ShouldRegisterMediaLinkService()
    {
        if (!_secretsAvailable) return;

        // Act
        var mediaLinkService = _serviceProvider!.GetService<IMediaLinkService>();

        // Assert
        Assert.NotNull(mediaLinkService);
    }

    [Fact]
    public async Task GetInfoByISRC_WithValidISRC_ShouldReturnResult()
    {
        if (!_secretsAvailable) return;

        // Arrange
        var mediaLinkService = _serviceProvider!.GetRequiredService<IMediaLinkService>();
        // Using a well-known ISRC for "Bohemian Rhapsody" by Queen
        var isrc = "GBUM71029604";

        // Act
        var result = await mediaLinkService.GetInfoByISRCAsync(isrc);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Results);
        var firstResult = result.Results.First().Value;
        Assert.False(firstResult.IsAlbum ?? true);
        Assert.NotNull(firstResult.Title);
        Assert.NotNull(firstResult.Artist);
    }

    [Fact]
    public async Task GetInfoByUPC_WithValidUPC_ShouldReturnResult()
    {

        if (!_secretsAvailable) return;


        // Arrange
        var mediaLinkService = _serviceProvider!.GetRequiredService<IMediaLinkService>();
        // Using a well-known UPC for "A Night at the Opera" by Queen
        var upc = "00602547202307";

        // Act
        var result = await mediaLinkService.GetInfoByUPCAsync(upc);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Results);
        var firstResult = result.Results.First().Value;
        Assert.True(firstResult.IsAlbum ?? false);
        Assert.NotNull(firstResult.Title);
        Assert.NotNull(firstResult.Artist);
    }

    [Fact]
    public async Task GetInfoByTitle_WithValidTitleAndArtist_ShouldReturnResult()
    {

        if (!_secretsAvailable) return;


        // Arrange
        var mediaLinkService = _serviceProvider!.GetRequiredService<IMediaLinkService>();
        var title = "Bohemian Rhapsody";
        var artist = "Queen";

        // Act
        var result = await mediaLinkService.GetInfoAsync(title, artist);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Results);
        var firstResult = result.Results.First().Value;
        Assert.NotNull(firstResult.Title);
        Assert.NotNull(firstResult.Artist);
        Assert.Contains("Bohemian", firstResult.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetInfoByUrl_WithAppleMusicUrl_ShouldReturnResult()
    {

        if (!_secretsAvailable) return;


        // Arrange
        var mediaLinkService = _serviceProvider!.GetRequiredService<IMediaLinkService>();
        var appleUrl = "https://music.apple.com/us/album/bohemian-rhapsody/1440806041?i=1440806326";

        // Act
        var results = new List<Domain.Contracts.DTOs.MediaLinkResult>();
        await foreach (var result in mediaLinkService.GetInfoAsync(appleUrl))
        {
            results.Add(result);
        }

        // Assert
        Assert.NotEmpty(results);
        var firstResult = results[0];
        Assert.NotEmpty(firstResult.Results);
        var firstLookup = firstResult.Results.First().Value;
        Assert.NotNull(firstLookup.Title);
        Assert.NotNull(firstLookup.Artist);
    }

    [Fact]
    public async Task GetInfoByUrl_WithSpotifyUrl_ShouldReturnResult()
    {

        if (!_secretsAvailable) return;


        // Arrange
        var mediaLinkService = _serviceProvider!.GetRequiredService<IMediaLinkService>();
        var spotifyUrl = "https://open.spotify.com/track/4u7EnebtmKWzUH433cf5Qv";

        // Act
        var results = new List<Domain.Contracts.DTOs.MediaLinkResult>();
        await foreach (var result in mediaLinkService.GetInfoAsync(spotifyUrl))
        {
            results.Add(result);
        }

        // Assert
        Assert.NotEmpty(results);
        var firstResult = results[0];
        Assert.NotEmpty(firstResult.Results);
        var firstLookup = firstResult.Results.First().Value;
        Assert.NotNull(firstLookup.Title);
        Assert.NotNull(firstLookup.Artist);
    }

    [Fact]
    public async Task GetInfoByISRC_WithInvalidISRC_ShouldReturnNull()
    {

        if (!_secretsAvailable) return;


        // Arrange
        var mediaLinkService = _serviceProvider!.GetRequiredService<IMediaLinkService>();
        var invalidIsrc = "INVALID12345";

        // Act
        var result = await mediaLinkService.GetInfoByISRCAsync(invalidIsrc);

        // Assert - Should handle gracefully, either null or empty results
        Assert.True(result == null || result.Results.Count == 0);
    }

    public void Dispose()
    {
        (_serviceProvider as IDisposable)?.Dispose();
    }
}
