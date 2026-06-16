using BridgeBeats.Web.Configuration;
using idunno.Security;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable CS1591

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Verifies that the named <c>"ATProtoOAuth"</c> HttpClient registered by
/// <see cref="StartupExtensions.AddATProtoOAuthHttpClient"/> is SSRF-hardened: requests to private and
/// link-local address ranges are blocked before any connection is made.
/// </summary>
/// <remarks>
/// The block is enforced by the third-party SSRF handler the client is wired with; these tests assert
/// the wiring is in effect by confirming that a request to a forbidden host fails with an
/// <see cref="HttpRequestException"/> whose inner exception is an <see cref="SsrfException"/>. The
/// covered ranges are the cloud-metadata link-local address (<c>169.254.169.254</c>), IPv4 loopback
/// (<c>127.0.0.1</c>), and an RFC 1918 private address (<c>10.0.0.1</c>) — the addresses an attacker
/// would target to pivot through the server.
/// </remarks>
[TestClass]
public class ATProtoOAuthSsrfTests {

    /// <summary>
    /// MSTest-injected context, used here to flow the test's cancellation token into the HTTP calls.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Verifies that a request to the link-local cloud-metadata address <c>169.254.169.254</c> through
    /// the <c>"ATProtoOAuth"</c> client is blocked, throwing an <see cref="HttpRequestException"/> whose
    /// inner exception is an <see cref="SsrfException"/>.
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
    /// Verifies that a request to the IPv4 loopback address <c>127.0.0.1</c> through the
    /// <c>"ATProtoOAuth"</c> client is blocked, throwing an <see cref="HttpRequestException"/> whose
    /// inner exception is an <see cref="SsrfException"/>. Loopback is a distinct SSRF vector from
    /// link-local, so this confirms the handler blocks the full loopback range, not just the
    /// cloud-metadata address.
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
    /// Verifies that a request to the RFC 1918 private address <c>10.0.0.1</c> through the
    /// <c>"ATProtoOAuth"</c> client is blocked, throwing an <see cref="HttpRequestException"/> whose
    /// inner exception is an <see cref="SsrfException"/>. RFC 1918 private ranges are an SSRF vector for
    /// reaching internal network services, so this confirms the handler covers the 10.0.0.0/8 range.
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
