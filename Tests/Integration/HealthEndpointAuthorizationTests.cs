using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

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
        _factory = new WebApplicationFactory<Program>( );
        _client = _factory.CreateClient( );
    }

    [ClassCleanup]
    public static void Cleanup( ) {
        _factory?.Dispose( );
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
        HttpResponseMessage response = await _client!.GetAsync( "/" );

        // Assert
        _ = response.StatusCode.Should( ).Be( HttpStatusCode.OK );
    }
}
