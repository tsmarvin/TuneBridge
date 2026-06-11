using BridgeBeats.Core.Domain.Providers.Common;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="ProviderMetricsHandler.NormalizeEndpoint"/> covering the
/// bulk-endpoint <c>ids=</c> query-parameter normalization added in CR-M-1.
/// </summary>
/// <remarks>
/// <para>
/// Without the <c>SpotifyBulkIdsRegex</c> normalization step, each unique comma-separated
/// ID list emitted a distinct <c>endpoint</c> tag value, producing unbounded metric
/// series growth in Prometheus/OTEL stores. After the fix, bulk URIs collapse to stable
/// templates: <c>tracks?ids={ids}</c> / <c>albums?ids={ids}</c>.
/// </para>
/// <para>
/// Failure-first discipline: tests were written before the <c>SpotifyBulkIdsRegex</c>
/// replace step was added to <c>NormalizeSpotifyEndpoint</c>. Against the pre-fix code,
/// the bulk-tracks and bulk-albums assertions fail because the raw ID list remains in the
/// tag value; the single-track assertion passes without the fix (the per-ID regex already
/// handles path-segment IDs).
/// </para>
/// </remarks>
[TestClass]
public class ProviderMetricsHandlerNormalizationTests {

    private ProviderMetricsHandler _spotifyHandler = null!;

    /// <summary>Gets or sets the test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Creates a Spotify-scoped handler before each test.</summary>
    [TestInitialize]
    public void Initialize( ) {
        _spotifyHandler = new ProviderMetricsHandler( "spotify" );
    }

    #region Bulk-endpoint ids= normalization (CR-M-1)

    /// <summary>
    /// Verifies that a bulk-tracks URI with multiple IDs normalizes to the stable
    /// template <c>tracks?ids={ids}</c> rather than embedding the raw ID list.
    /// Failure-first: before the SpotifyBulkIdsRegex step, this returned
    /// <c>tracks?ids=ID1,ID2,ID3</c> (unique per batch → unbounded cardinality).
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
    /// Verifies that a bulk-albums URI normalizes to <c>albums?ids={ids}</c>.
    /// Failure-first: same as bulk-tracks — raw ID list bypassed normalization before the fix.
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
    /// Verifies that a single-track URI normalizes to <c>tracks/{id}</c> (existing behavior).
    /// This test ensures the bulk-ids normalization does not break the single-track path.
    /// Failure-first: this test passes even against the pre-fix code because single-track
    /// URIs use path-segment IDs (handled by SpotifyIdRegex), not the ids= query param.
    /// The test is included to document the stable contract and confirm the bulk fix is additive.
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
    /// Verifies that a bulk-tracks URI with a single ID still normalizes to the stable
    /// template — the normalization must fire regardless of ID count (even count=1).
    /// This is distinct from the single-track path-segment URI above.
    /// Failure-first: before the fix a single-ID query param <c>tracks?ids=AAAA…</c>
    /// was matched by neither SpotifyBulkIdsRegex (absent) nor SpotifyIdRegex (query-param,
    /// not path-segment) and therefore leaked the raw ID into the tag.
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
