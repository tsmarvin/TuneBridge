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
    [Timeout( 10000, CooperativeCancellation = true )] // 10 second timeout - should be fast
    public async Task Index_ReturnsSuccessStatusCode( ) {
        // Act
        HttpResponseMessage response = await s_client!.GetAsync("/", TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        Assert.AreEqual( "text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString( ) );
    }

    [TestMethod]
    [Timeout( 10000, CooperativeCancellation = true )] // 10 second timeout - should be fast
    public async Task Index_ContainsExpectedContent( ) {
        // Act
        HttpResponseMessage response = await s_client!.GetAsync("/", TestContext.CancellationToken );
        string content = await response.Content.ReadAsStringAsync( TestContext.CancellationToken );

        // Assert
        Assert.IsTrue( content.Contains( "BridgeBeats", StringComparison.OrdinalIgnoreCase ) );
    }

    [TestMethod]
    [Timeout( 10000, CooperativeCancellation = true )] // 10 second timeout - should be fast
    public async Task Privacy_ReturnsSuccessStatusCode( ) {
        // Act
        HttpResponseMessage response = await s_client!.GetAsync("/Home/Privacy", TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
    }

    [TestMethod]
    [Timeout( 10000, CooperativeCancellation = true )] // 10 second timeout - should be fast
    public async Task Error_ReturnsSuccessStatusCode( ) {
        // Act
        HttpResponseMessage response = await s_client!.GetAsync("/Home/Error", TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
    }

    [TestMethod]
    [Timeout( 10000, CooperativeCancellation = true )] // 10 second timeout - should be fast
    public async Task NonExistentRoute_ReturnsNotFound( ) {
        // Act
        HttpResponseMessage response = await s_client!.GetAsync("/NonExistent/Route", TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.NotFound, response.StatusCode );
    }

    [TestMethod]
    [Timeout( 10000, CooperativeCancellation = true )] // 10 second timeout - should be fast
    public async Task LookupResults_WithEmptyUri_ReturnsMessageView( ) {
        // Arrange
        FormUrlEncodedContent formData = new( new Dictionary<string, string> {
            ["uri"] = ""
        } );

        // Act
        HttpResponseMessage response = await s_client!.PostAsync("/Home/LookupResults", formData, TestContext.CancellationToken );
        string content = await response.Content.ReadAsStringAsync( TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        Assert.IsTrue( content.Contains( "URI is required", StringComparison.OrdinalIgnoreCase ) );
    }

    [TestMethod]
    [Timeout( 10000, CooperativeCancellation = true )] // 10 second timeout - should be fast
    public async Task LookupResultsByIsrc_WithEmptyIsrc_ReturnsMessageView( ) {
        // Arrange
        FormUrlEncodedContent formData = new( new Dictionary<string, string> {
            ["isrc"] = ""
        } );

        // Act
        HttpResponseMessage response = await s_client!.PostAsync("/Home/LookupResultsByIsrc", formData, TestContext.CancellationToken );
        string content = await response.Content.ReadAsStringAsync( TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        Assert.IsTrue( content.Contains( "ISRC is required", StringComparison.OrdinalIgnoreCase ) );
    }

    [TestMethod]
    [Timeout( 10000, CooperativeCancellation = true )] // 10 second timeout - should be fast
    public async Task LookupResultsByUpc_WithEmptyUpc_ReturnsMessageView( ) {
        // Arrange
        FormUrlEncodedContent formData = new( new Dictionary<string, string> {
            ["upc"] = ""
        } );

        // Act
        HttpResponseMessage response = await s_client!.PostAsync("/Home/LookupResultsByUpc", formData, TestContext.CancellationToken );
        string content = await response.Content.ReadAsStringAsync( TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        Assert.IsTrue( content.Contains( "UPC is required", StringComparison.OrdinalIgnoreCase ) );
    }

    [TestMethod]
    [Timeout( 10000, CooperativeCancellation = true )] // 10 second timeout - should be fast
    public async Task LookupResultsByTitle_WithEmptyTitleAndArtist_ReturnsMessageView( ) {
        // Arrange
        FormUrlEncodedContent formData = new( new Dictionary<string, string> {
            ["title"] = "",
            ["artist"] = ""
        } );

        // Act
        HttpResponseMessage response = await s_client!.PostAsync("/Home/LookupResultsByTitle", formData, TestContext.CancellationToken );
        string content = await response.Content.ReadAsStringAsync( TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        Assert.IsTrue( content.Contains( "Title and artist are required", StringComparison.OrdinalIgnoreCase ) );
    }

    [TestMethod]
    [TestCategory( "Integration" )] // Requires real API credentials
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout for API call
    public async Task LookupResultsByIsrc_WithValidIsrc_ReturnsHtmlView( ) {
        // Arrange
        FormUrlEncodedContent formData = new( new Dictionary<string, string> {
            ["isrc"] = "QMY951610010"
        } );

        // Act
        HttpResponseMessage response = await s_client!.PostAsync("/Home/LookupResultsByIsrc", formData, TestContext.CancellationToken );
        string content = await response.Content.ReadAsStringAsync( TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        Assert.AreEqual( "text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString( ) );
        // Content should either contain embed-card or a "No results found" message
        Assert.IsTrue( content.Contains( "embed-card", StringComparison.OrdinalIgnoreCase ) ||
                      content.Contains( "No results found", StringComparison.OrdinalIgnoreCase ) );
    }

    [TestMethod]
    [TestCategory( "Integration" )] // Requires real API credentials
    [Timeout( 30000, CooperativeCancellation = true )] // 30 second timeout for API call
    public async Task LookupResultsByUpc_WithValidUpc_ReturnsHtmlView( ) {
        // Arrange
        FormUrlEncodedContent formData = new( new Dictionary<string, string> {
            ["upc"] = "00602527513607"
        } );

        // Act
        HttpResponseMessage response = await s_client!.PostAsync("/Home/LookupResultsByUpc", formData, TestContext.CancellationToken );
        string content = await response.Content.ReadAsStringAsync( TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        Assert.AreEqual( "text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString( ) );
        // Content should either contain embed-card or a "No results found" message
        Assert.IsTrue( content.Contains( "embed-card", StringComparison.OrdinalIgnoreCase ) ||
                      content.Contains( "No results found", StringComparison.OrdinalIgnoreCase ) );
    }

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
        HttpResponseMessage response = await s_client!.PostAsync("/Home/LookupResultsByTitle", formData, TestContext.CancellationToken );
        string content = await response.Content.ReadAsStringAsync( TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        Assert.AreEqual( "text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString( ) );
        // Content should either contain embed-card or a "No results found" message
        Assert.IsTrue( content.Contains( "embed-card", StringComparison.OrdinalIgnoreCase ) ||
                      content.Contains( "No results found", StringComparison.OrdinalIgnoreCase ) );
    }

    public TestContext TestContext { get; set; }
}
