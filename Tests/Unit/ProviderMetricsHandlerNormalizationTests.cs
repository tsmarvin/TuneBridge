using BridgeBeats.Core.Domain.Providers.Common;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="ProviderMetricsHandler"/>'s endpoint-normalization logic.
/// Verify that <c>NormalizeEndpoint</c> collapses high-cardinality provider URLs (bulk
/// <c>ids=</c> query lists and single-id path segments) into stable placeholder templates so
/// that emitted metric tags stay bounded in cardinality. The handler under test is the Spotify
/// instance created in <see cref="Initialize"/>.
/// </summary>
[TestClass]
public class ProviderMetricsHandlerNormalizationTests {
    /// <summary>The handler under test, constructed for the <c>"spotify"</c> provider.</summary>
    private ProviderMetricsHandler _spotifyHandler = null!;

    /// <summary>MSTest-injected context for the running test.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Creates a fresh Spotify-tagged <see cref="ProviderMetricsHandler"/> before each test.</summary>
    [TestInitialize]
    public void Initialize( ) {
        _spotifyHandler = new ProviderMetricsHandler( "spotify" );
    }

    #region Bulk-endpoint ids= normalization (CR-M-1)

    /// <summary>
    /// A bulk-tracks URL carrying a comma-separated <c>ids=</c> list normalizes to
    /// <c>"tracks?ids={ids}"</c>, so the id count does not inflate metric cardinality.
    /// </summary>
    [TestMethod]
    public void NormalizeEndpoint_BulkTracksUri_ShouldProduceStableIdsPlaceholder( ) {
        // Arrange — representative bulk tracks URI (3 comma-separated IDs)
        Uri uri = new( "https://api.spotify.com/v1/tracks?ids=3n3Ppam7vgaVa1iaRUc9Lp,4uLU6hMCjMI75M1A2tKUQC,6WdSsBrH5QtofaTTqgwxOV" );

        // Act
        string normalized = _spotifyHandler.NormalizeEndpoint( uri );

        // Assert — stable template; raw IDs must not be present
        Assert.AreEqual( "tracks?ids={ids}", normalized,
            "Bulk-tracks URI must normalize to 'tracks?ids={ids}' regardless of ID count" );
    }

    /// <summary>
    /// A bulk-albums URL carrying a comma-separated <c>ids=</c> list normalizes to
    /// <c>"albums?ids={ids}"</c>.
    /// </summary>
    [TestMethod]
    public void NormalizeEndpoint_BulkAlbumsUri_ShouldProduceStableIdsPlaceholder( ) {
        // Arrange — bulk albums URI (2 IDs)
        Uri uri = new( "https://api.spotify.com/v1/albums?ids=6WdSsBrH5QtofaTTqgwxOV,1A2GTWGtFfWp7KSQTwWOyo" );

        // Act
        string normalized = _spotifyHandler.NormalizeEndpoint( uri );

        // Assert
        Assert.AreEqual( "albums?ids={ids}", normalized,
            "Bulk-albums URI must normalize to 'albums?ids={ids}'" );
    }

    /// <summary>
    /// A single-track URL with the id in the path segment normalizes to <c>"tracks/{id}"</c>,
    /// distinct from the query-parameter <c>"tracks?ids={ids}"</c> bulk form.
    /// </summary>
    [TestMethod]
    public void NormalizeEndpoint_SingleTrackUri_ShouldProducePathSegmentPlaceholder( ) {
        // Arrange — single-track path-segment URI
        Uri uri = new( "https://api.spotify.com/v1/tracks/3n3Ppam7vgaVa1iaRUc9Lp" );

        // Act
        string normalized = _spotifyHandler.NormalizeEndpoint( uri );

        // Assert — single-track uses the existing path-segment normalization
        Assert.AreEqual( "tracks/{id}", normalized,
            "Single-track URI must normalize to 'tracks/{id}' (path-segment, not query-param)" );
    }

    /// <summary>
    /// A bulk-tracks URL with only one id in its <c>ids=</c> list still normalizes to the bulk
    /// template <c>"tracks?ids={ids}"</c> (the query-parameter shape, not the path-segment shape),
    /// so single- and multi-id bulk requests share one metric series.
    /// </summary>
    [TestMethod]
    public void NormalizeEndpoint_BulkTracksUriWithSingleId_ShouldStillNormalizeToTemplate( ) {
        // Arrange — bulk-tracks URI with exactly one ID (edge case: count=1)
        Uri uri = new( "https://api.spotify.com/v1/tracks?ids=3n3Ppam7vgaVa1iaRUc9Lp" );

        // Act
        string normalized = _spotifyHandler.NormalizeEndpoint( uri );

        // Assert
        Assert.AreEqual( "tracks?ids={ids}", normalized,
            "Single-ID bulk-tracks URI must still normalize to 'tracks?ids={ids}'" );
    }

    #endregion
}
