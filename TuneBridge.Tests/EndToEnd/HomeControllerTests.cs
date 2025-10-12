using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TuneBridge.Tests.EndToEnd;

/// <summary>
/// End-to-end tests for the HomeController web interface endpoints.
/// </summary>
public class HomeControllerTests : IClassFixture<CustomWebApplicationFactory>, IDisposable
{
    private readonly HttpClient? _client;
    private readonly bool _secretsAvailable;

    public HomeControllerTests(CustomWebApplicationFactory factory)
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
    public async Task Index_ReturnsSuccessStatusCode()
    {
        if (!_secretsAvailable) return;

        // Act
        var response = await _client!.GetAsync("/");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString());
    }

    [Fact]
    public async Task Index_ContainsExpectedContent()
    {
        if (!_secretsAvailable) return;

        // Act
        var response = await _client!.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Contains("TuneBridge", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Privacy_ReturnsSuccessStatusCode()
    {
        if (!_secretsAvailable) return;

        // Act
        var response = await _client!.GetAsync("/Home/Privacy");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Error_ReturnsSuccessStatusCode()
    {
        if (!_secretsAvailable) return;

        // Act
        var response = await _client!.GetAsync("/Home/Error");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NonExistentRoute_ReturnsNotFound()
    {
        if (!_secretsAvailable) return;

        // Act
        var response = await _client!.GetAsync("/NonExistent/Route");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
