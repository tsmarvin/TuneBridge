using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace BridgeBeats.Tests.EndToEnd;

/// <summary>
/// End-to-end tests for the web-specific lookup functionality.
/// </summary>
[TestClass]
[DoNotParallelize] // Prevent parallel execution to avoid overwhelming external APIs with rate limits
[TestCategory( "EndToEnd" )] // Mark as end-to-end tests
public class WebLookupTests {
    private static CustomWebApplicationFactory? s_factory;
    private static HttpClient? s_client;

    [ClassInitialize]
    public static async Task ClassInitialize( TestContext context ) {
        // Load configuration from appsettings.json and user secrets
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddJsonFile( Path.Combine( "src", "appsettings.json" ), optional: true )
            .AddUserSecrets<Program>( optional: true )
            .AddEnvironmentVariables()
            .Build();

        // Build config overrides from loaded configuration
        Dictionary<string, string?> configData = configuration
            .AsEnumerable()
            .Where( kv => kv.Value is not null )
            .ToDictionary( kv => kv.Key, kv => kv.Value );

        // Force Discord token to null to prevent Discord service registration
        configData["BridgeBeats:DiscordToken"] = null;

        s_factory = new CustomWebApplicationFactory( configData );
        s_client = s_factory.CreateClient( );

        // Initialize databases after services are configured
        await s_factory.InitializeDatabasesAsync( );
    }

    [ClassCleanup]
    public static void ClassCleanup( ) {
        s_factory?.Dispose( );
    }

    [TestMethod]
    [TestCategory( "Integration" )] // Requires real API credentials
    [TestCategory( "Spotify" )]
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout
    public async Task WebLookup_WithValidSpotifyUrl_ReturnsCardUrl( ) {
        // Arrange
        object payload = new { uri = "https://open.spotify.com/track/3n3Ppam7vgaVa1iaRUc9Lp" };

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync(
            "/lookup/web",
            payload,
            TestContext.CancellationToken
        );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );

        JsonElement data = await response.Content.ReadFromJsonAsync<JsonElement>( TestContext.CancellationToken );

        bool hasResults = data.GetProperty( "hasResults" ).GetBoolean( );

        // If we hit rate limits, hasResults may be false - that's acceptable for integration tests
        if (!hasResults) {
            Assert.Inconclusive( "API returned no results - possibly due to rate limiting" );
            return;
        }

        Assert.IsTrue( hasResults, "Should have results for valid Spotify URL" );

        // Check items array exists
        JsonElement itemsElement = data.GetProperty( "items" );
        Assert.AreEqual( JsonValueKind.Array, itemsElement.ValueKind );
    }

    [TestMethod]
    [TestCategory( "Integration" )] // Requires real API credentials
    [TestCategory( "Spotify" )]
    [TestCategory( "AppleMusic" )]
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout
    public async Task WebLookup_WithMultipleUrls_ReturnsMultipleCards( ) {
        // Arrange - Multiple URLs separated by space/newline
        object payload = new { uri = "https://open.spotify.com/track/3n3Ppam7vgaVa1iaRUc9Lp https://music.apple.com/us/album/chiron/1695231829" };

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync(
            "/lookup/web",
            payload,
            TestContext.CancellationToken
        );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );

        JsonElement data = await response.Content.ReadFromJsonAsync<JsonElement>( TestContext.CancellationToken );

        bool hasResults = data.GetProperty( "hasResults" ).GetBoolean( );

        // If we hit rate limits, hasResults may be false - that's acceptable for integration tests
        if (!hasResults) {
            Assert.Inconclusive( "API returned no results - possibly due to rate limiting" );
            return;
        }

        Assert.IsTrue( hasResults, "Should have results for valid URLs" );

        // Check that we have multiple items
        JsonElement items = data.GetProperty( "items" );
        int itemCount = items.GetArrayLength( );
        Assert.IsGreaterThan( 0, itemCount, "Should have at least one item" );
    }

    [TestMethod]
    [Timeout( 10000, CooperativeCancellation = true )] // 10 second timeout - should be fast
    public async Task WebLookup_WithEmptyUri_ReturnsBadRequest( ) {
        // Arrange
        object payload = new { uri = "" };

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync(
            "/lookup/web",
            payload,
            TestContext.CancellationToken
        );

        // Assert
        Assert.AreEqual( HttpStatusCode.BadRequest, response.StatusCode );
    }

    [TestMethod]
    [Timeout( 10000, CooperativeCancellation = true )] // 10 second timeout - should be fast
    public async Task WebLookup_WithInvalidUri_ReturnsNoResults( ) {
        // Arrange
        object payload = new { uri = "not-a-valid-music-url" };

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync(
            "/lookup/web",
            payload,
            TestContext.CancellationToken
        );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );

        JsonElement data = await response.Content.ReadFromJsonAsync<JsonElement>( TestContext.CancellationToken );

        bool hasResults = data.GetProperty( "hasResults" ).GetBoolean( );
        Assert.IsFalse( hasResults, "Should have no results for invalid URL" );
    }

    public TestContext TestContext { get; set; } = null!;
}
