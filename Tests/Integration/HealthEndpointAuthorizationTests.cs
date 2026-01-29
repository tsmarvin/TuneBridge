using System.Net;
using Microsoft.Extensions.Configuration;

namespace BridgeBeats.Tests.Integration;

/// <summary>
/// Integration tests for health endpoint authorization middleware.
/// </summary>
[TestClass]
[TestCategory( "Integration" )]
public class HealthEndpointAuthorizationTests {
    private static CustomWebApplicationFactory? s_factory;
    private static HttpClient? s_client;

    /// <summary>
    /// Initializes the test factory and HTTP client for all tests in this class.
    /// </summary>
    /// <param name="_">The test context provided by MSTest (unused).</param>
    [ClassInitialize]
    public static async Task Setup( TestContext _ ) {
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
        configData["BridgeBeats:ATProtoIdentifier"] = string.Empty;
        configData["BridgeBeats:ATProtoPassword"] = string.Empty;
        configData["BridgeBeats:ATProtoUserDID"] = string.Empty;

        s_factory = new CustomWebApplicationFactory( configData );
        s_client = s_factory.CreateClient( );

        // Initialize databases after services are configured
        await s_factory.InitializeDatabasesAsync( );
    }

    /// <summary>
    /// Disposes the test factory after all tests in this class have completed.
    /// </summary>
    [ClassCleanup]
    public static void Cleanup( ) {
        s_factory?.Dispose( );
    }

    /// <summary>
    /// Tests that the health endpoint is accessible (the test environment appears as localhost).
    /// In production, this would be restricted to internal Docker network IPs.
    /// </summary>
    [TestMethod]
    public async Task HealthEndpoint_FromTestClient_ReturnsOk( ) {
        // Arrange
        // The test client appears as localhost/internal to the middleware

        // Act
        HttpResponseMessage response = await s_client!.GetAsync( "/health" );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
    }

    /// <summary>
    /// Tests that the health endpoint returns JSON with expected structure.
    /// </summary>
    [TestMethod]
    public async Task HealthEndpoint_ReturnsValidJson( ) {
        // Arrange & Act
        HttpResponseMessage response = await s_client!.GetAsync( "/health" );
        string content = await response.Content.ReadAsStringAsync( );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        Assert.Contains( "healthy", content );
        Assert.Contains( "timestamp", content );
    }

    /// <summary>
    /// Tests that non-health endpoints are not affected by the health endpoint middleware.
    /// </summary>
    [TestMethod]
    public async Task NonHealthEndpoint_NotAffectedByMiddleware( ) {
        // Arrange & Act
        HttpResponseMessage response = await s_client!.GetAsync( "/", TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
    }

    /// <summary>
    /// Gets or sets the test context which provides information about and functionality for the current test run.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;
}
