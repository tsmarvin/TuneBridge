using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Interfaces;
using TuneBridge.Tests.EndToEnd;

namespace TuneBridge.Tests.Integration;

/// <summary>
/// Integration tests for music lookup services (Apple Music and Spotify).
/// These tests require valid API credentials in appsettings.json.
/// </summary>
[TestClass]
public class MusicLookupServiceTests {

    private IServiceProvider _serviceProvider = null!;

    private static WebApplicationFactory<Program>? s_factory;

    [TestInitialize]
    public void Initialize( ) {
        IConfigurationRoot configuration = new ConfigurationBuilder()
                                            .AddJsonFile("appsettings.json", optional: true)
                                            .AddEnvironmentVariables()
                                            .Build();

        // Use the same factory pattern as controller E2E tests, but feed it appsettings.json
        Dictionary<string, string?> overrides = configuration
         .AsEnumerable()
         .Where(kv => kv.Value is not null) // filter nulls from section placeholders
         .ToDictionary(kv => kv.Key, kv => kv.Value);

        s_factory = new CustomWebApplicationFactory( overrides );
        _serviceProvider = s_factory.Services;
    }

    [TestMethod]
    public void ServiceRegistration_WithValidSecrets_ShouldRegisterMediaLinkService( ) {
        // Act
        IMediaLinkService? mediaLinkService = _serviceProvider.GetService<IMediaLinkService>( );

        // Assert
        Assert.IsNotNull( mediaLinkService );
    }

    [TestMethod]
    public async Task GetInfoByISRC_WithValidISRC_ShouldReturnResult( ) {
        // Arrange
        IMediaLinkService mediaLinkService = _serviceProvider.GetRequiredService<IMediaLinkService>();
        // Using a well-known ISRC for "Bohemian Rhapsody" by Queen
        string isrc = "GBUM71029604";

        // Act
        MediaLinkResult? result = await mediaLinkService.GetInfoByISRCAsync( isrc );

        // Assert
        Assert.IsNotNull( result );
        Assert.IsNotEmpty( result.Results, "result.Results should not be empty" );
        MusicLookupResultDto firstResult = result.Results.First( ).Value;
        Assert.IsFalse( firstResult.IsAlbum ?? true );
        Assert.IsNotNull( firstResult.Title );
        Assert.IsNotNull( firstResult.Artist );
    }

    [TestMethod]
    public async Task GetInfoByUPC_WithValidUPC_ShouldReturnResult( ) {
        // Arrange
        IMediaLinkService mediaLinkService = _serviceProvider.GetRequiredService<IMediaLinkService>();
        // Using a well-known UPC for "A Night at the Opera" by Queen
        string upc = "00602547202307";

        // Act
        MediaLinkResult? result = await mediaLinkService.GetInfoByUPCAsync( upc );

        // Assert
        Assert.IsNotNull( result );
        Assert.IsNotEmpty( result.Results, "result.Results should not be empty" );
        MusicLookupResultDto firstResult = result.Results.First( ).Value;
        Assert.IsTrue( firstResult.IsAlbum ?? false );
        Assert.IsNotNull( firstResult.Title );
        Assert.IsNotNull( firstResult.Artist );
    }

    [TestMethod]
    public async Task GetInfoByTitle_WithValidTitleAndArtist_ShouldReturnResult( ) {
        // Arrange
        IMediaLinkService mediaLinkService = _serviceProvider.GetRequiredService<IMediaLinkService>();
        string title = "Bohemian Rhapsody";
        string artist = "Queen";

        // Act
        MediaLinkResult? result = await mediaLinkService.GetInfoAsync( title, artist );

        // Assert
        Assert.IsNotNull( result );
        Assert.IsNotEmpty( result.Results, "result.Results should not be empty" );
        MusicLookupResultDto firstResult = result.Results.First( ).Value;
        Assert.IsNotNull( firstResult.Title );
        Assert.IsNotNull( firstResult.Artist );
        Assert.IsTrue( firstResult.Title?.Contains( "Bohemian", StringComparison.OrdinalIgnoreCase ), "Title should contain Bohemian" );
    }

    [TestMethod]
    public async Task GetInfoByUrl_WithAppleMusicUrl_ShouldReturnResult( ) {
        // Arrange
        IMediaLinkService mediaLinkService = _serviceProvider.GetRequiredService<IMediaLinkService>();
        // Using the same Bohemian Rhapsody track but with album-only URL format
        string appleUrl = "https://music.apple.com/us/album/a-night-at-the-opera-deluxe-remastered-version/1440806041";

        // Act
        List<MediaLinkResult> results = [];
        await foreach (MediaLinkResult result in mediaLinkService.GetInfoAsync( appleUrl )) {
            results.Add( result );
        }

        // Assert
        Assert.IsNotEmpty( results, "Results collection should not be empty" );
        MediaLinkResult firstResult = results[0];
        Assert.IsNotEmpty( firstResult.Results, "firstResult.Results should not be empty" );
        MusicLookupResultDto firstLookup = firstResult.Results.First( ).Value;
        Assert.IsNotNull( firstLookup.Title );
        Assert.IsNotNull( firstLookup.Artist );
    }

    [TestMethod]
    public async Task GetInfoByUrl_WithSpotifyUrl_ShouldReturnResult( ) {
        // Arrange
        IMediaLinkService mediaLinkService = _serviceProvider.GetRequiredService<IMediaLinkService>();
        // Using Bohemian Rhapsody album URL
        string spotifyUrl = "https://open.spotify.com/album/6i6folBtxKV28WX3msQ4FE";
        // Act
        List<MediaLinkResult> results = [];
        await foreach (MediaLinkResult result in mediaLinkService.GetInfoAsync( spotifyUrl )) {
            results.Add( result );
        }

        // Assert
        Assert.IsNotEmpty( results, "Results collection should not be empty" );
        MediaLinkResult firstResult = results[0];
        Assert.IsNotEmpty( firstResult.Results, "firstResult.Results should not be empty" );
        MusicLookupResultDto firstLookup = firstResult.Results.First( ).Value;
        Assert.IsNotNull( firstLookup.Title );
        Assert.IsNotNull( firstLookup.Artist );
    }

    [TestMethod]
    public async Task GetInfoByISRC_WithInvalidISRC_ShouldReturnNull( ) {
        // Arrange
        IMediaLinkService mediaLinkService = _serviceProvider.GetRequiredService<IMediaLinkService>();
        string invalidIsrc = "INVALID12345";

        // Act
        MediaLinkResult? result = await mediaLinkService.GetInfoByISRCAsync( invalidIsrc );

        // Assert - Should handle gracefully, either null or empty results
        Assert.IsTrue( result == null || result.Results.Count == 0 );
    }

    [TestMethod]
    public async Task GetInfoByUrl_WithTidalUrl_ShouldReturnResult( ) {
        // Arrange
        IMediaLinkService mediaLinkService = _serviceProvider.GetRequiredService<IMediaLinkService>();
        // Using Bohemian Rhapsody track URL on Tidal
        string tidalUrl = "https://tidal.com/track/96572657";

        // Act
        List<MediaLinkResult> results = [];
        await foreach (MediaLinkResult result in mediaLinkService.GetInfoAsync( tidalUrl )) {
            results.Add( result );
        }

        // Assert
        Assert.IsNotEmpty( results, "Results collection should not be empty" );
        MediaLinkResult firstResult = results[0];
        Assert.IsNotEmpty( firstResult.Results, "firstResult.Results should not be empty" );
        MusicLookupResultDto firstLookup = firstResult.Results.First( ).Value;
        Assert.IsNotNull( firstLookup.Title );
        Assert.IsNotNull( firstLookup.Artist );
    }

    /// <summary>
    /// Tests that Spotify can find a track from Apple Music using ISRC cross-platform matching.
    /// This test validates the scenario where an Apple Music track link should be found on Spotify.
    /// Apple Music URL: https://music.apple.com/us/album/chiron/1695231829?i=1695231831
    /// Expected ISRC: US25X1087647
    /// Track: "Chiron" by Shades (Alix Perez & Eprom)
    /// </summary>
    [TestMethod]
    public async Task GetInfoByUrl_WithAppleMusicChironTrack_ShouldFindOnSpotify( ) {
        // Arrange
        IMediaLinkService mediaLinkService = _serviceProvider.GetRequiredService<IMediaLinkService>();
        string appleUrl = "https://music.apple.com/us/album/chiron/1695231829?i=1695231831";

        // Act
        List<MediaLinkResult> results = [];
        await foreach (MediaLinkResult result in mediaLinkService.GetInfoAsync( appleUrl )) {
            results.Add( result );
        }

        // Assert
        Assert.IsNotEmpty( results, "Results collection should not be empty" );
        MediaLinkResult firstResult = results[0];
        Assert.IsNotEmpty( firstResult.Results, "firstResult.Results should not be empty" );

        // Verify Apple Music result
        Assert.IsTrue( firstResult.Results.ContainsKey( Domain.Types.Enums.SupportedProviders.AppleMusic ), "Should have Apple Music result" );
        MusicLookupResultDto appleResult = firstResult.Results[Domain.Types.Enums.SupportedProviders.AppleMusic];
        Assert.IsNotNull( appleResult.Title );
        Assert.IsNotNull( appleResult.Artist );
        Assert.IsFalse( appleResult.IsAlbum ?? true, "Should be a track, not an album" );

        // Verify ISRC is present
        Assert.IsFalse( string.IsNullOrWhiteSpace( appleResult.ExternalId ), "Apple Music result should have an ISRC" );
        Assert.AreEqual( "US25X1087647", appleResult.ExternalId, "ISRC should match expected value" );

        // Verify Spotify result is present (the main issue being tested)
        Assert.IsTrue( firstResult.Results.ContainsKey( Domain.Types.Enums.SupportedProviders.Spotify ),
            "Spotify should find the track using ISRC US25X1087647" );
        MusicLookupResultDto spotifyResult = firstResult.Results[Domain.Types.Enums.SupportedProviders.Spotify];
        Assert.IsNotNull( spotifyResult.Title );
        Assert.IsNotNull( spotifyResult.Artist );
        Assert.IsFalse( string.IsNullOrWhiteSpace( spotifyResult.URL ), "Spotify result should have a URL" );
    }

    [TestCleanup]
    public void Cleanup( ) {
        s_factory?.Dispose( );
    }

}
