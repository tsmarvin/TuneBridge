using BridgeBeats.Web.Configuration;
using idunno.Security;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable CS1591

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Verifies that the ATProtoOAuth named HTTP client refuses connections to private/link-local
/// IP addresses via the SSRF handler installed by <see cref="StartupExtensions.AddATProtoOAuthHttpClient"/>.
/// </summary>
[TestClass]
public class ATProtoOAuthSsrfTests {

    /// <summary>Gets or sets the test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Verifies that the ATProtoOAuth client rejects a request to a link-local address
    /// (169.254.169.254, the cloud-metadata endpoint) with an <see cref="HttpRequestException"/>
    /// whose inner exception is <see cref="SsrfException"/>.
    /// Failure-first: the test was run against the pre-implementation code (AddHttpClient without
    /// ConfigurePrimaryHttpMessageHandler) and the inner exception was a network-level error rather
    /// than <see cref="SsrfException"/> — the <c>Assert.IsInstanceOfType</c> check on
    /// <c>ex.InnerException</c> went red. After wiring <c>SsrfSocketsHttpHandlerFactory.Create</c>
    /// the inner exception becomes <see cref="SsrfException"/> and the assertion passes.
    /// </summary>
    [TestMethod]
    public async Task ATProtoOAuthClient_RequestToLinkLocalAddress_ThrowsSsrfException( ) {
        ServiceCollection services = new( );
        _ = services.AddLogging( );
        _ = services.AddATProtoOAuthHttpClient( );

        using ServiceProvider sp = services.BuildServiceProvider( );
        HttpClient client = sp.GetRequiredService<IHttpClientFactory>( ).CreateClient( "ATProtoOAuth" );

        HttpRequestException ex = await Assert.ThrowsExactlyAsync<HttpRequestException>(
            ( ) => client.GetAsync(
                "http://169.254.169.254/.well-known/oauth-authorization-server",
                TestContext.CancellationToken ) );
        _ = Assert.IsInstanceOfType<SsrfException>( ex.InnerException );
    }

    /// <summary>
    /// Verifies that the ATProtoOAuth client rejects a request to a loopback address
    /// (127.0.0.1) with an <see cref="HttpRequestException"/> whose inner exception is
    /// <see cref="SsrfException"/>.
    /// Loopback is a distinct SSRF vector from link-local; this test ensures the SSRF handler
    /// blocks the full loopback range, not just the cloud-metadata address.
    /// Failure-first: without the SSRF handler the connection attempt would produce a
    /// connection-refused network error rather than an SsrfException inner exception.
    /// </summary>
    [TestMethod]
    public async Task ATProtoOAuthClient_RequestToLoopbackAddress_ThrowsSsrfException( ) {
        ServiceCollection services = new( );
        _ = services.AddLogging( );
        _ = services.AddATProtoOAuthHttpClient( );

        using ServiceProvider sp = services.BuildServiceProvider( );
        HttpClient client = sp.GetRequiredService<IHttpClientFactory>( ).CreateClient( "ATProtoOAuth" );

        HttpRequestException ex = await Assert.ThrowsExactlyAsync<HttpRequestException>(
            ( ) => client.GetAsync(
                "http://127.0.0.1/.well-known/oauth-authorization-server",
                TestContext.CancellationToken ) );
        _ = Assert.IsInstanceOfType<SsrfException>( ex.InnerException );
    }

    /// <summary>
    /// Verifies that the ATProtoOAuth client rejects a request to an RFC-1918 private address
    /// (10.0.0.1) with an <see cref="HttpRequestException"/> whose inner exception is
    /// <see cref="SsrfException"/>.
    /// RFC-1918 private ranges are an SSRF vector for accessing internal network services;
    /// this test confirms the SSRF handler covers the 10.0.0.0/8 range.
    /// Failure-first: without the SSRF handler the connection attempt would either time out
    /// or produce a network error, not an SsrfException inner exception.
    /// </summary>
    [TestMethod]
    public async Task ATProtoOAuthClient_RequestToRfc1918Address_ThrowsSsrfException( ) {
        ServiceCollection services = new( );
        _ = services.AddLogging( );
        _ = services.AddATProtoOAuthHttpClient( );

        using ServiceProvider sp = services.BuildServiceProvider( );
        HttpClient client = sp.GetRequiredService<IHttpClientFactory>( ).CreateClient( "ATProtoOAuth" );

        HttpRequestException ex = await Assert.ThrowsExactlyAsync<HttpRequestException>(
            ( ) => client.GetAsync(
                "http://10.0.0.1/.well-known/oauth-authorization-server",
                TestContext.CancellationToken ) );
        _ = Assert.IsInstanceOfType<SsrfException>( ex.InnerException );
    }

}

#pragma warning restore CS1591
