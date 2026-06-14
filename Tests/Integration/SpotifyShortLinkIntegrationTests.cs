using BridgeBeats.Providers.Spotify;

#pragma warning disable CS1591

namespace BridgeBeats.Tests.Integration;

[TestClass]
[DoNotParallelize] // Prevent parallel execution; SpotifyLinkParser has a static test hook (SetHandlerFactoryForTests) used by unit tests.
[TestCategory( "Integration" )]
public class SpotifyShortLinkIntegrationTests {

    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Live reachability test: a real spotify.link short URL is resolved through the
    /// SSRF-hardened, single-hop resolver. Validates that (a) the SSRF wrapper does NOT
    /// block the legitimate public spotify.link host, and (b) the 3xx redirect is followed
    /// and a Location is returned.
    ///
    /// Durability: asserts ONLY that the resolver returns a non-null, absolute http(s) URI.
    /// It does NOT assert the resolved host (spotify.link redirects host-variably to either
    /// spotify.app.link or open.spotify.com) and does NOT assert a specific track/album, so
    /// it survives link expiry. If a link is later removed, the resolver returns null OR
    /// throws; either fails this test and the link should be swapped for a current one.
    ///
    /// Network policy (director): a network blip is a hard failure, restart CI. No
    /// Assert.Inconclusive / skip-on-network — a real SSRF regression must surface as red.
    /// </summary>
    [TestMethod]
    [DataRow( "spotify.link/dimbgwPvzXb" )]   // -> spotify.app.link (Branch.io)
    [DataRow( "spotify.link/6OGFZqOizXb" )]   // -> spotify.app.link (Branch.io)
    [Timeout( 15000, CooperativeCancellation = true )]
    public async Task ResolveSpotifyShortLinkAsync_WithRealShortLink_FollowsRedirectThroughSsrfClient(
        string shortLink ) {
        // Act - real outbound HTTPS through the SSRF-hardened single-hop resolver.
        string? resolvedUrl = await SpotifyLinkParser.ResolveSpotifyShortLinkAsync( shortLink );

        // Assert - POSITIVE control: the redirect was followed and a Location came back.
        Assert.IsNotNull( resolvedUrl,
            $"Expected '{shortLink}' to return a non-null redirect target. Null means the 3xx " +
            "Location was absent or the SSRF client blocked the call. If the link expired, " +
            "replace it with a current spotify.link URL." );

        Assert.IsTrue(
            Uri.TryCreate( resolvedUrl, UriKind.Absolute, out Uri? resolved )
                && (resolved.Scheme == Uri.UriSchemeHttp || resolved.Scheme == Uri.UriSchemeHttps),
            $"Expected '{shortLink}' to resolve to an absolute http(s) URI; got '{resolvedUrl}'. " +
            "The resolved host is intentionally NOT pinned - spotify.link redirects to either " +
            "spotify.app.link or open.spotify.com depending on the link." );
    }
}

#pragma warning restore CS1591
