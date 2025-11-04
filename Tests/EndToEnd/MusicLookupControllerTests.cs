using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TuneBridge.Configuration;
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
        MusicLookupController.UrlReq request = new(
 "https://music.apple.com/us/album/bohemian-rhapsody/1440806041"
 );

        // Act
        HttpResponseMessage response = await _client!.PostAsJsonAsync("/music/lookup/urlList", request);

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        List<MediaLinkResult>? results = await response.Content.ReadFromJsonAsync<List<MediaLinkResult>>( );
        Assert.IsNotNull( results );
        Assert.IsTrue( results!.Count > 0, "Results should not be empty" );
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
        Assert.IsTrue( results!.Count > 0, "Results should not be empty" );
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
        Assert.IsTrue( results!.Count > 0, "Results should not be empty" );
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
        Assert.IsTrue( results!.Count > 0, "Results should not be empty" );
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
        Assert.IsTrue( result.Results.Count > 0, "result.Results should not be empty" );
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
        Assert.IsTrue( result.Results.Count > 0, "result.Results should not be empty" );
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
        Assert.IsTrue( result.Results.Count > 0, "result.Results should not be empty" );
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
        Assert.IsTrue( content.Length > 0, "content should not be empty" );
    }

    public void Dispose( ) {
        _client?.Dispose( );
    }
}

/// <summary>
/// Custom web application factory for integration testing.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program> {
    protected override void ConfigureWebHost( IWebHostBuilder builder ) {
        _ = builder.UseEnvironment( "Testing" );

        // Override configuration to inject test values
        _ = builder.ConfigureAppConfiguration( ( context, config ) => {
            // Add in-memory configuration with test values at the end so it overrides appsettings.json
            Dictionary<string, string?> configData = new( ) {
                ["TuneBridge:SpotifyClientId"] = "test",
                ["TuneBridge:SpotifyClientSecret"] = "test",
                ["TuneBridge:DiscordToken"] = string.Empty,
                ["TuneBridge:ConnectionString"] = $"Data Source=Identity_{Guid.NewGuid()};Mode=Memory;Cache=Shared",
                ["TuneBridge:ApiKeySalt"] = "test_api_key_salt",
                ["TuneBridge:BlueskyPdsUrl"] = string.Empty,
                ["TuneBridge:BlueskyIdentifier"] = string.Empty,
                ["TuneBridge:BlueskyPassword"] = string.Empty,
                ["TuneBridge:CacheDbPath"] = $"Data Source=LinkCache_{Guid.NewGuid()};Mode=Memory;Cache=Shared",
            };

            _ = config.AddInMemoryCollection( configData );
        } );

        // Configure test services
        _ = builder.ConfigureServices( services => {
            // Add a fake authentication handler for tests to bypass authorization
            _ = services.AddAuthentication( "Test" )
                .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, TestAuthHandler>( "Test", options => { } );
        } );
    }

    protected void RegisterUser( ) {
        // Use the account controller to register a test user
        //AccountController.

        // Use the account controller to register an api key


        // return the apikey to the caller to be used in future requests.

    }
}

/// <summary>
/// Test authentication handler that always succeeds for integration tests.
/// </summary>
public class TestAuthHandler : Microsoft.AspNetCore.Authentication.AuthenticationHandler<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions> {
    public TestAuthHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder )
        : base( options, logger, encoder ) {
    }

    protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync( ) {
        System.Security.Claims.Claim[] claims = [ new System.Security.Claims.Claim( System.Security.Claims.ClaimTypes.Name, "TestUser" ) ];
        System.Security.Claims.ClaimsIdentity identity = new( claims, "Test" );
        System.Security.Claims.ClaimsPrincipal principal = new( identity );
        Microsoft.AspNetCore.Authentication.AuthenticationTicket ticket = new( principal, "Test" );

        Microsoft.AspNetCore.Authentication.AuthenticateResult result = Microsoft.AspNetCore.Authentication.AuthenticateResult.Success( ticket );

        return Task.FromResult( result );
    }
}
