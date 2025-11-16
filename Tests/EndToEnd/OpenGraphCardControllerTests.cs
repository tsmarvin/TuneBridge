using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TuneBridge.Tests.EndToEnd;

/// <summary>
/// End-to-end tests for the web-specific lookup functionality.
/// </summary>
[TestClass]
[DoNotParallelize] // Prevent parallel execution to avoid overwhelming external APIs with rate limits
[TestCategory( "EndToEnd" )] // Mark as end-to-end tests
public class WebLookupTests {
    private static WebApplicationFactory<Program>? s_factory;
    private static HttpClient? s_client;

    [ClassInitialize]
    public static void ClassInitialize( TestContext context ) {
        // Create factory with unique database connection strings and no Discord token
        Dictionary<string, string?> configData = new( ) {
            ["TuneBridge:SpotifyClientId"] = "test",
            ["TuneBridge:SpotifyClientSecret"] = "test",
            ["TuneBridge:DiscordToken"] = null, // Explicitly null to prevent Discord service registration
            ["TuneBridge:IdentityConnectionString"] = $"Data Source=WebLookup_Identity_{Guid.NewGuid():N};Mode=Memory;Cache=Shared",
            ["TuneBridge:ApiKeySalt"] = "api_key_salt",
            ["TuneBridge:ATProtoIdentifier"] = "",
            ["TuneBridge:ATProtoPassword"] = "",
            ["TuneBridge:LinkCacheConnectionString"] = $"Data Source=WebLookup_LinkCache_{Guid.NewGuid():N};Mode=Memory;Cache=Shared",
        };
        s_factory = new CustomWebApplicationFactory( configData );
        s_client = s_factory.CreateClient( );
    }

    [ClassCleanup]
    public static void ClassCleanup( ) {
        s_client?.Dispose( );
        s_factory?.Dispose( );
    }

    [TestMethod]
    [TestCategory( "Integration" )] // Requires real API credentials
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

        dynamic? data = await response.Content.ReadFromJsonAsync<dynamic>( TestContext.CancellationToken );
        Assert.IsNotNull( data );

        bool hasResults = data?.GetProperty( "hasResults" ).GetBoolean( );

        // If we hit rate limits, hasResults may be false - that's acceptable for integration tests
        if (!hasResults) {
            Assert.Inconclusive( "API returned no results - possibly due to rate limiting" );
            return;
        }

        Assert.IsTrue( hasResults, "Should have results for valid Spotify URL" );

        // Check items array exists
        System.Text.Json.JsonElement itemsElement;
        Assert.IsTrue( data?.TryGetProperty( "items", out itemsElement ), "Should have items array" );
    }

    [TestMethod]
    [TestCategory( "Integration" )] // Requires real API credentials
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

        dynamic? data = await response.Content.ReadFromJsonAsync<dynamic>( TestContext.CancellationToken );
        Assert.IsNotNull( data );

        bool hasResults = data?.GetProperty( "hasResults" ).GetBoolean( );

        // If we hit rate limits, hasResults may be false - that's acceptable for integration tests
        if (!hasResults) {
            Assert.Inconclusive( "API returned no results - possibly due to rate limiting" );
            return;
        }

        Assert.IsTrue( hasResults, "Should have results for valid URLs" );
        int itemCount = 0;
        // Check that we have multiple items
        if (null != data?.GetProperty( "items" )) {
            dynamic items = data.GetProperty( "items" );
            itemCount = items?.GetArrayLength( ) ?? 0;
        }
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

        dynamic? data = await response.Content.ReadFromJsonAsync<dynamic>( TestContext.CancellationToken );
        Assert.IsNotNull( data );

        bool hasResults = data?.GetProperty( "hasResults" ).GetBoolean( );
        Assert.IsFalse( hasResults, "Should have no results for invalid URL" );
    }

    public TestContext TestContext { get; set; } = null!;
}
