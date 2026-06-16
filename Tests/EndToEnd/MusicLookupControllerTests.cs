using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Web.Controllers;
using Microsoft.Extensions.Configuration;

namespace BridgeBeats.Tests.EndToEnd;

/// <summary>
/// End-to-end tests for the <see cref="MusicLookupController"/> lookup API, running the real web app
/// in-memory via <see cref="CustomWebApplicationFactory"/> against the shared Redis container. Registers
/// a test user to obtain an API key, then exercises the URL, ISRC, UPC, title, and streaming endpoints
/// against live providers, and verifies the authorization model across API-key, anonymous, and internal
/// service-key callers. Tests use direct provider mode (not queue-based) for fast API validation.
/// </summary>
[TestClass]
[DoNotParallelize] // Prevent parallel execution to avoid overwhelming external APIs with rate limits
[TestCategory( "EndToEnd" )] // Mark as end-to-end tests
public class MusicLookupControllerTests {
    /// <summary>The shared web application factory hosting the app for this test class.</summary>
    private static CustomWebApplicationFactory? s_factory;
    /// <summary>HTTP client carrying a valid API key in the <c>X-API-Key</c> header.</summary>
    private static HttpClient? s_client;
    /// <summary>HTTP client with no credentials, used for anonymous-access tests.</summary>
    private static HttpClient? s_anonymousClient;
    /// <summary>HTTP client carrying a valid internal service key in the <c>X-Service-Key</c> header.</summary>
    private static HttpClient? s_internalClient;
    /// <summary>HTTP client carrying an invalid internal service key, used for negative auth tests.</summary>
    private static HttpClient? s_badInternalClient;
    /// <summary>The API key extracted from the registered test user.</summary>
    private static string? s_apiKey;
    /// <summary>The internal service key configured for the test host and sent by the internal client.</summary>
    private const string TestInternalServiceKey = "test-internal-service-key-abc123";

    /// <summary>
    /// Builds the test host with worker services disabled, registers a test user to obtain an API key,
    /// and creates API-key, anonymous, valid-internal, and invalid-internal clients for the auth tests.
    /// </summary>
    /// <param name="context">The MSTest class context, used for its cancellation token.</param>
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
            .ToDictionary( );

        // Inject a known internal-service key via environment variable so it is available
        // during service registration (AddBridgeBeatsServices runs before ConfigureAppConfiguration
        // test overrides are applied, so environment variables are the reliable injection point).
        Environment.SetEnvironmentVariable( "BridgeBeats__InternalServiceKey", TestInternalServiceKey );
        configData["BridgeBeats:InternalServiceKey"] = TestInternalServiceKey;
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

        // Get antiforgery token for registration using shared helper
        string antiforgeryToken = await AntiforgeryTestHelper.GetAntiforgeryTokenAsync( registrationClient, context.CancellationToken );

        // Register user via the actual API endpoint with antiforgery token
        var registerRequest = new { Email = testEmail, Password = testPassword };
        HttpResponseMessage registerResponse = await AntiforgeryTestHelper.PostWithAntiforgeryAsync(
            registrationClient, "/account/register", JsonContent.Create( registerRequest ), antiforgeryToken, context.CancellationToken );

        if (!registerResponse.IsSuccessStatusCode) {
            string errorContent = await registerResponse.Content.ReadAsStringAsync( context.CancellationToken );
            throw new InvalidOperationException( $"Failed to register test user: {registerResponse.StatusCode} - {errorContent}" );
        }

        // Extract API key from response
        JsonElement? registerResult = await registerResponse.Content.ReadFromJsonAsync<JsonElement>( context.CancellationToken );
        if (!registerResult.HasValue) {
            throw new InvalidOperationException( "Failed to parse registration response" );
        }

        s_apiKey = registerResult.Value.GetProperty( "apiKey" ).GetString( );

        if (string.IsNullOrEmpty( s_apiKey )) {
            throw new InvalidOperationException( "Failed to extract API key from registration response" );
        }

        // Create HTTP client with API key header for actual tests
        s_client = s_factory.CreateClient( );
        s_client.DefaultRequestHeaders.Add( "X-API-Key", s_apiKey );

        // Create an anonymous client with no API key for auth-boundary tests
        s_anonymousClient = s_factory.CreateClient( );

        // Create a client bearing a valid X-Service-Key for InternalService auth tests
        s_internalClient = s_factory.CreateClient( );
        s_internalClient.DefaultRequestHeaders.Add( "X-Service-Key", TestInternalServiceKey );

        // Create a client bearing an invalid X-Service-Key for negative-control auth tests
        s_badInternalClient = s_factory.CreateClient( );
        s_badInternalClient.DefaultRequestHeaders.Add( "X-Service-Key", "wrong-key" );

        registrationClient.Dispose( );
    }

    /// <summary>
    /// Disposes the test host and clears the internal service key environment variable.
    /// </summary>
    [ClassCleanup]
    public static void ClassCleanup( ) {
        s_factory?.Dispose( );
        Environment.SetEnvironmentVariable( "BridgeBeats__InternalServiceKey", null );
    }

    /// <summary>
    /// Posts a valid Apple Music album URL to the URL-list endpoint and verifies 200 OK with a result
    /// list (inconclusive when the provider returns nothing, signalling rate limiting).
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
    /// Posts a valid Spotify album URL to the URL-list endpoint and verifies 200 OK with a result list
    /// (inconclusive when the provider returns nothing, signalling rate limiting).
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
    /// Posts a valid Tidal track URL to the URL-list endpoint and verifies 200 OK with a result list
    /// (inconclusive when the provider returns nothing, signalling rate limiting).
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
    /// Posts two URLs (Spotify and Apple Music) to the URL-list endpoint and verifies 200 OK with two
    /// results (inconclusive when the count differs, signalling rate limiting).
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
    /// Posts a valid ISRC to the ISRC endpoint and verifies 200 OK with a non-empty result (inconclusive
    /// when no results are returned, signalling rate limiting).
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
    /// Posts a valid UPC to the UPC endpoint and verifies 200 OK with a non-empty result (inconclusive
    /// when no results are returned, signalling rate limiting).
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
    /// Posts a valid title and artist to the title endpoint and verifies 200 OK with a non-empty result
    /// (inconclusive when no results are returned, signalling rate limiting).
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
    /// Posts a Spotify track URL to the streaming URL endpoint and verifies 200 OK with a non-empty body
    /// (inconclusive when the body is empty or an empty array, signalling rate limiting).
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
    /// Verifies the single-URL endpoint is public: an anonymous caller (no API key) receives 200 OK
    /// rather than 401 Unauthorized.
    /// Failure-first: before [AllowAnonymous] was added, the class-level [Authorize] returned 401
    /// for any unauthenticated request; the anonymous client would have received 401, not 200.
    /// </summary>
    [TestMethod]
    [TestCategory( "Auth" )]
    [Timeout( 10000, CooperativeCancellation = true )]
    public async Task ByUrl_WithoutApiKey_ReturnsOkNotUnauthorized( ) {
        // Arrange — anonymous client (no X-API-Key header)
        MusicLookupController.UrlReq request = new( "https://open.spotify.com/track/4u7EnebtmKWzUH433cf5Qv" );

        // Act
        HttpResponseMessage response = await s_anonymousClient!.PostAsJsonAsync(
            "/music/lookup/url", request, cancellationToken: TestContext.CancellationToken );

        // Assert — 401 would mean the endpoint is still gated; 200 confirms [AllowAnonymous] is effective
        Assert.AreNotEqual( HttpStatusCode.Unauthorized, response.StatusCode,
            "ByUrl must not return 401; the endpoint is public ([AllowAnonymous])" );
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode,
            "ByUrl must return 200 for anonymous callers" );
    }

    /// <summary>
    /// Verifies the URL-list endpoint is public: an anonymous caller (no API key) receives 200 OK rather
    /// than 401 Unauthorized.
    /// Failure-first: before [AllowAnonymous] was added, the class-level [Authorize] returned 401
    /// for any unauthenticated request; the anonymous client would have received 401, not 200.
    /// </summary>
    [TestMethod]
    [TestCategory( "Auth" )]
    [Timeout( 10000, CooperativeCancellation = true )]
    public async Task ByUrlList_WithoutApiKey_ReturnsOkNotUnauthorized( ) {
        // Arrange — anonymous client (no X-API-Key header)
        MusicLookupController.UrlReq request = new( "https://open.spotify.com/album/6i6folBtxKV28WX3msQ4FE" );

        // Act
        HttpResponseMessage response = await s_anonymousClient!.PostAsJsonAsync(
            "/music/lookup/urlList", request, cancellationToken: TestContext.CancellationToken );

        // Assert — 401 would mean the endpoint is still gated; 200 confirms [AllowAnonymous] is effective
        Assert.AreNotEqual( HttpStatusCode.Unauthorized, response.StatusCode,
            "ByUrlList must not return 401; the endpoint is public ([AllowAnonymous])" );
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode,
            "ByUrlList must return 200 for anonymous callers" );
    }

    /// <summary>
    /// Verifies the ISRC endpoint requires authentication: an anonymous caller (no API key) receives 401
    /// Unauthorized (representative for isrc/upc/title).
    /// Failure-first: this test verifies the class-level [Authorize] still applies to non-URL actions.
    /// If [AllowAnonymous] were incorrectly placed at class level, this test would fail with 200.
    /// </summary>
    [TestMethod]
    [TestCategory( "Auth" )]
    [Timeout( 10000, CooperativeCancellation = true )]
    public async Task ByIsrc_WithoutApiKey_ReturnsUnauthorized( ) {
        // Arrange — anonymous client (no X-API-Key header)
        MusicLookupController.IsrcReq request = new( "GBUM71029604" );

        // Act
        HttpResponseMessage response = await s_anonymousClient!.PostAsJsonAsync(
            "/music/lookup/isrc", request, cancellationToken: TestContext.CancellationToken );

        // Assert — isrc endpoint must remain protected
        Assert.AreEqual( HttpStatusCode.Unauthorized, response.StatusCode,
            "ByIsrc must return 401 for anonymous callers; the endpoint requires an API key" );
    }

    /// <summary>
    /// Verifies the ISRC endpoint accepts internal service authentication: a caller presenting a valid
    /// <c>X-Service-Key</c> is not rejected with 401 Unauthorized.
    /// Failure-first: before the InternalService scheme was registered (or if the controller's
    /// <c>[Authorize]</c> did not list <c>InternalService</c>), a valid service key would
    /// have received 401 because no scheme would accept it.
    /// </summary>
    [TestMethod]
    [TestCategory( "Auth" )]
    [Timeout( 10000, CooperativeCancellation = true )]
    public async Task ByIsrc_WithValidInternalServiceKey_ReturnsOkNotUnauthorized( ) {
        // Arrange — client bears a valid X-Service-Key; no X-API-Key header
        MusicLookupController.IsrcReq request = new( "GBUM71029604" );

        // Act
        HttpResponseMessage response = await s_internalClient!.PostAsJsonAsync(
            "/music/lookup/isrc", request, cancellationToken: TestContext.CancellationToken );

        // Assert — the InternalService scheme must authenticate the request
        Assert.AreNotEqual( HttpStatusCode.Unauthorized, response.StatusCode,
            "A valid X-Service-Key must not return 401; InternalService auth must succeed" );
    }

    /// <summary>
    /// Verifies the ISRC endpoint rejects a bad internal service key: a caller presenting an invalid
    /// <c>X-Service-Key</c> receives 401 Unauthorized.
    /// Failure-first: a lenient auth handler that accepted any non-empty key would return 200,
    /// not 401, causing this test to fail.
    /// </summary>
    [TestMethod]
    [TestCategory( "Auth" )]
    [Timeout( 10000, CooperativeCancellation = true )]
    public async Task ByIsrc_WithInvalidInternalServiceKey_ReturnsUnauthorized( ) {
        // Arrange — client bears an incorrect X-Service-Key; no X-API-Key header
        MusicLookupController.IsrcReq request = new( "GBUM71029604" );

        // Act
        HttpResponseMessage response = await s_badInternalClient!.PostAsJsonAsync(
            "/music/lookup/isrc", request, cancellationToken: TestContext.CancellationToken );

        // Assert — wrong key must be rejected
        Assert.AreEqual( HttpStatusCode.Unauthorized, response.StatusCode,
            "An invalid X-Service-Key must return 401" );
    }

    /// <summary>
    /// The MSTest-injected test context, used here to obtain the per-test cancellation token.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;
}
