using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TuneBridge.Tests.EndToEnd;

/// <summary>
/// End-to-end tests for the HomeController web interface endpoints.
/// </summary>
public class HomeControllerTests : IClassFixture<CustomWebApplicationFactory>, IDisposable
{
    private readonly HttpClient _client;

    public HomeControllerTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Index_ReturnsSuccessStatusCode()
    {
        // Act
        var response = await _client.GetAsync("/");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString());
    }

    [Fact]
    public async Task Index_ContainsExpectedContent()
    {
        // Act
        var response = await _client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Contains("TuneBridge", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Privacy_ReturnsSuccessStatusCode()
    {

        // Act
        var response = await _client.GetAsync("/Home/Privacy");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Error_ReturnsSuccessStatusCode()
    {
        // Act
        var response = await _client.GetAsync("/Home/Error");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NonExistentRoute_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/NonExistent/Route");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
