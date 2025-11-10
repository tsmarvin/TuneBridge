using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TuneBridge.Tests.EndToEnd;

/// <summary>
/// End-to-end tests for the HomeController web interface endpoints.
/// </summary>
[TestClass]
public class HomeControllerTests {
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
    public async Task Index_ReturnsSuccessStatusCode( ) {
        // Act
        HttpResponseMessage response = await s_client!.GetAsync("/", TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        Assert.AreEqual( "text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString( ) );
    }

    [TestMethod]
    public async Task Index_ContainsExpectedContent( ) {
        // Act
        HttpResponseMessage response = await s_client!.GetAsync("/", TestContext.CancellationToken );
        string content = await response.Content.ReadAsStringAsync( TestContext.CancellationToken );

        // Assert
        Assert.IsTrue( content.Contains( "TuneBridge", StringComparison.OrdinalIgnoreCase ) );
    }

    [TestMethod]
    public async Task Privacy_ReturnsSuccessStatusCode( ) {
        // Act
        HttpResponseMessage response = await s_client!.GetAsync("/Home/Privacy", TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
    }

    [TestMethod]
    public async Task Error_ReturnsSuccessStatusCode( ) {
        // Act
        HttpResponseMessage response = await s_client!.GetAsync("/Home/Error", TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
    }

    [TestMethod]
    public async Task NonExistentRoute_ReturnsNotFound( ) {
        // Act
        HttpResponseMessage response = await s_client!.GetAsync("/NonExistent/Route", TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.NotFound, response.StatusCode );
    }

    public TestContext TestContext { get; set; }
}
