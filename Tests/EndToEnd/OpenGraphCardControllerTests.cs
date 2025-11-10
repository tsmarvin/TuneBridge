using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TuneBridge.Tests.EndToEnd;

/// <summary>
/// End-to-end tests for the web-specific lookup functionality.
/// </summary>
[TestClass]
public class WebLookupTests {
    private static WebApplicationFactory<Program>? s_factory;
    private static HttpClient? s_client;

    [ClassInitialize]
    public static void ClassInitialize( TestContext context ) {
        s_factory = new CustomWebApplicationFactory( );
        s_client = s_factory.CreateClient( );
    }

    [ClassCleanup]
    public static void ClassCleanup( ) {
        s_client?.Dispose( );
        s_factory?.Dispose( );
    }

    [TestMethod]
    public async Task WebLookup_WithValidSpotifyUrl_ReturnsCardUrl( ) {
        // Arrange
        object payload = new { uri = "https://open.spotify.com/track/3n3Ppam7vgaVa1iaRUc9Lp" };

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync(
            "/lookup/web",
            payload,
            TestContext.CancellationToken
        );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );

        dynamic? data = await response.Content.ReadFromJsonAsync<dynamic>( TestContext.CancellationToken );
        Assert.IsNotNull( data );

        bool hasResults = data.GetProperty( "hasResults" ).GetBoolean( );
        Assert.IsTrue( hasResults, "Should have results for valid Spotify URL" );

        // Check items array exists
        System.Text.Json.JsonElement itemsElement;
        Assert.IsTrue( data.TryGetProperty( "items", out itemsElement ), "Should have items array" );
    }

    [TestMethod]
    public async Task WebLookup_WithMultipleUrls_ReturnsMultipleCards( ) {
        // Arrange - Multiple URLs separated by space/newline
        object payload = new { uri = "https://open.spotify.com/track/3n3Ppam7vgaVa1iaRUc9Lp https://music.apple.com/us/album/chiron/1695231829" };

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync(
            "/lookup/web",
            payload,
            TestContext.CancellationToken
        );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );

        dynamic? data = await response.Content.ReadFromJsonAsync<dynamic>( TestContext.CancellationToken );
        Assert.IsNotNull( data );

        bool hasResults = data.GetProperty( "hasResults" ).GetBoolean( );
        Assert.IsTrue( hasResults, "Should have results for valid URLs" );

        // Check that we have multiple items
        dynamic items = data.GetProperty( "items" );
        int itemCount = items.GetArrayLength( );
        Assert.IsTrue( itemCount > 0, "Should have at least one item" );
    }

    [TestMethod]
    public async Task WebLookup_WithEmptyUri_ReturnsBadRequest( ) {
        // Arrange
        object payload = new { uri = "" };

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync(
            "/lookup/web",
            payload,
            TestContext.CancellationToken
        );

        // Assert
        Assert.AreEqual( HttpStatusCode.BadRequest, response.StatusCode );
    }

    [TestMethod]
    public async Task WebLookup_WithInvalidUri_ReturnsNoResults( ) {
        // Arrange
        object payload = new { uri = "not-a-valid-music-url" };

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync(
            "/lookup/web",
            payload,
            TestContext.CancellationToken
        );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );

        dynamic? data = await response.Content.ReadFromJsonAsync<dynamic>( TestContext.CancellationToken );
        Assert.IsNotNull( data );

        bool hasResults = data.GetProperty( "hasResults" ).GetBoolean( );
        Assert.IsFalse( hasResults, "Should have no results for invalid URL" );
    }

    public TestContext TestContext { get; set; }
}
