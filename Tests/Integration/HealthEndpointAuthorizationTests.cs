using System.Net;
using FluentAssertions;
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

    [ClassInitialize]
    public static async Task Setup( TestContext testContext ) {
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
        configData["BridgeBeats:DiscordToken"] = null;

        s_factory = new CustomWebApplicationFactory( configData );
        s_client = s_factory.CreateClient( );

        // Initialize databases after services are configured
        await s_factory.InitializeDatabasesAsync( );
    }

    [ClassCleanup]
    public static void Cleanup( ) {
        s_factory?.Dispose( );
    }

    // TODO: Fix authorization connection in tests here and then reenable these tests.
    ///// <summary>
    ///// Tests that the health endpoint is accessible (the test environment appears as localhost).
    ///// In production, this would be restricted to internal Docker network IPs.
    ///// </summary>
    //[TestMethod]
    //public async Task HealthEndpoint_FromTestClient_ReturnsOk( ) {
    //    // Arrange
    //    // The test client appears as localhost/internal to the middleware
    //
    //    // Act
    //    HttpResponseMessage response = await _client!.GetAsync( "/health" );
    //
    //    // Assert
    //    _ = response.StatusCode.Should( ).Be( HttpStatusCode.OK );
    //}
    //
    ///// <summary>
    ///// Tests that the health endpoint returns JSON with expected structure.
    ///// </summary>
    //[TestMethod]
    //public async Task HealthEndpoint_ReturnsValidJson( ) {
    //    // Arrange & Act
    //    HttpResponseMessage response = await _client!.GetAsync( "/health" );
    //    string content = await response.Content.ReadAsStringAsync( );
    //
    //    // Assert
    //    _ = response.StatusCode.Should( ).Be( HttpStatusCode.OK );
    //    _ = content.Should( ).Contain( "healthy" );
    //    _ = content.Should( ).Contain( "timestamp" );
    //}

    /// <summary>
    /// Tests that non-health endpoints are not affected by the health endpoint middleware.
    /// </summary>
    [TestMethod]
    public async Task NonHealthEndpoint_NotAffectedByMiddleware( ) {
        // Arrange & Act
        HttpResponseMessage response = await s_client!.GetAsync( "/", TestContext.CancellationToken );

        // Assert
        _ = response.StatusCode.Should( ).Be( HttpStatusCode.OK );
    }

    public TestContext TestContext { get; set; }
}
