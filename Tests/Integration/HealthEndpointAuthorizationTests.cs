using System.Net;

namespace BridgeBeats.Tests.Integration;

/// <summary>
/// Integration tests for health endpoint authorization middleware.
/// </summary>
[TestClass]
[TestCategory( "Integration" )]
public class HealthEndpointAuthorizationTests {
    private static CustomWebApplicationFactory? s_factory;
    private static HttpClient? s_client;

    [ClassInitialize]
    public static async Task Setup( TestContext testContext ) {
        // Use minimal config - let factory provide test defaults for Spotify
        Dictionary<string, string?> configData = new( ) {
            // Force Discord token to empty string to prevent Discord service registration
            ["BridgeBeats:DiscordToken"] = ""
        };

        s_factory = new CustomWebApplicationFactory( configData );
        s_client = s_factory.CreateClient( );

        // Initialize databases after services are configured
        await s_factory.InitializeDatabasesAsync( );
    }

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
        Assert.IsTrue( content.Contains( "healthy" ) );
        Assert.IsTrue( content.Contains( "timestamp" ) );
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

    public TestContext TestContext { get; set; } = null!;
}
