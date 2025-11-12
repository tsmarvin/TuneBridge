using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Implementations.Auth;
using TuneBridge.Domain.Implementations.Database;
using TuneBridge.Domain.Models;
using TuneBridge.Web.Controllers;

namespace TuneBridge.Tests.EndToEnd;

/// <summary>
/// End-to-end tests for the MusicLookupController API endpoints.
/// These tests verify the full request/response cycle including routing, serialization, and service integration.
/// </summary>
[TestClass]
public class MusicLookupControllerTests {
    private static WebApplicationFactory<Program>? s_factory;
    private static HttpClient? s_client;
    private static string? s_apiKey;

    [ClassInitialize]
    public static async Task ClassInitialize( TestContext context ) {
        s_factory = new CustomWebApplicationFactory( );
        
        // Create a test user and API key
        using IServiceScope scope = s_factory.Services.CreateScope();
        IServiceProvider services = scope.ServiceProvider;
        
        UserManager<ApplicationUser> userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        ApiKeyHasher hasher = services.GetRequiredService<ApiKeyHasher>();
        
        // Try to find existing test user
        ApplicationUser? existingUser = await userManager.FindByEmailAsync( "testuser@test.com" );
        
        string rawApiKey;
        if (existingUser != null && !string.IsNullOrEmpty( existingUser.ApiKeyHash )) {
            // User already exists - we can't retrieve the original key, so generate a new one
            rawApiKey = Guid.NewGuid( ).ToString( );
            string hashedKey = hasher.HashApiKey( rawApiKey );
            existingUser.ApiKeyHash = hashedKey;
            _ = await userManager.UpdateAsync( existingUser );
        } else if (existingUser == null) {
            // Generate and hash API key
            rawApiKey = Guid.NewGuid( ).ToString( );
            string hashedKey = hasher.HashApiKey( rawApiKey );
            
            // Create test user with API key
            ApplicationUser testUser = new( ) {
                UserName = "testuser@test.com",
                Email = "testuser@test.com",
                ApiKeyHash = hashedKey
            };
            IdentityResult result = await userManager.CreateAsync( testUser, "TestPassword123!" );
            
            if (!result.Succeeded) {
                throw new Exception( $"Failed to create test user: {string.Join( ", ", result.Errors.Select( e => e.Description ) )}" );
            }
        } else {
            throw new Exception( "Test user exists but has no API key" );
        }
        
        s_apiKey = rawApiKey;
        
        // Create HTTP client and set API key header
        s_client = s_factory.CreateClient( );
        s_client.DefaultRequestHeaders.Add( "X-API-Key", s_apiKey );
    }

    [ClassCleanup]
    public static void ClassCleanup( ) {
        s_client?.Dispose( );
        s_factory?.Dispose( );
    }

    [TestMethod]
    public async Task ByUrlList_WithValidAppleMusicUrl_ReturnsOkWithResults( ) {
        // Arrange
        MusicLookupController.UrlReq request = new("https://music.apple.com/us/album/bohemian-rhapsody/1440806041");

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync("/music/lookup/urlList", request, cancellationToken: TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        List<MediaLinkResult>? results = await response.Content.ReadFromJsonAsync<List<MediaLinkResult>>(TestContext.CancellationToken);
        Assert.IsNotNull( results );
        Assert.IsNotEmpty( results, "Results should not be empty" );
    }

    [TestMethod]
    public async Task ByUrlList_WithValidSpotifyUrl_ReturnsOkWithResults( ) {
        // Arrange
        MusicLookupController.UrlReq request = new( "https://open.spotify.com/album/6i6folBtxKV28WX3msQ4FE"  );

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync("/music/lookup/urlList", request, cancellationToken: TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        List<MediaLinkResult>? results = await response.Content.ReadFromJsonAsync<List<MediaLinkResult>>(TestContext.CancellationToken);
        Assert.IsNotNull( results );
        Assert.IsNotEmpty( results, "Results should not be empty" );
    }

    [TestMethod]
    public async Task ByUrlList_WithValidTidalUrl_ReturnsOkWithResults( ) {
        // Arrange
        MusicLookupController.UrlReq request = new(  "https://tidal.com/track/96572657"  );

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync("/music/lookup/urlList", request, cancellationToken: TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        List<MediaLinkResult>? results = await response.Content.ReadFromJsonAsync<List<MediaLinkResult>>(TestContext.CancellationToken);
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
        HttpResponseMessage response = await s_client!.PostAsJsonAsync("/music/lookup/urlList", request, cancellationToken: TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        List<MediaLinkResult>? results = await response.Content.ReadFromJsonAsync<List<MediaLinkResult>>(TestContext.CancellationToken);
        Assert.IsNotNull( results );
        // Should have at least one result (deduplication may occur if URLs point to same content)
        Assert.IsNotEmpty( results, "Results should not be empty" );
    }

    [TestMethod]
    public async Task ByIsrc_WithValidIsrc_ReturnsOkWithResult( ) {

        // Arrange
        MusicLookupController.IsrcReq request = new( "GBUM71029604" );

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync("/music/lookup/isrc", request, cancellationToken: TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        MediaLinkResult? result = await response.Content.ReadFromJsonAsync<MediaLinkResult>(TestContext.CancellationToken);
        Assert.IsNotNull( result );
        Assert.IsNotEmpty( result.Results, "result.Results should not be empty" );
    }

    [TestMethod]
    public async Task ByUpc_WithValidUpc_ReturnsOkWithResult( ) {

        // Arrange
        MusicLookupController.UpcReq request = new( "00602547202307" );

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync("/music/lookup/upc", request, cancellationToken: TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        MediaLinkResult? result = await response.Content.ReadFromJsonAsync<MediaLinkResult>(TestContext.CancellationToken);
        Assert.IsNotNull( result );
        Assert.IsNotEmpty( result.Results, "result.Results should not be empty" );
    }

    [TestMethod]
    public async Task ByTitle_WithValidTitleAndArtist_ReturnsOkWithResult( ) {

        // Arrange
        MusicLookupController.TitleReq request = new( "Bohemian Rhapsody", "Queen" );

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync("/music/lookup/title", request, cancellationToken: TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        MediaLinkResult? result = await response.Content.ReadFromJsonAsync<MediaLinkResult>(TestContext.CancellationToken);
        Assert.IsNotNull( result );
        Assert.IsNotEmpty( result.Results, "result.Results should not be empty" );
    }

    [TestMethod]
    public async Task ByUrl_StreamingEndpoint_ReturnsResults( ) {

        // Arrange
        MusicLookupController.UrlReq request = new(  "https://open.spotify.com/track/4u7EnebtmKWzUH433cf5Qv"  );

        // Act
        HttpResponseMessage response = await s_client!.PostAsJsonAsync("/music/lookup/url", request, cancellationToken: TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        string content = await response.Content.ReadAsStringAsync( TestContext.CancellationToken );
        Assert.IsGreaterThan( 0, content.Length, "content should not be empty" );
    }

    public void Dispose( ) => s_client?.Dispose( );

    public TestContext TestContext { get; set; }
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
            ["TuneBridge:IdentityConnectionString"] = $"Data Source=Identity;Mode=Memory",
            ["TuneBridge:ApiKeySalt"] = "api_key_salt",
            ["TuneBridge:BlueskyPdsUrl"] = string.Empty,
            ["TuneBridge:BlueskyIdentifier"] = string.Empty,
            ["TuneBridge:BlueskyPassword"] = string.Empty,
            ["TuneBridge:LinkCacheConnectionString"] = "Data Source=LinkCache;Mode=Memory;Cache=Shared",
        };
        _ = builder.UseEnvironment( "Testing" );

        _ = builder.ConfigureAppConfiguration( ( context, config ) => {
            _ = config.AddInMemoryCollection( configData );
        } );
    }
}
