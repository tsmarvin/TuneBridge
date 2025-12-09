using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace BridgeBeats.Tests.Integration;

/// <summary>
/// Integration tests for health endpoint authorization middleware.
/// </summary>
[TestClass]
public class HealthEndpointAuthorizationTests {
    private static WebApplicationFactory<Program>? _factory;
    private static HttpClient? _client;

    [ClassInitialize]
    public static void Setup( TestContext testContext ) {
        _factory = new WebApplicationFactory<Program>( )
            .WithWebHostBuilder( builder => {
                _ = builder.UseEnvironment( "Testing" );
                _ = builder.ConfigureAppConfiguration( ( context, config ) => {
                    // Don't clear - just add our config with high priority
                    Dictionary<string, string?> configData = new( ) {
                        ["BridgeBeats:SpotifyClientId"] = "test",
                        ["BridgeBeats:SpotifyClientSecret"] = "test",
                        ["BridgeBeats:DiscordToken"] = null, // Explicitly null to prevent Discord service registration
                        ["BridgeBeats:IdentityConnectionString"] = $"Data Source=Health_Identity_{Guid.NewGuid():N};Mode=Memory;Cache=Shared",
                        ["BridgeBeats:ApiKeySalt"] = "api_key_salt",
                        ["BridgeBeats:ATProtoIdentifier"] = "",
                        ["BridgeBeats:ATProtoPassword"] = "",
                        ["BridgeBeats:LinkCacheConnectionString"] = $"Data Source=Health_LinkCache_{Guid.NewGuid():N};Mode=Memory;Cache=Shared",
                    };
                    _ = config.AddInMemoryCollection( configData );
                } );
            } );
        _client = _factory.CreateClient( );
    }

    [ClassCleanup]
    public static void Cleanup( ) {
        _factory?.Dispose( );
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
        HttpResponseMessage response = await _client!.GetAsync( "/health" );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
    }

    /// <summary>
    /// Tests that the health endpoint returns JSON with expected structure.
    /// </summary>
    [TestMethod]
    public async Task HealthEndpoint_ReturnsValidJson( ) {
        // Arrange & Act
        HttpResponseMessage response = await _client!.GetAsync( "/health" );
        string content = await response.Content.ReadAsStringAsync( );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        Assert.IsTrue( content.Contains( "healthy", StringComparison.OrdinalIgnoreCase ) );
        Assert.IsTrue( content.Contains( "timestamp", StringComparison.OrdinalIgnoreCase ) );
    }

    /// <summary>
    /// Tests that non-health endpoints are not affected by the health endpoint middleware.
    /// </summary>
    [TestMethod]
    public async Task NonHealthEndpoint_NotAffectedByMiddleware( ) {
        // Arrange & Act
        HttpResponseMessage response = await _client!.GetAsync( "/" );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
    }
}
