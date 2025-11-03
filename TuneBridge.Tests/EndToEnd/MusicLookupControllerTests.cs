using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Web.Controllers;

namespace TuneBridge.Tests.EndToEnd;

/// <summary>
/// End-to-end tests for the MusicLookupController API endpoints.
/// These tests verify the full request/response cycle including routing, serialization, and service integration.
/// </summary>
[TestClass]
public class MusicLookupControllerTests
{
    private static WebApplicationFactory<Program>? _factory;
    private static HttpClient? _client;

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        _factory = new CustomWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    [TestMethod]
    public async Task ByUrlList_WithValidAppleMusicUrl_ReturnsOkWithResults()
    {
        // Arrange
        var request = new MusicLookupController.UrlReq(
            "https://music.apple.com/us/album/bohemian-rhapsody/1440806041"
        );

        // Act
        var response = await _client!.PostAsJsonAsync("/music/lookup/urlList", request);

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<List<MediaLinkResult>>();
        Assert.IsNotNull(results);
        Assert.IsTrue(results!.Count > 0, "Results should not be empty");
    }

    [TestMethod]
    public async Task ByUrlList_WithValidSpotifyUrl_ReturnsOkWithResults()
    {
        // Arrange
        var request = new MusicLookupController.UrlReq(
            "https://open.spotify.com/album/6i6folBtxKV28WX3msQ4FE"
        );

        // Act
        var response = await _client!.PostAsJsonAsync("/music/lookup/urlList", request);

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<List<MediaLinkResult>>();
        Assert.IsNotNull(results);
        Assert.IsTrue(results!.Count > 0, "Results should not be empty");
    }

    [TestMethod]
    public async Task ByUrlList_WithValidTidalUrl_ReturnsOkWithResults()
    {
        // Arrange
        var request = new MusicLookupController.UrlReq(
            "https://tidal.com/track/96572657"
        );

        // Act
        var response = await _client!.PostAsJsonAsync("/music/lookup/urlList", request);

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<List<MediaLinkResult>>();
        Assert.IsNotNull(results);
        Assert.IsTrue(results!.Count > 0, "Results should not be empty");
    }

    [TestMethod]
    public async Task ByUrlList_WithMultipleUrls_ReturnsMultipleResults()
    {
        // Arrange
        var request = new MusicLookupController.UrlReq(
            "https://open.spotify.com/album/6X9k3hgEYTUx6tD5FVx7hq " +
            "https://music.apple.com/us/album/a-night-at-the-opera-deluxe-remastered-version/1440806041"
        );

        // Act
        var response = await _client!.PostAsJsonAsync("/music/lookup/urlList", request);

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<List<MediaLinkResult>>();
        Assert.IsNotNull(results);
        // Should have at least one result (deduplication may occur if URLs point to same content)
        Assert.IsTrue(results!.Count > 0, "Results should not be empty");
    }

    [TestMethod]
    public async Task ByIsrc_WithValidIsrc_ReturnsOkWithResult()
    {

        // Arrange
        var request = new MusicLookupController.IsrcReq("GBUM71029604");

        // Act
        var response = await _client!.PostAsJsonAsync("/music/lookup/isrc", request);

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<MediaLinkResult>();
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Results.Count > 0, "result.Results should not be empty");
    }

    [TestMethod]
    public async Task ByUpc_WithValidUpc_ReturnsOkWithResult()
    {

        // Arrange
        var request = new MusicLookupController.UpcReq("00602547202307");

        // Act
        var response = await _client!.PostAsJsonAsync("/music/lookup/upc", request);

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<MediaLinkResult>();
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Results.Count > 0, "result.Results should not be empty");
    }

    [TestMethod]
    public async Task ByTitle_WithValidTitleAndArtist_ReturnsOkWithResult()
    {

        // Arrange
        var request = new MusicLookupController.TitleReq("Bohemian Rhapsody", "Queen");

        // Act
        var response = await _client!.PostAsJsonAsync("/music/lookup/title", request);

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<MediaLinkResult>();
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Results.Count > 0, "result.Results should not be empty");
    }

    [TestMethod]
    public async Task ByUrl_StreamingEndpoint_ReturnsResults()
    {

        // Arrange
        var request = new MusicLookupController.UrlReq(
            "https://open.spotify.com/track/4u7EnebtmKWzUH433cf5Qv"
        );

        // Act
        var response = await _client!.PostAsJsonAsync("/music/lookup/url", request);

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Length > 0, "content should not be empty");
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}

/// <summary>
/// Custom web application factory for integration testing.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        
        // Set content root to the test output directory where appsettings.json is located
        var testAssemblyPath = Path.GetDirectoryName(typeof(CustomWebApplicationFactory).Assembly.Location);
        if (testAssemblyPath != null)
        {
            builder.UseContentRoot(testAssemblyPath);
        }
        
        //Configure test-specific services (override Discord to prevent it from starting)
        builder.ConfigureServices(services =>
        {
            // Remove Discord hosted service if it was registered
            var descriptors = services.Where(d => 
                d.ServiceType.FullName != null && 
                d.ServiceType.FullName.Contains("Discord")).ToList();
            foreach (var descriptor in descriptors)
            {
                services.Remove(descriptor);
            }
        });
    }
}
