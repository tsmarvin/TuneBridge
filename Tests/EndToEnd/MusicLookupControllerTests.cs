using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Web.Controllers;

namespace TuneBridge.Tests.EndToEnd;

/// <summary>
/// End-to-end tests for the MusicLookupController API endpoints.
/// These tests verify the full request/response cycle including routing, serialization, and service integration.
/// </summary>
[TestClass]
public class MusicLookupControllerTests {
    private static WebApplicationFactory<Program>? _factory;
    private static HttpClient? _client;

    [ClassInitialize]
    public static void ClassInitialize( TestContext context ) {
        _factory = new CustomWebApplicationFactory( );
        _client = _factory.CreateClient( );
    }

    [ClassCleanup]
    public static void ClassCleanup( ) {
        _client?.Dispose( );
        _factory?.Dispose( );
    }

    [TestMethod]
    public async Task ByUrlList_WithValidAppleMusicUrl_ReturnsOkWithResults( ) {
        // Arrange
        MusicLookupController.UrlReq request = new("https://music.apple.com/us/album/bohemian-rhapsody/1440806041");

        // Act
        HttpResponseMessage response = await _client!.PostAsJsonAsync("/music/lookup/urlList", request);

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        List<MediaLinkResult>? results = await response.Content.ReadFromJsonAsync<List<MediaLinkResult>>( );
        Assert.IsNotNull( results );
        Assert.IsNotEmpty( results, "Results should not be empty" );
    }

    [TestMethod]
    public async Task ByUrlList_WithValidSpotifyUrl_ReturnsOkWithResults( ) {
        // Arrange
        MusicLookupController.UrlReq request = new( "https://open.spotify.com/album/6i6folBtxKV28WX3msQ4FE"  );

        // Act
        HttpResponseMessage response = await _client!.PostAsJsonAsync("/music/lookup/urlList", request);

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        List<MediaLinkResult>? results = await response.Content.ReadFromJsonAsync<List<MediaLinkResult>>( );
        Assert.IsNotNull( results );
        Assert.IsNotEmpty( results, "Results should not be empty" );
    }

    [TestMethod]
    public async Task ByUrlList_WithValidTidalUrl_ReturnsOkWithResults( ) {
        // Arrange
        MusicLookupController.UrlReq request = new(  "https://tidal.com/track/96572657"  );

        // Act
        HttpResponseMessage response = await _client!.PostAsJsonAsync("/music/lookup/urlList", request);

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        List<MediaLinkResult>? results = await response.Content.ReadFromJsonAsync<List<MediaLinkResult>>( );
        Assert.IsNotNull( results );
        Assert.IsNotEmpty( results, "Results should not be empty" );
    }

    [TestMethod]
    public async Task ByUrlList_WithMultipleUrls_ReturnsMultipleResults( ) {
        // Arrange
        MusicLookupController.UrlReq request = new(
          "https://open.spotify.com/album/6X9k3hgEYTUx6tD5FVx7hq " +
          "https://music.apple.com/us/album/a-night-at-the-opera-deluxe-remastered-version/1440806041"
        );

        // Act
        HttpResponseMessage response = await _client!.PostAsJsonAsync("/music/lookup/urlList", request);

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        List<MediaLinkResult>? results = await response.Content.ReadFromJsonAsync<List<MediaLinkResult>>( );
        Assert.IsNotNull( results );
        // Should have at least one result (deduplication may occur if URLs point to same content)
        Assert.IsNotEmpty( results, "Results should not be empty" );
    }

    [TestMethod]
    public async Task ByIsrc_WithValidIsrc_ReturnsOkWithResult( ) {

        // Arrange
        MusicLookupController.IsrcReq request = new( "GBUM71029604" );

        // Act
        HttpResponseMessage response = await _client!.PostAsJsonAsync("/music/lookup/isrc", request);

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        MediaLinkResult? result = await response.Content.ReadFromJsonAsync<MediaLinkResult>( );
        Assert.IsNotNull( result );
        Assert.IsNotEmpty( result.Results, "result.Results should not be empty" );
    }

    [TestMethod]
    public async Task ByUpc_WithValidUpc_ReturnsOkWithResult( ) {

        // Arrange
        MusicLookupController.UpcReq request = new( "00602547202307" );

        // Act
        HttpResponseMessage response = await _client!.PostAsJsonAsync("/music/lookup/upc", request);

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        MediaLinkResult? result = await response.Content.ReadFromJsonAsync<MediaLinkResult>( );
        Assert.IsNotNull( result );
        Assert.IsNotEmpty( result.Results, "result.Results should not be empty" );
    }

    [TestMethod]
    public async Task ByTitle_WithValidTitleAndArtist_ReturnsOkWithResult( ) {

        // Arrange
        MusicLookupController.TitleReq request = new( "Bohemian Rhapsody", "Queen" );

        // Act
        HttpResponseMessage response = await _client!.PostAsJsonAsync("/music/lookup/title", request);

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        MediaLinkResult? result = await response.Content.ReadFromJsonAsync<MediaLinkResult>( );
        Assert.IsNotNull( result );
        Assert.IsNotEmpty(result.Results, "result.Results should not be empty");
    }

    [TestMethod]
    public async Task ByUrl_StreamingEndpoint_ReturnsResults( ) {

        // Arrange
        MusicLookupController.UrlReq request = new(  "https://open.spotify.com/track/4u7EnebtmKWzUH433cf5Qv"  );

        // Act
        HttpResponseMessage response = await _client!.PostAsJsonAsync("/music/lookup/url", request);

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        string content = await response.Content.ReadAsStringAsync();
        Assert.IsGreaterThan( 0, content.Length, "content should not be empty" );
    }

    public void Dispose( ) {
        _client?.Dispose( );
    }
}

/// <summary>
/// Custom web application factory for integration testing.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program> {
    private readonly Dictionary<string, string?>? _configOverrides;

    public CustomWebApplicationFactory( ) { }

    public CustomWebApplicationFactory( Dictionary<string, string?> configOverrides ) {
        _configOverrides = configOverrides;
    }

    protected override void ConfigureWebHost( IWebHostBuilder builder ) {
        // Default test configuration (Spotify only) unless overrides are provided
        Dictionary<string, string?> configData = _configOverrides ?? new( ) {
            ["TuneBridge:SpotifyClientId"] = "test",
            ["TuneBridge:SpotifyClientSecret"] = "test",
            ["TuneBridge:DiscordToken"] = string.Empty,
            ["TuneBridge:ConnectionString"] = $"Data Source=Identity;Mode=Memory",
            ["TuneBridge:ApiKeySalt"] = "api_key_salt",
            ["TuneBridge:BlueskyPdsUrl"] = string.Empty,
            ["TuneBridge:BlueskyIdentifier"] = string.Empty,
            ["TuneBridge:BlueskyPassword"] = string.Empty,
            ["TuneBridge:CacheDbPath"] = "Data Source=LinkCache;Mode=Memory;Cache=Shared",
        };
        _ = builder.UseEnvironment( "Testing" );
        //_ = builder.ConfigureTuneBridgeServices( services, configuration );

        _ = builder.ConfigureAppConfiguration( ( context, config ) => {
            _ = config.AddInMemoryCollection( configData );
        } );
    }
}
