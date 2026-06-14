using System.Net;
using Microsoft.Extensions.Configuration;

namespace BridgeBeats.Tests.EndToEnd;

/// <summary>
/// End-to-end tests for the home controller, exercising the real web application in-memory through
/// <see cref="CustomWebApplicationFactory"/> against the shared Redis container and an isolated SQLite
/// identity database. Covers page rendering (home, privacy, error, 404) and the lookup form actions,
/// including antiforgery-protected POSTs and live provider lookups by ISRC, UPC, and title.
/// </summary>
[TestClass]
[TestCategory( "EndToEnd" )] // Mark as end-to-end tests - these are fast and don't hit external APIs
public class HomeControllerTests {
    /// <summary>The shared web application factory hosting the app for this test class.</summary>
    private static CustomWebApplicationFactory? s_factory;
    /// <summary>The HTTP client connected to the test host.</summary>
    private static HttpClient? s_client;
    /// <summary>The antiforgery token reused across the class's form-POST tests.</summary>
    private static string? s_antiforgeryToken;

    /// <summary>
    /// Builds the test host with worker services disabled and external integrations blanked, creates a
    /// client, migrates the identity database, and fetches an antiforgery token for the form tests.
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

        // Force Discord token to null to prevent Discord service registration
        configData["BridgeBeats:DiscordToken"] = string.Empty;
        // Disable worker services mode - use direct provider implementations
        configData["BridgeBeats:Workers:UseWorkerServices"] = "false";
        // Force ATProto credentials to empty to disable caching service
        // The caching service requires background workers that are not running in the test environment
        configData["BridgeBeats:ATProtoIdentifier"] = string.Empty;
        configData["BridgeBeats:ATProtoPassword"] = string.Empty;
        configData["BridgeBeats:ATProtoUserDID"] = string.Empty;

        s_factory = new CustomWebApplicationFactory( configData );
        s_client = s_factory.CreateClient( );

        // Initialize databases after services are configured
        await s_factory.InitializeDatabasesAsync( );

        // Fetch antiforgery token for POST requests
        s_antiforgeryToken = await AntiforgeryTestHelper.GetAntiforgeryTokenAsync( s_client, context.CancellationToken );
    }

    /// <summary>
    /// Disposes the test host (and its SQLite identity database) after the class completes.
    /// </summary>
    [ClassCleanup]
    public static void ClassCleanup( ) {
        s_factory?.Dispose( );
    }

    /// <summary>
    /// Verifies the home index returns 200 OK with an HTML content type.
    /// </summary>
    [TestMethod]
    [Timeout( 10000, CooperativeCancellation = true )] // 10 second timeout - should be fast
    public async Task Index_ReturnsSuccessStatusCode( ) {
        // Act
        HttpResponseMessage response = await s_client!.GetAsync("/", TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        Assert.AreEqual( "text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString( ) );
    }

    /// <summary>
    /// Verifies the home index response body contains the "BridgeBeats" branding text.
    /// </summary>
    [TestMethod]
    [Timeout( 10000, CooperativeCancellation = true )] // 10 second timeout - should be fast
    public async Task Index_ContainsExpectedContent( ) {
        // Act
        HttpResponseMessage response = await s_client!.GetAsync("/", TestContext.CancellationToken );
        string content = await response.Content.ReadAsStringAsync( TestContext.CancellationToken );

        // Assert
        Assert.IsTrue( content.Contains( "BridgeBeats", StringComparison.OrdinalIgnoreCase ) );
    }

    /// <summary>
    /// Verifies the privacy page returns 200 OK.
    /// </summary>
    [TestMethod]
    [Timeout( 10000, CooperativeCancellation = true )] // 10 second timeout - should be fast
    public async Task Privacy_ReturnsSuccessStatusCode( ) {
        // Act
        HttpResponseMessage response = await s_client!.GetAsync("/Home/Privacy", TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
    }

    /// <summary>
    /// Verifies the error page returns 200 OK.
    /// </summary>
    [TestMethod]
    [Timeout( 10000, CooperativeCancellation = true )] // 10 second timeout - should be fast
    public async Task Error_ReturnsSuccessStatusCode( ) {
        // Act
        HttpResponseMessage response = await s_client!.GetAsync("/Home/Error", TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
    }

    /// <summary>
    /// Verifies an unmapped route returns 404 Not Found.
    /// </summary>
    [TestMethod]
    [Timeout( 10000, CooperativeCancellation = true )] // 10 second timeout - should be fast
    public async Task NonExistentRoute_ReturnsNotFound( ) {
        // Act
        HttpResponseMessage response = await s_client!.GetAsync("/NonExistent/Route", TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.NotFound, response.StatusCode );
    }

    /// <summary>
    /// Posts an empty URI to the lookup action and verifies the response is 200 OK with a "URI is
    /// required" validation message.
    /// </summary>
    [TestMethod]
    [Timeout( 10000, CooperativeCancellation = true )] // 10 second timeout - should be fast
    public async Task LookupResults_WithEmptyUri_ReturnsMessageView( ) {
        // Arrange
        FormUrlEncodedContent formData = new( new Dictionary<string, string> {
            ["uri"] = ""
        } );

        // Act
        HttpResponseMessage response = await AntiforgeryTestHelper.PostWithAntiforgeryAsync(
            s_client!, "/Home/LookupResults", formData, s_antiforgeryToken!, TestContext.CancellationToken );
        string content = await response.Content.ReadAsStringAsync( TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        Assert.IsTrue( content.Contains( "URI is required", StringComparison.OrdinalIgnoreCase ) );
    }

    /// <summary>
    /// Posts an empty ISRC to the ISRC lookup action and verifies the response is 200 OK with an "ISRC
    /// is required" validation message.
    /// </summary>
    [TestMethod]
    [Timeout( 10000, CooperativeCancellation = true )] // 10 second timeout - should be fast
    public async Task LookupResultsByIsrc_WithEmptyIsrc_ReturnsMessageView( ) {
        // Arrange
        FormUrlEncodedContent formData = new( new Dictionary<string, string> {
            ["isrc"] = ""
        } );

        // Act
        HttpResponseMessage response = await AntiforgeryTestHelper.PostWithAntiforgeryAsync(
            s_client!, "/Home/LookupResultsByIsrc", formData, s_antiforgeryToken!, TestContext.CancellationToken );
        string content = await response.Content.ReadAsStringAsync( TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        Assert.IsTrue( content.Contains( "ISRC is required", StringComparison.OrdinalIgnoreCase ) );
    }

    /// <summary>
    /// Posts an empty UPC to the UPC lookup action and verifies the response is 200 OK with a "UPC is
    /// required" validation message.
    /// </summary>
    [TestMethod]
    [Timeout( 10000, CooperativeCancellation = true )] // 10 second timeout - should be fast
    public async Task LookupResultsByUpc_WithEmptyUpc_ReturnsMessageView( ) {
        // Arrange
        FormUrlEncodedContent formData = new( new Dictionary<string, string> {
            ["upc"] = ""
        } );

        // Act
        HttpResponseMessage response = await AntiforgeryTestHelper.PostWithAntiforgeryAsync(
            s_client!, "/Home/LookupResultsByUpc", formData, s_antiforgeryToken!, TestContext.CancellationToken );
        string content = await response.Content.ReadAsStringAsync( TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        Assert.IsTrue( content.Contains( "UPC is required", StringComparison.OrdinalIgnoreCase ) );
    }

    /// <summary>
    /// Posts an empty title and artist to the title lookup action and verifies the response is 200 OK
    /// with a "Title and artist are required" validation message.
    /// </summary>
    [TestMethod]
    [Timeout( 10000, CooperativeCancellation = true )] // 10 second timeout - should be fast
    public async Task LookupResultsByTitle_WithEmptyTitleAndArtist_ReturnsMessageView( ) {
        // Arrange
        FormUrlEncodedContent formData = new( new Dictionary<string, string> {
            ["title"] = "",
            ["artist"] = ""
        } );

        // Act
        HttpResponseMessage response = await AntiforgeryTestHelper.PostWithAntiforgeryAsync(
            s_client!, "/Home/LookupResultsByTitle", formData, s_antiforgeryToken!, TestContext.CancellationToken );
        string content = await response.Content.ReadAsStringAsync( TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        Assert.IsTrue( content.Contains( "Title and artist are required", StringComparison.OrdinalIgnoreCase ) );
    }

    /// <summary>
    /// Posts a valid ISRC and verifies the response is 200 OK HTML containing either an embed card or a
    /// "No results found" message (tolerating provider availability).
    /// </summary>
    [TestMethod]
    [TestCategory( "Integration" )] // Requires real API credentials
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout for API call
    public async Task LookupResultsByIsrc_WithValidIsrc_ReturnsHtmlView( ) {
        // Arrange
        FormUrlEncodedContent formData = new( new Dictionary<string, string> {
            ["isrc"] = "QMY951610010"
        } );

        // Act
        HttpResponseMessage response = await AntiforgeryTestHelper.PostWithAntiforgeryAsync(
            s_client!, "/Home/LookupResultsByIsrc", formData, s_antiforgeryToken!, TestContext.CancellationToken );
        string content = await response.Content.ReadAsStringAsync( TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        Assert.AreEqual( "text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString( ) );
        // Content should either contain embed-card or a "No results found" message
        Assert.IsTrue( content.Contains( "embed-card", StringComparison.OrdinalIgnoreCase ) ||
                      content.Contains( "No results found", StringComparison.OrdinalIgnoreCase ) );
    }

    /// <summary>
    /// Posts a valid UPC and verifies the response is 200 OK HTML containing either an embed card or a
    /// "No results found" message (tolerating provider availability).
    /// </summary>
    [TestMethod]
    [TestCategory( "Integration" )] // Requires real API credentials
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout for API call
    public async Task LookupResultsByUpc_WithValidUpc_ReturnsHtmlView( ) {
        // Arrange
        FormUrlEncodedContent formData = new( new Dictionary<string, string> {
            ["upc"] = "00602527513607"
        } );

        // Act
        HttpResponseMessage response = await AntiforgeryTestHelper.PostWithAntiforgeryAsync(
            s_client!, "/Home/LookupResultsByUpc", formData, s_antiforgeryToken!, TestContext.CancellationToken );
        string content = await response.Content.ReadAsStringAsync( TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        Assert.AreEqual( "text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString( ) );
        // Content should either contain embed-card or a "No results found" message
        Assert.IsTrue( content.Contains( "embed-card", StringComparison.OrdinalIgnoreCase ) ||
                      content.Contains( "No results found", StringComparison.OrdinalIgnoreCase ) );
    }

    /// <summary>
    /// Posts a valid title and artist and verifies the response is 200 OK HTML containing either an
    /// embed card or a "No results found" message (tolerating provider availability).
    /// </summary>
    [TestMethod]
    [TestCategory( "Integration" )] // Requires real API credentials
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout for API call
    public async Task LookupResultsByTitle_WithValidTitleAndArtist_ReturnsHtmlView( ) {
        // Arrange
        FormUrlEncodedContent formData = new( new Dictionary<string, string> {
            ["title"] = "The Best Part",
            ["artist"] = "Bien"
        } );

        // Act
        HttpResponseMessage response = await AntiforgeryTestHelper.PostWithAntiforgeryAsync(
            s_client!, "/Home/LookupResultsByTitle", formData, s_antiforgeryToken!, TestContext.CancellationToken );
        string content = await response.Content.ReadAsStringAsync( TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        Assert.AreEqual( "text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString( ) );
        // Content should either contain embed-card or a "No results found" message
        Assert.IsTrue( content.Contains( "embed-card", StringComparison.OrdinalIgnoreCase ) ||
                      content.Contains( "No results found", StringComparison.OrdinalIgnoreCase ) );
    }

    /// <summary>
    /// The MSTest-injected test context, used here to obtain the per-test cancellation token.
    /// </summary>
    public TestContext TestContext { get; set; }
}
