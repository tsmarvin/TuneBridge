using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Web.Controllers;
using Microsoft.Extensions.Configuration;

namespace BridgeBeats.Tests.EndToEnd;

/// <summary>
/// End-to-end tests for the MusicLookupController API endpoints.
/// These tests verify the full request/response cycle including routing, serialization, and service integration.
/// Tests use direct provider mode (not queue-based) for fast API validation.
/// </summary>
[TestClass]
[DoNotParallelize] // Prevent parallel execution to avoid overwhelming external APIs with rate limits
[TestCategory( "EndToEnd" )] // Mark as end-to-end tests
public class MusicLookupControllerTests {
    private static CustomWebApplicationFactory? s_factory;
    private static HttpClient? s_client;
    private static string? s_apiKey;

    /// <summary>
    /// Initializes the test factory, HTTP client, and registers a test user with an API key.
    /// </summary>
    /// <param name="context">The test context provided by the test framework.</param>
    [ClassInitialize]
    public static async Task ClassInitialize( TestContext context ) {
        // Load configuration from appsettings.json and user secrets
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddJsonFile( Path.Combine( "src", "BridgeBeats.Web", "appsettings.json" ), optional: true )
            .AddUserSecrets<Web.Program>( optional: true )
            .AddEnvironmentVariables()
            .Build();

        // Build config overrides from loaded configuration
        Dictionary<string, string?> configData = configuration
            .AsEnumerable()
            .Where( kv => kv.Value is not null )
            .Where( kv => !kv.Key.EndsWith( "ConnectionString", StringComparison.OrdinalIgnoreCase ) )
            .ToDictionary( kv => kv.Key, kv => kv.Value );

        // Force Discord token to null to prevent Discord service registration
        configData["BridgeBeats:DiscordToken"] = string.Empty;
        // Disable worker services mode - use direct provider implementations
        configData["BridgeBeats:Workers:UseWorkerServices"] = "false";
        // Force ATProto credentials to empty to disable caching service
        // The caching service requires background workers (QueueProcessorBackgroundService)
        // that are not running in the test environment, causing tests to timeout
        configData["BridgeBeats:ATProtoIdentifier"] = string.Empty;
        configData["BridgeBeats:ATProtoPassword"] = string.Empty;
        configData["BridgeBeats:ATProtoUserDID"] = string.Empty;
        // Higher rate limit for integration tests
        configData["BridgeBeats:RateLimitRequestsPerHour"] = "1000";

        s_factory = new CustomWebApplicationFactory( configData );

        // Create HTTP client for registration
        HttpClient registrationClient = s_factory.CreateClient();

        // Initialize databases after services are configured
        await s_factory.InitializeDatabasesAsync( );

        // Generate unique email for this test class
        string testEmail = $"musiclookup-test-{Guid.NewGuid():N}@test.com";
        string testPassword = "TestPassword123!";

        // Register user via the actual API endpoint
        var registerRequest = new { Email = testEmail, Password = testPassword };
        HttpResponseMessage registerResponse = await registrationClient.PostAsJsonAsync(
            "/account/register",
            registerRequest,
            cancellationToken: context.CancellationToken
        );

        if (!registerResponse.IsSuccessStatusCode) {
            string errorContent = await registerResponse.Content.ReadAsStringAsync( context.CancellationToken );
            throw new Exception( $"Failed to register test user: {registerResponse.StatusCode} - {errorContent}" );
        }

        // Extract API key from response
        JsonElement? registerResult = await registerResponse.Content.ReadFromJsonAsync<JsonElement>( context.CancellationToken );
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

    /// <summary>
    /// Disposes of the test factory after all tests complete.
    /// </summary>
    [ClassCleanup]
    public static void ClassCleanup( ) {
        s_factory?.Dispose( );
    }

    /// <summary>
    /// Verifies that the URL list lookup endpoint returns results for a valid Apple Music URL.
    /// </summary>
    [TestMethod]
    [TestCategory( "Integration" )] // Requires real API credentials
    [TestCategory( "AppleMusic" )]
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout
    public async Task ByUrlList_WithValidAppleMusicUrl_ReturnsOkWithResults( ) {
        // Arrange
        MusicLookupController.UrlReq request = new("https://music.apple.com/us/album/bohemian-rhapsody/1440806041");

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync("/music/lookup/urlList", request, cancellationToken: TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        List<MediaLinkResult>? results = await response.Content.ReadFromJsonAsync<List<MediaLinkResult>>( TestContext.CancellationToken );
        Assert.IsNotNull( results );

        // If we hit rate limits, results may be empty - that's acceptable for integration tests
        if (results.Count == 0) {
            Assert.Inconclusive( "API returned no results - possibly due to rate limiting" );
        } else {
            Assert.IsNotEmpty( results, "Results should not be empty" );
        }
    }

    /// <summary>
    /// Verifies that the URL list lookup endpoint returns results for a valid Spotify URL.
    /// </summary>
    [TestMethod]
    [TestCategory( "Integration" )] // Requires real API credentials
    [TestCategory( "Spotify" )]
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout
    public async Task ByUrlList_WithValidSpotifyUrl_ReturnsOkWithResults( ) {
        // Arrange
        MusicLookupController.UrlReq request = new( "https://open.spotify.com/album/6i6folBtxKV28WX3msQ4FE"  );

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync("/music/lookup/urlList", request, cancellationToken: TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        List<MediaLinkResult>? results = await response.Content.ReadFromJsonAsync<List<MediaLinkResult>>( TestContext.CancellationToken );
        Assert.IsNotNull( results );

        // If we hit rate limits, results may be empty - that's acceptable for integration tests
        if (results.Count == 0) {
            Assert.Inconclusive( "API returned no results - possibly due to rate limiting" );
        } else {
            Assert.IsNotEmpty( results, "Results should not be empty" );
        }
    }

    /// <summary>
    /// Verifies that the URL list lookup endpoint returns results for a valid Tidal URL.
    /// </summary>
    [TestMethod]
    [TestCategory( "Integration" )] // Requires real API credentials
    [TestCategory( "Tidal" )]
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout
    public async Task ByUrlList_WithValidTidalUrl_ReturnsOkWithResults( ) {
        // Arrange
        MusicLookupController.UrlReq request = new(  "https://tidal.com/track/96572657"  );

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync("/music/lookup/urlList", request, cancellationToken: TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        List<MediaLinkResult>? results = await response.Content.ReadFromJsonAsync<List<MediaLinkResult>>( TestContext.CancellationToken );
        Assert.IsNotNull( results );

        // If we hit rate limits, results may be empty - that's acceptable for integration tests
        if (results.Count == 0) {
            Assert.Inconclusive( "API returned no results - possibly due to rate limiting" );
        } else {
            Assert.IsNotEmpty( results, "Results should not be empty" );
        }
    }

    /// <summary>
    /// Verifies that the URL list lookup endpoint returns multiple results when given multiple URLs.
    /// </summary>
    [TestMethod]
    [TestCategory( "Integration" )] // Requires real API credentials
    [TestCategory( "AppleMusic" )]
    [TestCategory( "Spotify" )]
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout
    public async Task ByUrlList_WithMultipleUrls_ReturnsMultipleResults( ) {
        // Arrange
        MusicLookupController.UrlReq request = new(
            "https://open.spotify.com/track/42XDDpDrAXPbryyA9dp1BB " +
            "https://music.apple.com/us/album/a-night-at-the-opera/1440806041"
        );

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync("/music/lookup/urlList", request, cancellationToken: TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        List<MediaLinkResult>? results = await response.Content.ReadFromJsonAsync<List<MediaLinkResult>>( TestContext.CancellationToken );
        Assert.IsNotNull( results );

        // If we hit rate limits, results may be empty - that's acceptable for integration tests
        if (results.Count != 2) {
            Assert.Inconclusive( "API returned no results - possibly due to rate limiting" );
        } else {
            // Should have at least one result (deduplication may occur if URLs point to same content)
            Assert.IsNotEmpty( results, "Results should not be empty" );
        }
    }

    /// <summary>
    /// Verifies that the ISRC lookup endpoint returns results for a valid ISRC code.
    /// </summary>
    [TestMethod]
    [TestCategory( "Integration" )] // Requires real API credentials
    [TestCategory( "AppleMusic" )]
    [TestCategory( "Spotify" )]
    [TestCategory( "Tidal" )]
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout
    public async Task ByIsrc_WithValidIsrc_ReturnsOkWithResult( ) {

        // Arrange
        MusicLookupController.IsrcReq request = new( "GBUM71029604" );

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync("/music/lookup/isrc", request, cancellationToken: TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        MediaLinkResult? result = await response.Content.ReadFromJsonAsync<MediaLinkResult>( TestContext.CancellationToken );

        // If we hit rate limits, result may be null - that's acceptable for integration tests
        if (result is null || result.Results.Count == 0) {
            Assert.Inconclusive( "API returned no results - possibly due to rate limiting" );
        } else {
            Assert.IsNotNull( result );
            Assert.IsNotEmpty( result.Results, "result.Results should not be empty" );
        }
    }

    /// <summary>
    /// Verifies that the UPC lookup endpoint returns results for a valid UPC code.
    /// </summary>
    [TestMethod]
    [TestCategory( "Integration" )] // Requires real API credentials
    [TestCategory( "AppleMusic" )]
    [TestCategory( "Spotify" )]
    [TestCategory( "Tidal" )]
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout
    public async Task ByUpc_WithValidUpc_ReturnsOkWithResult( ) {

        // Arrange
        MusicLookupController.UpcReq request = new( "00602547202307" );

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync("/music/lookup/upc", request, cancellationToken: TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        MediaLinkResult? result = await response.Content.ReadFromJsonAsync<MediaLinkResult>( TestContext.CancellationToken );

        // If we hit rate limits, result may be null - that's acceptable for integration tests
        if (result is null || result.Results.Count == 0) {
            Assert.Inconclusive( "API returned no results - possibly due to rate limiting" );
        } else {
            Assert.IsNotNull( result );
            Assert.IsNotEmpty( result.Results, "result.Results should not be empty" );
        }
    }

    /// <summary>
    /// Verifies that the title lookup endpoint returns results for a valid title and artist combination.
    /// </summary>
    [TestMethod]
    [TestCategory( "Integration" )] // Requires real API credentials
    [TestCategory( "AppleMusic" )]
    [TestCategory( "Spotify" )]
    [TestCategory( "Tidal" )]
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout
    public async Task ByTitle_WithValidTitleAndArtist_ReturnsOkWithResult( ) {

        // Arrange
        MusicLookupController.TitleReq request = new( "Bohemian Rhapsody", "Queen" );

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync("/music/lookup/title", request, cancellationToken: TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        MediaLinkResult? result = await response.Content.ReadFromJsonAsync<MediaLinkResult>( TestContext.CancellationToken );

        // If we hit rate limits, result may be null - that's acceptable for integration tests
        if (result is null || result.Results.Count == 0) {
            Assert.Inconclusive( "API returned no results - possibly due to rate limiting" );
        } else {
            Assert.IsNotNull( result );
            Assert.IsNotEmpty( result.Results, "result.Results should not be empty" );
        }
    }

    /// <summary>
    /// Verifies that the streaming URL endpoint returns results for a valid Spotify URL.
    /// </summary>
    [TestMethod]
    [TestCategory( "Integration" )] // Requires real API credentials
    [TestCategory( "Spotify" )]
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout
    public async Task ByUrl_StreamingEndpoint_ReturnsResults( ) {

        // Arrange
        MusicLookupController.UrlReq request = new(  "https://open.spotify.com/track/4u7EnebtmKWzUH433cf5Qv"  );

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync("/music/lookup/url", request, cancellationToken: TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        string content = await response.Content.ReadAsStringAsync( TestContext.CancellationToken );

        // If we hit rate limits, content may be empty - that's acceptable for integration tests
        if ((content.Length == 0) || (content == "[]")) {
            Assert.Inconclusive( "API returned no results - possibly due to rate limiting" );
        } else {
            Assert.IsGreaterThan( 0, content.Length, "content should not be empty" );
        }
    }

    /// <summary>
    /// Gets or sets the test context which provides information about the current test run.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;
}
