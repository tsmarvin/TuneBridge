using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BridgeBeats.Domain.Contracts.DTOs;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BridgeBeats.Tests.EndToEnd;

/// <summary>
/// End-to-end tests for the oEmbed endpoint functionality.
/// </summary>
[TestClass]
[DoNotParallelize] // Prevent parallel execution to avoid overwhelming external APIs with rate limits
[TestCategory( "EndToEnd" )] // Mark as end-to-end tests
public class OEmbedEndpointTests {
    private static WebApplicationFactory<Program>? s_factory;
    private static HttpClient? s_client;

    [ClassInitialize]
    public static void ClassInitialize( TestContext context ) {
        // Create factory with unique database connection strings and no Discord token
        Dictionary<string, string?> configData = new( ) {
            ["BridgeBeats:SpotifyClientId"] = "test",
            ["BridgeBeats:SpotifyClientSecret"] = "test",
            ["BridgeBeats:DiscordToken"] = null, // Explicitly null to prevent Discord service registration
            ["BridgeBeats:IdentityConnectionString"] = $"Data Source=OEmbed_Identity_{Guid.NewGuid( ):N};Mode=Memory;Cache=Shared",
            ["BridgeBeats:ApiKeySalt"] = "api_key_salt",
            ["BridgeBeats:ATProtoIdentifier"] = "",
            ["BridgeBeats:ATProtoPassword"] = "",
            ["BridgeBeats:LinkCacheConnectionString"] = $"Data Source=OEmbed_LinkCache_{Guid.NewGuid( ):N};Mode=Memory;Cache=Shared",
            ["BridgeBeats:BaseUrl"] = "test.bridgebeats.link",
            ["BridgeBeats:AppleTeamId"] = "test",
            ["BridgeBeats:AppleKeyId"] = "test",
            ["BridgeBeats:AppleKeyPath"] = "/tmp/test.p8"
        };

        // Create a dummy Apple key file for testing
        System.IO.File.WriteAllText( "/tmp/test.p8", "-----BEGIN PRIVATE KEY-----\ntest\n-----END PRIVATE KEY-----" );

        s_factory = new CustomWebApplicationFactory( configData );
        s_client = s_factory.CreateClient( );
    }

    [ClassCleanup]
    public static void ClassCleanup( ) {
        s_factory?.Dispose( );
    }

    [TestMethod]
    [Timeout( 10000, CooperativeCancellation = true )]
    public async Task OEmbed_WithMissingUrl_ReturnsBadRequest( ) {
        // Act
        HttpResponseMessage response = await s_client!.GetAsync(
            "/oembed",
            TestContext.CancellationToken
        );

        // Assert
        Assert.AreEqual( HttpStatusCode.BadRequest, response.StatusCode );

        JsonElement? data = await response.Content.ReadFromJsonAsync<JsonElement>( TestContext.CancellationToken );
        Assert.IsNotNull( data );
        Assert.IsTrue( data!.Value.TryGetProperty( "error", out JsonElement errorElement ) );
        Assert.IsTrue( errorElement.GetString( )!.Contains( "URL parameter is required" ) );
    }

    [TestMethod]
    [Timeout( 10000, CooperativeCancellation = true )]
    public async Task OEmbed_WithInvalidCardUrl_ReturnsBadRequest( ) {
        // Arrange
        string invalidUrl = "https://example.com/not-a-card";

        // Act
        HttpResponseMessage response = await s_client!.GetAsync(
            $"/oembed?url={Uri.EscapeDataString( invalidUrl )}",
            TestContext.CancellationToken
        );

        // Assert
        Assert.AreEqual( HttpStatusCode.BadRequest, response.StatusCode );

        JsonElement? data = await response.Content.ReadFromJsonAsync<JsonElement>( TestContext.CancellationToken );
        Assert.IsNotNull( data );
        Assert.IsTrue( data!.Value.TryGetProperty( "error", out JsonElement errorElement ) );
        Assert.IsTrue( errorElement.GetString( )!.Contains( "Invalid card URL format" ) );
    }

    [TestMethod]
    [Timeout( 10000, CooperativeCancellation = true )]
    public async Task OEmbed_WithNonExistentCardId_ReturnsNotFound( ) {
        // Arrange
        string cardUrl = "https://test.bridgebeats.link/card/non-existent-card-12345";

        // Act
        HttpResponseMessage response = await s_client!.GetAsync(
            $"/oembed?url={Uri.EscapeDataString( cardUrl )}",
            TestContext.CancellationToken
        );

        // Assert
        Assert.AreEqual( HttpStatusCode.NotFound, response.StatusCode );

        JsonElement? data = await response.Content.ReadFromJsonAsync<JsonElement>( TestContext.CancellationToken );
        Assert.IsNotNull( data );
        Assert.IsTrue( data!.Value.TryGetProperty( "error", out JsonElement errorElement ) );
        Assert.IsTrue( errorElement.GetString( )!.Contains( "Card not found or expired" ) );
    }

    [TestMethod]
    [Timeout( 10000, CooperativeCancellation = true )]
    public async Task OEmbed_WithUnsupportedFormat_ReturnsBadRequest( ) {
        // Arrange
        string cardUrl = "https://test.bridgebeats.link/card/test-card-123";

        // Act
        HttpResponseMessage response = await s_client!.GetAsync(
            $"/oembed?url={Uri.EscapeDataString( cardUrl )}&format=xml",
            TestContext.CancellationToken
        );

        // Assert
        Assert.AreEqual( HttpStatusCode.BadRequest, response.StatusCode );

        JsonElement? data = await response.Content.ReadFromJsonAsync<JsonElement>( TestContext.CancellationToken );
        Assert.IsNotNull( data );
        Assert.IsTrue( data!.Value.TryGetProperty( "error", out JsonElement errorElement ) );
        Assert.IsTrue( errorElement.GetString( )!.Contains( "Only JSON format is supported" ) );
    }

    [TestMethod]
    [TestCategory( "Integration" )] // Requires real API credentials
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task OEmbed_WithValidCardFromRealLookup_ReturnsValidOEmbedResponse( ) {
        // Arrange - First create a card by doing a web lookup
        object payload = new { uri = "https://open.spotify.com/track/3n3Ppam7vgaVa1iaRUc9Lp" };

        HttpResponseMessage lookupResponse = await s_client!.PostAsJsonAsync(
            "/lookup/web",
            payload,
            TestContext.CancellationToken
        );

        Assert.AreEqual( HttpStatusCode.OK, lookupResponse.StatusCode );

        JsonElement? lookupData = await lookupResponse.Content.ReadFromJsonAsync<JsonElement>( TestContext.CancellationToken );
        Assert.IsNotNull( lookupData );

        bool hasResults = lookupData!.Value.GetProperty( "hasResults" ).GetBoolean( );

        if (!hasResults) {
            Assert.Inconclusive( "API returned no results - possibly due to rate limiting" );
            return;
        }

        // Extract the card URL from the lookup response
        JsonElement items = lookupData.Value.GetProperty( "items" );
        Assert.IsTrue( items.GetArrayLength( ) > 0 );

        JsonElement firstItem = items[0];
        string? cardUrl = firstItem.GetProperty( "cardUrl" ).GetString( );
        Assert.IsNotNull( cardUrl );
        Assert.IsFalse( string.IsNullOrWhiteSpace( cardUrl ) );

        // Act - Now request the oEmbed data for this card
        HttpResponseMessage oembedResponse = await s_client.GetAsync(
            $"/oembed?url={Uri.EscapeDataString( cardUrl! )}",
            TestContext.CancellationToken
        );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, oembedResponse.StatusCode );

        OEmbedResponse? oembedData = await oembedResponse.Content.ReadFromJsonAsync<OEmbedResponse>( TestContext.CancellationToken );
        Assert.IsNotNull( oembedData );

        // Validate oEmbed response structure
        Assert.AreEqual( "1.0", oembedData!.Version );
        Assert.AreEqual( "rich", oembedData.Type );
        Assert.IsFalse( string.IsNullOrWhiteSpace( oembedData.Title ) );
        Assert.IsFalse( string.IsNullOrWhiteSpace( oembedData.AuthorName ) );
        Assert.AreEqual( "BridgeBeats", oembedData.ProviderName );
        Assert.IsFalse( string.IsNullOrWhiteSpace( oembedData.ProviderUrl ) );
        Assert.IsFalse( string.IsNullOrWhiteSpace( oembedData.Html ) );
        Assert.IsTrue( oembedData.Width > 0 );
        Assert.IsTrue( oembedData.Height > 0 );

        // Validate the HTML contains an iframe
        Assert.IsTrue( oembedData.Html.Contains( "<iframe" ) );
        Assert.IsTrue( oembedData.Html.Contains( "src=" ) );
        Assert.IsTrue( oembedData.Html.Contains( "/embed" ) );
    }

    [TestMethod]
    [TestCategory( "Integration" )] // Requires real API credentials
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task OEmbed_WithMaxDimensions_RespectsRequestedDimensions( ) {
        // Arrange - First create a card by doing a web lookup
        object payload = new { uri = "https://music.apple.com/us/album/chiron/1695231829" };

        HttpResponseMessage lookupResponse = await s_client!.PostAsJsonAsync(
            "/lookup/web",
            payload,
            TestContext.CancellationToken
        );

        Assert.AreEqual( HttpStatusCode.OK, lookupResponse.StatusCode );

        JsonElement? lookupData = await lookupResponse.Content.ReadFromJsonAsync<JsonElement>( TestContext.CancellationToken );
        Assert.IsNotNull( lookupData );

        bool hasResults = lookupData!.Value.GetProperty( "hasResults" ).GetBoolean( );

        if (!hasResults) {
            Assert.Inconclusive( "API returned no results - possibly due to rate limiting" );
            return;
        }

        // Extract the card URL from the lookup response
        JsonElement items = lookupData.Value.GetProperty( "items" );
        Assert.IsTrue( items.GetArrayLength( ) > 0 );

        JsonElement firstItem = items[0];
        string? cardUrl = firstItem.GetProperty( "cardUrl" ).GetString( );
        Assert.IsNotNull( cardUrl );
        Assert.IsFalse( string.IsNullOrWhiteSpace( cardUrl ) );

        const int maxWidth = 400;
        const int maxHeight = 300;

        // Act - Now request the oEmbed data with custom dimensions
        HttpResponseMessage oembedResponse = await s_client.GetAsync(
            $"/oembed?url={Uri.EscapeDataString( cardUrl! )}&maxwidth={maxWidth}&maxheight={maxHeight}",
            TestContext.CancellationToken
        );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, oembedResponse.StatusCode );

        OEmbedResponse? oembedData = await oembedResponse.Content.ReadFromJsonAsync<OEmbedResponse>( TestContext.CancellationToken );
        Assert.IsNotNull( oembedData );

        Assert.AreEqual( maxWidth, oembedData!.Width );
        Assert.AreEqual( maxHeight, oembedData.Height );
        Assert.IsTrue( oembedData.Html!.Contains( $"width=\"{maxWidth}\"" ) );
        Assert.IsTrue( oembedData.Html.Contains( $"height=\"{maxHeight}\"" ) );
    }

    public TestContext TestContext { get; set; } = null!;
}
