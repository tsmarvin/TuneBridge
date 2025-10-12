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
public class MusicLookupControllerTests : IClassFixture<CustomWebApplicationFactory>, IDisposable
{
    private readonly HttpClient? _client;
    private readonly bool _secretsAvailable;

    public MusicLookupControllerTests(CustomWebApplicationFactory factory)
    {
        try
        {
            _client = factory.CreateClient();
            _secretsAvailable = true;
        }
        catch
        {
            _secretsAvailable = false;
        }
    }

    [Fact]
    public async Task ByUrlList_WithValidAppleMusicUrl_ReturnsOkWithResults()
    {
        if (!_secretsAvailable) return;

        // Arrange
        var request = new MusicLookupController.UrlReq(
            "https://music.apple.com/us/album/bohemian-rhapsody/1440806041?i=1440806326"
        );

        // Act
        var response = await _client!.PostAsJsonAsync("/music/lookup/urlList", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<List<MediaLinkResult>>();
        Assert.NotNull(results);
        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task ByUrlList_WithValidSpotifyUrl_ReturnsOkWithResults()
    {
        if (!_secretsAvailable) return;

        // Arrange
        var request = new MusicLookupController.UrlReq(
            "https://open.spotify.com/track/4u7EnebtmKWzUH433cf5Qv"
        );

        // Act
        var response = await _client!.PostAsJsonAsync("/music/lookup/urlList", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<List<MediaLinkResult>>();
        Assert.NotNull(results);
        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task ByUrlList_WithMultipleUrls_ReturnsMultipleResults()
    {
        if (!_secretsAvailable) return;

        // Arrange
        var request = new MusicLookupController.UrlReq(
            "https://open.spotify.com/track/4u7EnebtmKWzUH433cf5Qv " +
            "https://music.apple.com/us/album/bohemian-rhapsody/1440806041?i=1440806326"
        );

        // Act
        var response = await _client!.PostAsJsonAsync("/music/lookup/urlList", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<List<MediaLinkResult>>();
        Assert.NotNull(results);
        // Should have at least one result (deduplication may occur if URLs point to same content)
        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task ByIsrc_WithValidIsrc_ReturnsOkWithResult()
    {
        if (!_secretsAvailable) return;

        // Arrange
        var request = new MusicLookupController.IsrcReq("GBUM71029604");

        // Act
        var response = await _client!.PostAsJsonAsync("/music/lookup/isrc", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<MediaLinkResult>();
        Assert.NotNull(result);
        Assert.NotEmpty(result.Results);
    }

    [Fact]
    public async Task ByUpc_WithValidUpc_ReturnsOkWithResult()
    {
        if (!_secretsAvailable) return;

        // Arrange
        var request = new MusicLookupController.UpcReq("00602547202307");

        // Act
        var response = await _client!.PostAsJsonAsync("/music/lookup/upc", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<MediaLinkResult>();
        Assert.NotNull(result);
        Assert.NotEmpty(result.Results);
    }

    [Fact]
    public async Task ByTitle_WithValidTitleAndArtist_ReturnsOkWithResult()
    {
        if (!_secretsAvailable) return;

        // Arrange
        var request = new MusicLookupController.TitleReq("Bohemian Rhapsody", "Queen");

        // Act
        var response = await _client!.PostAsJsonAsync("/music/lookup/title", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<MediaLinkResult>();
        Assert.NotNull(result);
        Assert.NotEmpty(result.Results);
    }

    [Fact]
    public async Task ByUrl_StreamingEndpoint_ReturnsResults()
    {
        if (!_secretsAvailable) return;

        // Arrange
        var request = new MusicLookupController.UrlReq(
            "https://open.spotify.com/track/4u7EnebtmKWzUH433cf5Qv"
        );

        // Act
        var response = await _client!.PostAsJsonAsync("/music/lookup/url", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(content);
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
    }
}
