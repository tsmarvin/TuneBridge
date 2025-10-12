using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TuneBridge.Tests.EndToEnd;

/// <summary>
/// End-to-end tests for the HomeController web interface endpoints.
/// </summary>
[TestClass]
public class HomeControllerTests
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
    public async Task Index_ReturnsSuccessStatusCode()
    {
        // Act
        var response = await _client!.GetAsync("/");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString());
    }

    [TestMethod]
    public async Task Index_ContainsExpectedContent()
    {
        // Act
        var response = await _client!.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.IsTrue(content.Contains("TuneBridge", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task Privacy_ReturnsSuccessStatusCode()
    {
        // Act
        var response = await _client!.GetAsync("/Home/Privacy");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task Error_ReturnsSuccessStatusCode()
    {
        // Act
        var response = await _client!.GetAsync("/Home/Error");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task NonExistentRoute_ReturnsNotFound()
    {
        // Act
        var response = await _client!.GetAsync("/NonExistent/Route");

        // Assert
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
