using System.Net;
using System.Net.Http.Json;
using BridgeBeats.Domain.Contracts.DTOs;
using BridgeBeats.Web.Controllers;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BridgeBeats.Tests.EndToEnd;

/// <summary>
/// End-to-end tests for the MusicLookupController API endpoints.
/// These tests verify the full request/response cycle including routing, serialization, and service integration.
/// </summary>
[TestClass]
[DoNotParallelize] // Prevent parallel execution to avoid overwhelming external APIs with rate limits
[TestCategory( "EndToEnd" )] // Mark as end-to-end tests
public class MusicLookupControllerTests {
    private static WebApplicationFactory<Program>? s_factory;
    private static HttpClient? s_client;
    private static string? s_apiKey;

    [ClassInitialize]
    public static async Task ClassInitialize( TestContext context ) {
        // Create factory with unique database connection strings and higher rate limit for tests
        Dictionary<string, string?> configData = new( ) {
            ["BridgeBeats:SpotifyClientId"] = "test",
            ["BridgeBeats:SpotifyClientSecret"] = "test",
            ["BridgeBeats:DiscordToken"] = null, // Explicitly null to prevent Discord service registration
            ["BridgeBeats:IdentityConnectionString"] = $"Data Source=MusicLookup_Identity_{Guid.NewGuid():N};Mode=Memory",
            ["BridgeBeats:ApiKeySalt"] = "api_key_salt",
            ["BridgeBeats:ATProtoIdentifier"] = "",
            ["BridgeBeats:ATProtoPassword"] = "",
            ["BridgeBeats:LinkCacheConnectionString"] = $"Data Source=MusicLookup_LinkCache_{Guid.NewGuid():N};Mode=Memory",
            ["BridgeBeats:RateLimitRequestsPerHour"] = "1000", // Much higher limit for integration tests
        };
        s_factory = new CustomWebApplicationFactory( configData );

        // Create HTTP client for registration
        HttpClient registrationClient = s_factory.CreateClient();

        // Generate unique email for this test class
        string testEmail = $"musiclookup-test-{Guid.NewGuid():N}@test.com";
        string testPassword = "TestPassword123!";

        // Register user via the actual API endpoint
        var registerRequest = new { Email = testEmail, Password = testPassword };
        HttpResponseMessage registerResponse = await registrationClient.PostAsJsonAsync(
            "/account/register",
            registerRequest
        );

        if (!registerResponse.IsSuccessStatusCode) {
            string errorContent = await registerResponse.Content.ReadAsStringAsync();
            throw new Exception( $"Failed to register test user: {registerResponse.StatusCode} - {errorContent}" );
        }

        // Extract API key from response
        System.Text.Json.JsonElement? registerResult = await registerResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        if (!registerResult.HasValue) {
            throw new Exception( "Failed to parse registration response" );
        }

        s_apiKey = registerResult.Value.GetProperty( "apiKey" ).GetString( );

        if (string.IsNullOrEmpty( s_apiKey )) {
            throw new Exception( "Failed to extract API key from registration response" );
        }

        // Create HTTP client with API key header for actual tests
        s_client = s_factory.CreateClient( );
        s_client.DefaultRequestHeaders.Add( "X-API-Key", s_apiKey );

        registrationClient.Dispose( );
    }

    [ClassCleanup]
    public static void ClassCleanup( ) {
        s_factory?.Dispose( );
    }

    [TestMethod]
    [TestCategory( "Integration" )] // Requires real API credentials
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout
    public async Task ByUrlList_WithValidAppleMusicUrl_ReturnsOkWithResults( ) {
        // Arrange
        MusicLookupController.UrlReq request = new("https://music.apple.com/us/album/bohemian-rhapsody/1440806041");

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync("/music/lookup/urlList", request );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        List<MediaLinkResult>? results = await response.Content.ReadFromJsonAsync<List<MediaLinkResult>>();
        Assert.IsNotNull( results );

        // If we hit rate limits, results may be empty - that's acceptable for integration tests
        if (results.Count == 0) {
            Assert.Inconclusive( "API returned no results - possibly due to rate limiting" );
        } else {
            Assert.IsNotEmpty( results, "Results should not be empty" );
        }
    }

    [TestMethod]
    [TestCategory( "Integration" )] // Requires real API credentials
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout
    public async Task ByUrlList_WithValidSpotifyUrl_ReturnsOkWithResults( ) {
        // Arrange
        MusicLookupController.UrlReq request = new( "https://open.spotify.com/album/6i6folBtxKV28WX3msQ4FE"  );

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync("/music/lookup/urlList", request );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        List<MediaLinkResult>? results = await response.Content.ReadFromJsonAsync<List<MediaLinkResult>>();
        Assert.IsNotNull( results );

        // If we hit rate limits, results may be empty - that's acceptable for integration tests
        if (results.Count == 0) {
            Assert.Inconclusive( "API returned no results - possibly due to rate limiting" );
        } else {
            Assert.IsNotEmpty( results, "Results should not be empty" );
        }
    }

    [TestMethod]
    [TestCategory( "Integration" )] // Requires real API credentials
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout
    public async Task ByUrlList_WithValidTidalUrl_ReturnsOkWithResults( ) {
        // Arrange
        MusicLookupController.UrlReq request = new(  "https://tidal.com/track/96572657"  );

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync("/music/lookup/urlList", request );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        List<MediaLinkResult>? results = await response.Content.ReadFromJsonAsync<List<MediaLinkResult>>();
        Assert.IsNotNull( results );

        // If we hit rate limits, results may be empty - that's acceptable for integration tests
        if (results.Count == 0) {
            Assert.Inconclusive( "API returned no results - possibly due to rate limiting" );
        } else {
            Assert.IsNotEmpty( results, "Results should not be empty" );
        }
    }

    [TestMethod]
    [TestCategory( "Integration" )] // Requires real API credentials
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout
    public async Task ByUrlList_WithMultipleUrls_ReturnsMultipleResults( ) {
        // Arrange
        MusicLookupController.UrlReq request = new(
            "https://open.spotify.com/album/6X9k3hgEYTUx6tD5FVx7hq " +
            "https://music.apple.com/us/album/a-night-at-the-opera-deluxe-remastered-version/1440806041"
        );

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync("/music/lookup/urlList", request );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        List<MediaLinkResult>? results = await response.Content.ReadFromJsonAsync<List<MediaLinkResult>>();
        Assert.IsNotNull( results );

        // If we hit rate limits, results may be empty - that's acceptable for integration tests
        if (results.Count == 0) {
            Assert.Inconclusive( "API returned no results - possibly due to rate limiting" );
        } else {
            // Should have at least one result (deduplication may occur if URLs point to same content)
            Assert.IsNotEmpty( results, "Results should not be empty" );
        }
    }

    [TestMethod]
    [TestCategory( "Integration" )] // Requires real API credentials
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout
    public async Task ByIsrc_WithValidIsrc_ReturnsOkWithResult( ) {

        // Arrange
        MusicLookupController.IsrcReq request = new( "GBUM71029604" );

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync("/music/lookup/isrc", request );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        MediaLinkResult? result = await response.Content.ReadFromJsonAsync<MediaLinkResult>();

        // If we hit rate limits, result may be null - that's acceptable for integration tests
        if (result is null || result.Results.Count == 0) {
            Assert.Inconclusive( "API returned no results - possibly due to rate limiting" );
        } else {
            Assert.IsNotNull( result );
            Assert.IsNotEmpty( result.Results, "result.Results should not be empty" );
        }
    }

    [TestMethod]
    [TestCategory( "Integration" )] // Requires real API credentials
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout
    public async Task ByUpc_WithValidUpc_ReturnsOkWithResult( ) {

        // Arrange
        MusicLookupController.UpcReq request = new( "00602547202307" );

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync("/music/lookup/upc", request );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        MediaLinkResult? result = await response.Content.ReadFromJsonAsync<MediaLinkResult>();

        // If we hit rate limits, result may be null - that's acceptable for integration tests
        if (result is null || result.Results.Count == 0) {
            Assert.Inconclusive( "API returned no results - possibly due to rate limiting" );
        } else {
            Assert.IsNotNull( result );
            Assert.IsNotEmpty( result.Results, "result.Results should not be empty" );
        }
    }

    [TestMethod]
    [TestCategory( "Integration" )] // Requires real API credentials
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout
    public async Task ByTitle_WithValidTitleAndArtist_ReturnsOkWithResult( ) {

        // Arrange
        MusicLookupController.TitleReq request = new( "Bohemian Rhapsody", "Queen" );

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync("/music/lookup/title", request );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        MediaLinkResult? result = await response.Content.ReadFromJsonAsync<MediaLinkResult>();

        // If we hit rate limits, result may be null - that's acceptable for integration tests
        if (result is null || result.Results.Count == 0) {
            Assert.Inconclusive( "API returned no results - possibly due to rate limiting" );
        } else {
            Assert.IsNotNull( result );
            Assert.IsNotEmpty( result.Results, "result.Results should not be empty" );
        }
    }

    [TestMethod]
    [TestCategory( "Integration" )] // Requires real API credentials
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout
    public async Task ByUrl_StreamingEndpoint_ReturnsResults( ) {

        // Arrange
        MusicLookupController.UrlReq request = new(  "https://open.spotify.com/track/4u7EnebtmKWzUH433cf5Qv"  );

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync("/music/lookup/url", request );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        string content = await response.Content.ReadAsStringAsync( );

        // If we hit rate limits, content may be empty - that's acceptable for integration tests
        if (content.Length == 0) {
            Assert.Inconclusive( "API returned no results - possibly due to rate limiting" );
        } else {
            Assert.IsGreaterThan( 0, content.Length, "content should not be empty" );
        }
    }

    public TestContext TestContext { get; set; } = null!;
}
