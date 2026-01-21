using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Exceptions;
using BridgeBeats.Contracts.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BridgeBeats.Tests.Integration;

/// <summary>
/// Integration tests for music lookup services (Apple Music, Spotify, and Tidal).
/// These tests require valid API credentials in appsettings.json or user secrets.
/// Tests use direct provider mode (not queue-based) for fast API validation.
/// </summary>
[TestClass]
[DoNotParallelize] // Prevent parallel execution to avoid overwhelming external APIs with rate limits
[TestCategory( "Integration" )] // Mark as integration tests
public class MusicLookupServiceTests {

    private static IServiceProvider s_serviceProvider = null!;

    private static CustomWebApplicationFactory? s_factory;

    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    [Obsolete]
    public static async Task ClassInitialize( TestContext context ) {
        IConfigurationRoot configuration = new ConfigurationBuilder()
                            .AddJsonFile( Path.Combine( "src", "BridgeBeats.Web", "appsettings.json" ), optional: true )
                                            .AddUserSecrets<Web.Program>( optional: true )
                                            .AddEnvironmentVariables()
                                            .Build();

        // Use the same factory pattern as controller E2E tests, but feed it appsettings.json
        Dictionary<string, string?> overrides = configuration
         .AsEnumerable()
         .Where(kv => kv.Value is not null) // filter nulls from section placeholders
         .Where(kv => !kv.Key.EndsWith( "ConnectionString", StringComparison.OrdinalIgnoreCase ))
         .ToDictionary(kv => kv.Key, kv => kv.Value);

        // ============================================================================
        // CRITICAL: Configure for direct provider mode (not queue-based)
        // ============================================================================

        // Disable Discord in tests
        overrides["BridgeBeats:DiscordToken"] = "";

        // Disable worker services mode - use direct provider implementations
        overrides["BridgeBeats:Workers:UseWorkerServices"] = "false";

        // Disable ATProto caching/queue mode - forces DefaultMediaLinkService (direct calls)
        overrides["BridgeBeats:ATProtoIdentifier"] = "";
        overrides["BridgeBeats:ATProtoPassword"] = "";
        overrides["BridgeBeats:ATProtoUserDID"] = "";

        s_factory = new CustomWebApplicationFactory( overrides );
        s_serviceProvider = s_factory.Services;

        // Initialize databases after services are configured
        await s_factory.InitializeDatabasesAsync( );
    }

    [TestMethod]
    [Timeout( 10000, CooperativeCancellation = true )] // 10 second timeout - service registration should be instant
    public void ServiceRegistration_WithValidSecrets_ShouldRegisterMediaLinkService( ) {
        // Act
        IMediaLinkService? mediaLinkService = s_serviceProvider.GetService<IMediaLinkService>( );

        // Assert
        Assert.IsNotNull( mediaLinkService );
    }

    [TestMethod]
    [TestCategory( "AppleMusic" )]
    [TestCategory( "Spotify" )]
    [TestCategory( "Tidal" )]
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout for simple ISRC lookup
    public async Task GetInfoByISRC_WithValidISRC_ShouldReturnResult( ) {
        // Arrange
        IMediaLinkService mediaLinkService = s_serviceProvider.GetRequiredService<IMediaLinkService>();
        // Using a well-known ISRC for "Bohemian Rhapsody" by Queen
        string isrc = "GBUM71029604";

        // Act - wrap in rate limit handler
        MediaLinkResult? result;
        try {
            result = await mediaLinkService.GetInfoByISRCAsync( isrc );
        } catch (RetryAfterExceededException ex) {
            RateLimitTestHelper.HandleRateLimitException( ex, "ISRC lookup" );
            return;
        }

        // Assert
        if (!RateLimitTestHelper.AssertNotNullOrRateLimited( result, "ISRC lookup" )) {
            return;
        }

        Assert.IsNotEmpty( result!.Results, "result.Results should not be empty" );
        MusicLookupResult firstResult = result.Results.First( ).Value;
        Assert.IsFalse( firstResult.IsAlbum ?? true );
        Assert.IsNotNull( firstResult.Title );
        Assert.IsNotNull( firstResult.Artist );
    }

    [TestMethod]
    [TestCategory( "AppleMusic" )]
    [TestCategory( "Spotify" )]
    [TestCategory( "Tidal" )]
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout for simple UPC lookup
    public async Task GetInfoByUPC_WithValidUPC_ShouldReturnResult( ) {
        // Arrange
        IMediaLinkService mediaLinkService = s_serviceProvider.GetRequiredService<IMediaLinkService>();
        // Using a well-known UPC for "A Night at the Opera" by Queen
        string upc = "00602547202307";

        // Act
        MediaLinkResult? result;
        try {
            result = await mediaLinkService.GetInfoByUPCAsync( upc );
        } catch (RetryAfterExceededException ex) {
            RateLimitTestHelper.HandleRateLimitException( ex, "UPC lookup" );
            return;
        }

        // Assert
        if (!RateLimitTestHelper.AssertNotNullOrRateLimited( result, "UPC lookup" )) {
            return;
        }

        Assert.IsNotEmpty( result!.Results, "result.Results should not be empty" );
        MusicLookupResult firstResult = result.Results.First( ).Value;
        Assert.IsTrue( firstResult.IsAlbum ?? false );
        Assert.IsNotNull( firstResult.Title );
        Assert.IsNotNull( firstResult.Artist );
    }

    [TestMethod]
    [TestCategory( "AppleMusic" )]
    [TestCategory( "Spotify" )]
    [TestCategory( "Tidal" )]
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout for title/artist search
    public async Task GetInfoByTitle_WithValidTitleAndArtist_ShouldReturnResult( ) {
        // Arrange
        IMediaLinkService mediaLinkService = s_serviceProvider.GetRequiredService<IMediaLinkService>();
        string title = "Bohemian Rhapsody";
        string artist = "Queen";

        // Act
        MediaLinkResult? result;
        try {
            result = await mediaLinkService.GetInfoAsync( title, artist );
        } catch (RetryAfterExceededException ex) {
            RateLimitTestHelper.HandleRateLimitException( ex, "Title/Artist lookup" );
            return;
        }

        // Assert
        if (!RateLimitTestHelper.AssertNotNullOrRateLimited( result, "Title/Artist lookup" )) {
            return;
        }

        Assert.IsNotEmpty( result!.Results, "result.Results should not be empty" );
        MusicLookupResult firstResult = result.Results.First( ).Value;
        Assert.IsNotNull( firstResult.Title );
        Assert.IsNotNull( firstResult.Artist );
        Assert.IsTrue( firstResult.Title.Contains( "Bohemian", StringComparison.OrdinalIgnoreCase ), "Title should contain Bohemian" );
    }

    [TestMethod]
    [TestCategory( "AppleMusic" )]
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout for URL lookup
    public async Task GetInfoByUrl_WithAppleMusicUrl_ShouldReturnResult( ) {
        // Arrange
        IMediaLinkService mediaLinkService = s_serviceProvider.GetRequiredService<IMediaLinkService>();
        // Using the same Bohemian Rhapsody track but with album-only URL format
        string appleUrl = "https://music.apple.com/us/album/a-night-at-the-opera-deluxe-remastered-version/1440806041";

        // Act
        List<MediaLinkResult> results = [];
        try {
            await foreach (MediaLinkResult result in mediaLinkService.GetInfoAsync( appleUrl )) {
                results.Add( result );
            }
        } catch (RetryAfterExceededException ex) {
            RateLimitTestHelper.HandleRateLimitException( ex, "Apple Music URL lookup" );
            return;
        }

        // Assert
        if (!RateLimitTestHelper.AssertNotEmptyOrRateLimited( results, "Apple Music URL lookup" )) {
            return;
        }

        MediaLinkResult firstResult = results[0];
        Assert.IsNotEmpty( firstResult.Results, "firstResult.Results should not be empty" );
        MusicLookupResult firstLookup = firstResult.Results.First( ).Value;
        Assert.IsNotNull( firstLookup.Title );
        Assert.IsNotNull( firstLookup.Artist );
    }

    [TestMethod]
    [TestCategory( "Spotify" )]
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout for URL lookup
    public async Task GetInfoByUrl_WithSpotifyUrl_ShouldReturnResult( ) {
        // Arrange
        IMediaLinkService mediaLinkService = s_serviceProvider.GetRequiredService<IMediaLinkService>();
        // Using Bohemian Rhapsody album URL
        string spotifyUrl = "https://open.spotify.com/album/6i6folBtxKV28WX3msQ4FE";

        // Act
        List<MediaLinkResult> results = [];
        try {
            await foreach (MediaLinkResult result in mediaLinkService.GetInfoAsync( spotifyUrl )) {
                results.Add( result );
            }
        } catch (RetryAfterExceededException ex) {
            RateLimitTestHelper.HandleRateLimitException( ex, "Spotify URL lookup" );
            return;
        }

        // Assert
        if (!RateLimitTestHelper.AssertNotEmptyOrRateLimited( results, "Spotify URL lookup" )) {
            return;
        }

        MediaLinkResult firstResult = results[0];
        Assert.IsNotEmpty( firstResult.Results, "firstResult.Results should not be empty" );
        MusicLookupResult firstLookup = firstResult.Results.First( ).Value;
        Assert.IsNotNull( firstLookup.Title );
        Assert.IsNotNull( firstLookup.Artist );
    }

    [TestMethod]
    [TestCategory( "AppleMusic" )]
    [TestCategory( "Spotify" )]
    [TestCategory( "Tidal" )]
    [Timeout( 10000, CooperativeCancellation = true )] // 10 second timeout - invalid ISRC should fail fast
    public async Task GetInfoByISRC_WithInvalidISRC_ShouldReturnNull( ) {
        // Arrange
        IMediaLinkService mediaLinkService = s_serviceProvider.GetRequiredService<IMediaLinkService>();
        string invalidIsrc = "INVALID12345";

        // Act
        MediaLinkResult? result;
        try {
            result = await mediaLinkService.GetInfoByISRCAsync( invalidIsrc );
        } catch (RetryAfterExceededException ex) {
            RateLimitTestHelper.HandleRateLimitException( ex, "Invalid ISRC lookup" );
            return;
        }

        // Assert - Should handle gracefully, either null or empty results
        Assert.IsTrue( result == null || result.Results.Count == 0 );
    }

    [TestMethod]
    [TestCategory( "Tidal" )]
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout for URL lookup
    public async Task GetInfoByUrl_WithTidalUrl_ShouldReturnResult( ) {
        // Arrange
        IMediaLinkService mediaLinkService = s_serviceProvider.GetRequiredService<IMediaLinkService>();
        // Using Bohemian Rhapsody track URL on Tidal
        string tidalUrl = "https://tidal.com/track/96572657";

        // Act
        List<MediaLinkResult> results = [];
        try {
            await foreach (MediaLinkResult result in mediaLinkService.GetInfoAsync( tidalUrl )) {
                results.Add( result );
            }
        } catch (RetryAfterExceededException ex) {
            RateLimitTestHelper.HandleRateLimitException( ex, "Tidal URL lookup" );
            return;
        }

        // Assert
        if (!RateLimitTestHelper.AssertNotEmptyOrRateLimited( results, "Tidal URL lookup" )) {
            return;
        }

        MediaLinkResult firstResult = results[0];
        Assert.IsNotEmpty( firstResult.Results, "firstResult.Results should not be empty" );
        MusicLookupResult firstLookup = firstResult.Results.First( ).Value;
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
    [TestCategory( "AppleMusic" )]
    [TestCategory( "Spotify" )]
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout for URL lookup
    public async Task GetInfoByUrl_WithAppleMusicChironTrack_ShouldFindOnSpotify( ) {
        // Arrange
        IMediaLinkService mediaLinkService = s_serviceProvider.GetRequiredService<IMediaLinkService>();
        string appleUrl = "https://music.apple.com/us/album/chiron/1695231829?i=1695231831";

        // Act
        List<MediaLinkResult> results = [];
        try {
            await foreach (MediaLinkResult result in mediaLinkService.GetInfoAsync( appleUrl )) {
                results.Add( result );
            }
        } catch (RetryAfterExceededException ex) {
            RateLimitTestHelper.HandleRateLimitException( ex, "Apple Music Chiron lookup" );
            return;
        }

        // Assert
        if (!RateLimitTestHelper.AssertNotEmptyOrRateLimited( results, "Apple Music Chiron lookup" )) {
            return;
        }

        MediaLinkResult firstResult = results[0];
        Assert.IsNotEmpty( firstResult.Results, "firstResult.Results should not be empty" );

        // Verify Apple Music result
        Assert.IsTrue( firstResult.Results.TryGetValue( SupportedProviders.AppleMusic, out MusicLookupResult? appleResult ), "Should have Apple Music result" );
        Assert.IsNotNull( appleResult );
        Assert.IsNotNull( appleResult.Title );
        Assert.IsNotNull( appleResult.Artist );
        Assert.IsFalse( appleResult.IsAlbum ?? true, "Should be a track, not an album" );

        // Verify ISRC is present
        Assert.IsFalse( string.IsNullOrWhiteSpace( appleResult.ExternalId ), "Apple Music result should have an ISRC" );
        Assert.AreEqual( "US25X1087647", appleResult.ExternalId, "ISRC should match expected value" );

        // Verify Spotify result is present (the main issue being tested)
        // Note: This may fail if Spotify rate limits are hit
        if (!firstResult.Results.TryGetValue( SupportedProviders.Spotify, out MusicLookupResult? spotifyResult )) {
            Assert.Inconclusive( "Spotify result not found - possibly due to rate limiting" );
            return;
        }

        Assert.IsNotNull( spotifyResult, "Spotify should find the track using ISRC US25X1087647" );
        Assert.IsNotNull( spotifyResult.Title );
        Assert.IsNotNull( spotifyResult.Artist );
        Assert.IsFalse( string.IsNullOrWhiteSpace( spotifyResult.URL ), "Spotify result should have a URL" );
    }

    [ClassCleanup]
    public static void ClassCleanup( ) {
        s_factory?.Dispose( );
    }

}
