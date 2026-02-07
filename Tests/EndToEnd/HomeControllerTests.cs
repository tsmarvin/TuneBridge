using System.Net;
using Microsoft.Extensions.Configuration;

namespace BridgeBeats.Tests.EndToEnd;

/// <summary>
/// End-to-end tests for the HomeController web interface endpoints.
/// </summary>
[TestClass]
[TestCategory( "EndToEnd" )] // Mark as end-to-end tests - these are fast and don't hit external APIs
public class HomeControllerTests {
    private static CustomWebApplicationFactory? s_factory;
    private static HttpClient? s_client;
    private static string? s_antiforgeryToken;

    /// <summary>
    /// Initializes the test factory and HTTP client for all tests in this class.
    /// </summary>
    /// <param name="context">The test context provided by MSTest.</param>
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
    /// Disposes the test factory after all tests in this class have completed.
    /// </summary>
    [ClassCleanup]
    public static void ClassCleanup( ) {
        s_factory?.Dispose( );
    }

    /// <summary>
    /// Verifies that the Index page returns a successful HTTP status code.
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
    /// Verifies that the Index page contains the expected BridgeBeats content.
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
    /// Verifies that the Privacy page returns a successful HTTP status code.
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
    /// Verifies that the Error page returns a successful HTTP status code.
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
    /// Verifies that non-existent routes return a 404 Not Found status code.
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
    /// Verifies that LookupResults returns a message view when URI is empty.
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
    /// Verifies that LookupResultsByIsrc returns a message view when ISRC is empty.
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
    /// Verifies that LookupResultsByUpc returns a message view when UPC is empty.
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
    /// Verifies that LookupResultsByTitle returns a message view when title and artist are empty.
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
    /// Verifies that LookupResultsByIsrc returns an HTML view when a valid ISRC is provided.
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
    /// Verifies that LookupResultsByUpc returns an HTML view when a valid UPC is provided.
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
    /// Verifies that LookupResultsByTitle returns an HTML view when valid title and artist are provided.
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
    /// Gets or sets the test context which provides information about and functionality for the current test run.
    /// </summary>
    public TestContext TestContext { get; set; }
}
