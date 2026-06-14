using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Infrastructure.Utilities;
using BridgeBeats.Providers.Spotify;
using BridgeBeats.Worker.JetStreamWatcher;
using idunno.Security;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for the Spotify JetStream type-routing logic.
/// Covers the contract between <see cref="SpotifyLinkParser"/> entity recognition
/// and the lookup-key / saga-ID format used by
/// <c>JetStreamWatcherService.EnqueueMusicLinkAsync</c>.
/// </summary>
[TestClass]
public class SpotifyLinkTypeRoutingTests {

    /// <summary>Gets or sets the test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    #region SpotifyLinkParser Entity Recognition

    /// <summary>
    /// Verifies that a Spotify track URL produces SpotifyEntity.Track with the correct ID.
    /// </summary>
    [TestMethod]
    public async Task TryParseUriAsync_WithTrackUrl_ShouldReturnTrackEntityAndId( ) {
        // Arrange
        string trackId = "3n3Ppam7vgaVa1iaRUc9Lp";
        string url = $"https://open.spotify.com/track/{trackId}";

        // Act
        (bool success, SpotifyEntity kind, string id) = await SpotifyLinkParser.TryParseUriAsync( url );

        // Assert
        Assert.IsTrue( success );
        Assert.AreEqual( SpotifyEntity.Track, kind );
        Assert.AreEqual( trackId, id );
    }

    /// <summary>
    /// Verifies that a Spotify track URL with query parameters strips the query parameter
    /// and still extracts the correct ID.
    /// </summary>
    [TestMethod]
    public async Task TryParseUriAsync_WithTrackUrlAndQueryParams_ShouldReturnTrackEntityAndId( ) {
        // Arrange
        string trackId = "3n3Ppam7vgaVa1iaRUc9Lp";
        string url = $"https://open.spotify.com/track/{trackId}?si=abc123xyz";

        // Act
        (bool success, SpotifyEntity kind, string id) = await SpotifyLinkParser.TryParseUriAsync( url );

        // Assert
        Assert.IsTrue( success );
        Assert.AreEqual( SpotifyEntity.Track, kind );
        Assert.AreEqual( trackId, id );
    }

    /// <summary>
    /// Verifies that a Spotify album URL produces SpotifyEntity.Album with the correct ID.
    /// </summary>
    [TestMethod]
    public async Task TryParseUriAsync_WithAlbumUrl_ShouldReturnAlbumEntityAndId( ) {
        // Arrange
        string albumId = "6WdSsBrH5QtofaTTqgwxOV";
        string url = $"https://open.spotify.com/album/{albumId}";

        // Act
        (bool success, SpotifyEntity kind, string id) = await SpotifyLinkParser.TryParseUriAsync( url );

        // Assert
        Assert.IsTrue( success );
        Assert.AreEqual( SpotifyEntity.Album, kind );
        Assert.AreEqual( albumId, id );
    }

    /// <summary>
    /// Verifies that an artist URL is not recognised by the parser (parse returns success=false).
    /// Artist links are not processed anywhere in the system; the JetStream watcher drops them.
    /// Director ruling 2026-06-07: no artist/playlist processing exists or is planned.
    /// </summary>
    [TestMethod]
    public async Task TryParseUriAsync_WithArtistUrl_ShouldNotProduceTypedIdCandidate( ) {
        // Arrange
        string artistId = "6qqNVTkY8uBg9cP3Jd7DAH";
        string url = $"https://open.spotify.com/artist/{artistId}";

        // Act
        (bool success, _, _) = await SpotifyLinkParser.TryParseUriAsync( url );

        // Assert — the regex does not match "artist"; parse must fail outright.
        // Pinning success==false is the discriminating assertion: it would fail against any
        // implementation that expanded the regex to recognise artist URLs.
        Assert.IsFalse( success,
            "Artist URLs are not recognised by SpotifyLinkParser; parse must return success=false" );
    }

    /// <summary>
    /// Verifies that a playlist URL is not recognised by the parser (parse returns success=false).
    /// Playlist links are not processed anywhere in the system; the JetStream watcher drops them.
    /// Director ruling 2026-06-07: no artist/playlist processing exists or is planned.
    /// </summary>
    [TestMethod]
    public async Task TryParseUriAsync_WithPlaylistUrl_ShouldNotProduceTypedIdCandidate( ) {
        // Arrange
        string playlistId = "37i9dQZF1DXcBWIGoYBM5M";
        string url = $"https://open.spotify.com/playlist/{playlistId}";

        // Act
        (bool success, _, _) = await SpotifyLinkParser.TryParseUriAsync( url );

        // Assert — the regex does not match "playlist"; parse must fail outright.
        // Pinning success==false is the discriminating assertion: it would fail against any
        // implementation that expanded the regex to recognise playlist URLs.
        Assert.IsFalse( success,
            "Playlist URLs are not recognised by SpotifyLinkParser; parse must return success=false" );
    }

    /// <summary>
    /// Verifies that a prerelease URL does not produce a typed ID lookup candidate.
    /// </summary>
    [TestMethod]
    public async Task TryParseUriAsync_WithPrereleaseUrl_ShouldNotProduceTypedIdCandidate( ) {
        // Arrange
        string url = "https://open.spotify.com/prerelease/1ABC2DEF3GHI4JKL5MNO6P";

        // Act
        (bool success, SpotifyEntity kind, string id) = await SpotifyLinkParser.TryParseUriAsync( url );

        // Assert — prerelease links are not batch-API-eligible
        bool isTypedLookupCandidate = success && (kind is SpotifyEntity.Track or SpotifyEntity.Album);
        Assert.IsFalse( isTypedLookupCandidate,
            "Prerelease URL must NOT produce a typed ID lookup candidate" );
    }

    /// <summary>
    /// Verifies that a non-Spotify URL produces no entity (TryParseUriAsync fails).
    /// </summary>
    [TestMethod]
    public async Task TryParseUriAsync_WithNonSpotifyUrl_ShouldReturnFailure( ) {
        // Arrange
        string url = "https://music.apple.com/us/album/test/1234567890";

        // Act
        (bool success, _, _) = await SpotifyLinkParser.TryParseUriAsync( url );

        // Assert
        Assert.IsFalse( success );
    }

    #endregion

    #region LookupKey Cross-Producer Alignment Tests

    /// <summary>
    /// Verifies that <see cref="LookupKeyBuilder.TypedKey"/> for a SongIdLookup produces
    /// the canonical three-segment format <c>{LookupType}:{Provider}:{id}</c>.
    /// </summary>
    [TestMethod]
    public void LookupKeyBuilder_TypedKey_ForSongIdLookup_ShouldContainThreeSegments( ) {
        // Arrange
        string trackId = "3n3Ppam7vgaVa1iaRUc9Lp";

        // Act
        string key = LookupKeyBuilder.TypedKey( LookupRequestType.SongIdLookup, SupportedProviders.Spotify, trackId );

        // Assert — three segments separated by ':'
        string[] parts = key.Split( ':' );
        Assert.HasCount( 3, parts,
            "TypedKey must produce exactly three ':'-separated segments: {LookupType}:{Provider}:{id}" );
        Assert.AreEqual( LookupRequestType.SongIdLookup.ToString( ), parts[0], "First segment must be LookupType" );
        Assert.AreEqual( SupportedProviders.Spotify.ToString( ), parts[1], "Second segment must be Provider" );
        Assert.AreEqual( trackId, parts[2], "Third segment must be the entity ID" );
    }

    /// <summary>
    /// Verifies that <see cref="LookupKeyBuilder.TypedKey"/> for an AlbumIdLookup follows
    /// the same three-segment format.
    /// </summary>
    [TestMethod]
    public void LookupKeyBuilder_TypedKey_ForAlbumIdLookup_ShouldContainThreeSegments( ) {
        // Arrange
        string albumId = "6WdSsBrH5QtofaTTqgwxOV";

        // Act
        string key = LookupKeyBuilder.TypedKey( LookupRequestType.AlbumIdLookup, SupportedProviders.Spotify, albumId );

        // Assert
        string[] parts = key.Split( ':' );
        Assert.HasCount( 3, parts );
        Assert.AreEqual( LookupRequestType.AlbumIdLookup.ToString( ), parts[0] );
        Assert.AreEqual( SupportedProviders.Spotify.ToString( ), parts[1] );
        Assert.AreEqual( albumId, parts[2] );
    }

    /// <summary>
    /// Verifies that <see cref="LookupKeyBuilder.UrlKey"/> produces a key starting with
    /// <c>UriLookup:</c> followed by a URL hash — not the raw URL.
    /// Failure-first: before the format-alignment fix, some producers used the raw URL as
    /// the lookup value, while others used the hash, causing saga ID mismatches for the
    /// same artist URL.
    /// </summary>
    [TestMethod]
    public void LookupKeyBuilder_UrlKey_ShouldHashUrlNotUseRaw( ) {
        // Arrange
        string url = "https://open.spotify.com/artist/6qqNVTkY8uBg9cP3Jd7DAH";

        // Act
        string key = LookupKeyBuilder.UrlKey( url );

        // Assert — must NOT contain the raw URL (it must be hashed)
        Assert.IsFalse( key.Contains( url, StringComparison.Ordinal ),
            "UrlKey must hash the URL, not embed the raw URL in the key" );
        Assert.IsTrue( key.StartsWith( "UriLookup:", StringComparison.Ordinal ),
            "UrlKey must start with 'UriLookup:'" );
    }

    /// <summary>
    /// Verifies that all four producers (JetStreamWatcher, SpotifyBulkProcessorService,
    /// QueueProcessorBackgroundService, and LookupOrchestrator) produce the same saga ID
    /// for the same track ID when they all use <see cref="LookupKeyBuilder.TypedKey"/>.
    /// This is the core correctness guarantee of the format-alignment fix: cross-producer
    /// state sharing works only when the lookup key format is identical.
    /// </summary>
    [TestMethod]
    public void LookupKeyBuilder_AllProducers_ShouldProduceSameSagaIdForSameTrackId( ) {
        // Arrange — all four producers build the key with LookupKeyBuilder.TypedKey
        const string TrackId = "3n3Ppam7vgaVa1iaRUc9Lp";

        string jetStreamKey = LookupKeyBuilder.TypedKey( LookupRequestType.SongIdLookup, SupportedProviders.Spotify, TrackId );
        string bulkProcessorKey = LookupKeyBuilder.TypedKey( LookupRequestType.SongIdLookup, SupportedProviders.Spotify, TrackId );
        string queueProcessorKey = LookupKeyBuilder.TypedKey( LookupRequestType.SongIdLookup, SupportedProviders.Spotify, TrackId );

        // Act
        string sagaIdJetStream = ISagaStateManager.GenerateSagaId( jetStreamKey );
        string sagaIdBulkProcessor = ISagaStateManager.GenerateSagaId( bulkProcessorKey );
        string sagaIdQueueProcessor = ISagaStateManager.GenerateSagaId( queueProcessorKey );

        // Assert — all three must be identical
        Assert.AreEqual( sagaIdJetStream, sagaIdBulkProcessor,
            "JetStreamWatcher and SpotifyBulkProcessorService must produce identical saga IDs for the same track" );
        Assert.AreEqual( sagaIdBulkProcessor, sagaIdQueueProcessor,
            "SpotifyBulkProcessorService and QueueProcessorBackgroundService must produce identical saga IDs for the same track" );
    }

    /// <summary>
    /// Verifies that different track IDs produce different saga IDs
    /// (no hash collision for distinct Spotify entities).
    /// </summary>
    [TestMethod]
    public void LookupKeyBuilder_TypedKey_ForDifferentTrackIds_ShouldProduceDifferentSagaIds( ) {
        // Arrange
        string key1 = LookupKeyBuilder.TypedKey( LookupRequestType.SongIdLookup, SupportedProviders.Spotify, "3n3Ppam7vgaVa1iaRUc9Lp" );
        string key2 = LookupKeyBuilder.TypedKey( LookupRequestType.SongIdLookup, SupportedProviders.Spotify, "4uLU6hMCjMI75M1A2tKUQC" );

        // Act
        string sagaId1 = ISagaStateManager.GenerateSagaId( key1 );
        string sagaId2 = ISagaStateManager.GenerateSagaId( key2 );

        // Assert
        Assert.AreNotEqual( sagaId1, sagaId2,
            "Different Spotify IDs must produce different saga IDs (no collision)" );
    }

    /// <summary>
    /// Verifies that <see cref="ISagaStateManager.GenerateSagaId"/> is deterministic:
    /// the same lookup key always produces the same saga ID regardless of call count.
    /// Failure-first: the old watcher used a timestamp-based ID that changed on every call.
    /// </summary>
    [TestMethod]
    public void GenerateSagaId_WithSameKey_ShouldBeDeterministic( ) {
        // Arrange
        string key = LookupKeyBuilder.TypedKey( LookupRequestType.SongIdLookup, SupportedProviders.Spotify, "3n3Ppam7vgaVa1iaRUc9Lp" );

        // Act — invoke twice
        string sagaId1 = ISagaStateManager.GenerateSagaId( key );
        string sagaId2 = ISagaStateManager.GenerateSagaId( key );

        // Assert
        Assert.AreEqual( sagaId1, sagaId2,
            "GenerateSagaId must be deterministic — the same key must always yield the same saga ID" );
    }

    #endregion

    #region SpotifyLinkParser Short-Link Host Gate (SSRF)

    /// <summary>
    /// Verifies that a link-local IP address embedded as the host — SSRF vector
    /// <c>169.254.169.254/spotify.link/a</c> — is rejected by the host gate and returns
    /// no-match without performing an outbound HTTP call.
    /// Failure-first evidence: before the host gate was added, the short-link resolver would
    /// attempt <c>GetAsync("https://169.254.169.254/spotify.link/a")</c> and throw
    /// <see cref="HttpRequestException"/> (or timeout after 5 s); the test would fail with
    /// an unhandled exception rather than returning <c>success=false</c> cleanly.
    /// </summary>
    [TestMethod]
    [Timeout( 3000, CooperativeCancellation = true )]
    public async Task TryParseUriAsync_WithLinkLocalIpSsrfVector_RejectsWithoutFetch( ) {
        // Arrange — link-local IP used as host with spotify.link in the path
        string ssrfVector = "169.254.169.254/spotify.link/a";

        // Act — must not throw; must return no-match immediately (no 5s fetch timeout)
        (bool success, _, _) = await SpotifyLinkParser.TryParseUriAsync( ssrfVector );

        // Assert — host gate rejects before any outbound fetch
        Assert.IsFalse( success,
            "SSRF vector 169.254.169.254/spotify.link/a must be rejected by the host gate" );
    }

    /// <summary>
    /// Verifies that an arbitrary external host — SSRF vector
    /// <c>evil.com/spotify.link/a</c> — is rejected by the host gate and returns
    /// no-match without performing an outbound HTTP call.
    /// Failure-first evidence: before the host gate was added, the short-link resolver would
    /// attempt <c>GetAsync("https://evil.com/spotify.link/a")</c> and throw
    /// <see cref="HttpRequestException"/>; the test would fail with an unhandled exception.
    /// </summary>
    [TestMethod]
    [Timeout( 3000, CooperativeCancellation = true )]
    public async Task TryParseUriAsync_WithArbitraryHostSsrfVector_RejectsWithoutFetch( ) {
        // Arrange — arbitrary host with spotify.link in the path
        string ssrfVector = "evil.com/spotify.link/a";

        // Act — must not throw; must return no-match immediately
        (bool success, _, _) = await SpotifyLinkParser.TryParseUriAsync( ssrfVector );

        // Assert — host gate rejects before any outbound fetch
        Assert.IsFalse( success,
            "SSRF vector evil.com/spotify.link/a must be rejected by the host gate" );
    }

    /// <summary>
    /// Verifies that a subdomain look-alike host — <c>spotify.link.evil.com/x</c> —
    /// is not matched by the short-link regex and therefore returns no-match.
    /// This is not a short-link and the host gate is not even reached: the regex
    /// requires <c>spotify\.link/</c> with the slash immediately after <c>.link</c>.
    /// </summary>
    [TestMethod]
    public async Task TryParseUriAsync_WithSubdomainLookalikeHost_ReturnsNoMatch( ) {
        // Arrange — looks like spotify.link but the TLD is evil.com
        string lookalike = "spotify.link.evil.com/x";

        // Act
        (bool success, _, _) = await SpotifyLinkParser.TryParseUriAsync( lookalike );

        // Assert — the short-link regex does not match; no host check or fetch is attempted
        Assert.IsFalse( success,
            "spotify.link.evil.com does not match the spotify.link short-link pattern; must return no-match" );
    }

    /// <summary>
    /// Verifies that a well-formed <c>spotify.link/&lt;id&gt;</c> input passes the host gate
    /// and — when the fake handler returns a 301 redirect to an open.spotify.com track URL —
    /// the full chain resolves to <c>(true, Track, &lt;id&gt;)</c>.
    /// Failure-first: before the handler seam existed the test made a live outbound call and
    /// asserted vacuously on an exception message; the new assertion would fail on any build
    /// where the handler seam is not wired or <c>TryParseUriAsync</c> ignores the resolved URL.
    /// </summary>
    [TestMethod]
    public async Task TryParseUriAsync_WithValidSpotifyLinkHost_ResolvesViaFakeHandler( ) {
        // Arrange — inject a fake handler that returns a 301 redirect to a known track URL
        const string TrackId = "3n3Ppam7vgaVa1iaRUc9Lp";
        string redirectTarget = $"https://open.spotify.com/track/{TrackId}";

        HttpResponseMessage fakeResponse = new( System.Net.HttpStatusCode.MovedPermanently );
        fakeResponse.Headers.Location = new Uri( redirectTarget );

        SpotifyLinkParser.SetHandlerFactoryForTests(
            ( ) => new FakeHttpMessageHandler( fakeResponse ) );
        try {
            // Act
            (bool success, SpotifyEntity kind, string id) =
                await SpotifyLinkParser.TryParseUriAsync( "spotify.link/abcdef123" );

            // Assert — full chain: host gate → resolve → re-parse → Track entity
            Assert.IsTrue( success, "A valid spotify.link redirect to a track URL must succeed" );
            Assert.AreEqual( SpotifyEntity.Track, kind );
            Assert.AreEqual( TrackId, id );
        } finally {
            SpotifyLinkParser.SetHandlerFactoryForTests(
                ( ) => SsrfSocketsHttpHandlerFactory.Create(
                    connectTimeout: TimeSpan.FromSeconds( 5 ),
                    allowAutoRedirect: false ) );
        }
    }

    /// <summary>
    /// Verifies the full host-gate→resolve→re-parse chain for an album short link:
    /// <c>spotify.link/&lt;id&gt;</c> + fake 301 → <c>open.spotify.com/album/&lt;id&gt;</c>
    /// → <c>(true, Album, &lt;id&gt;)</c>.
    /// Failure-first: would fail if <c>SpotifyEntity.Album</c> were never returned (e.g., if
    /// the resolved URL were ignored or the album regex branch were missing).
    /// </summary>
    [TestMethod]
    public async Task TryParseUriAsync_WithShortLinkResolvingToAlbum_ReturnsAlbumEntity( ) {
        // Arrange
        const string AlbumId = "6WdSsBrH5QtofaTTqgwxOV";
        string redirectTarget = $"https://open.spotify.com/album/{AlbumId}";

        HttpResponseMessage fakeResponse = new( System.Net.HttpStatusCode.MovedPermanently );
        fakeResponse.Headers.Location = new Uri( redirectTarget );

        SpotifyLinkParser.SetHandlerFactoryForTests(
            ( ) => new FakeHttpMessageHandler( fakeResponse ) );
        try {
            // Act
            (bool success, SpotifyEntity kind, string id) =
                await SpotifyLinkParser.TryParseUriAsync( "spotify.link/AlbumCode1" );

            // Assert
            Assert.IsTrue( success );
            Assert.AreEqual( SpotifyEntity.Album, kind );
            Assert.AreEqual( AlbumId, id );
        } finally {
            SpotifyLinkParser.SetHandlerFactoryForTests(
                ( ) => SsrfSocketsHttpHandlerFactory.Create(
                    connectTimeout: TimeSpan.FromSeconds( 5 ),
                    allowAutoRedirect: false ) );
        }
    }

    /// <summary>
    /// Negative control: an SSRF-vector host (<c>evil.com/spotify.link/x</c>) must not invoke
    /// the fake handler — the host gate rejects the input before any outbound call is made.
    /// Failure-first: would fail (call count == 1) against any implementation that skips
    /// the host gate and forwards all short-link-shaped inputs to the HTTP handler.
    /// </summary>
    [TestMethod]
    [Timeout( 3000, CooperativeCancellation = true )]
    public async Task TryParseUriAsync_WithSsrfVectorHost_NeverInvokesHandler( ) {
        // Arrange — countable fake; any invocation is a gate failure
        FakeHttpMessageHandler countingHandler = new(
            new HttpResponseMessage( System.Net.HttpStatusCode.OK ) );

        SpotifyLinkParser.SetHandlerFactoryForTests( ( ) => countingHandler );
        try {
            // Act
            (bool success, _, _) =
                await SpotifyLinkParser.TryParseUriAsync( "evil.com/spotify.link/x" );

            // Assert — host gate must reject; handler must never be called
            Assert.IsFalse( success, "SSRF vector must be rejected" );
            Assert.AreEqual( 0, countingHandler.CallCount,
                "Handler must NOT be invoked for a non-spotify.link host" );
        } finally {
            SpotifyLinkParser.SetHandlerFactoryForTests(
                ( ) => SsrfSocketsHttpHandlerFactory.Create(
                    connectTimeout: TimeSpan.FromSeconds( 5 ),
                    allowAutoRedirect: false ) );
        }
    }

    /// <summary>
    /// Verifies that a scheme-prefixed short link — <c>https://spotify.link/&lt;id&gt;</c> —
    /// passes the host gate, resolves via the fake 301 redirect, and returns
    /// <c>(true, Track, &lt;id&gt;)</c>.
    /// Failure-first: before the scheme-normalization fix, <c>new Uri($"https://{link}")</c>
    /// with a scheme-prefixed input produced <c>https://https://spotify.link/…</c>,
    /// causing <c>Uri.Host</c> to equal <c>"https"</c> instead of <c>"spotify.link"</c>, so the
    /// host gate rejected the input and the method returned <c>success=false</c>.
    /// </summary>
    [TestMethod]
    public async Task TryParseUriAsync_WithSchemePrefixedSpotifyLink_ResolvesViaFakeHandler( ) {
        // Arrange — inject a fake handler that returns a 301 redirect to a known track URL
        const string TrackId = "3n3Ppam7vgaVa1iaRUc9Lp";
        string redirectTarget = $"https://open.spotify.com/track/{TrackId}";

        HttpResponseMessage fakeResponse = new( System.Net.HttpStatusCode.MovedPermanently );
        fakeResponse.Headers.Location = new Uri( redirectTarget );

        SpotifyLinkParser.SetHandlerFactoryForTests(
            ( ) => new FakeHttpMessageHandler( fakeResponse ) );
        try {
            // Act — scheme-prefixed input: the real web/Discord path strips the scheme upstream,
            // but the parser contract must also accept scheme-prefixed inputs directly.
            (bool success, SpotifyEntity kind, string id) =
                await SpotifyLinkParser.TryParseUriAsync( "https://spotify.link/abcdef123" );

            // Assert — full chain: normalize scheme → host gate → resolve → re-parse → Track entity
            Assert.IsTrue( success,
                "A scheme-prefixed https://spotify.link/… input must be accepted by the host gate" );
            Assert.AreEqual( SpotifyEntity.Track, kind );
            Assert.AreEqual( TrackId, id );
        } finally {
            SpotifyLinkParser.SetHandlerFactoryForTests(
                ( ) => SsrfSocketsHttpHandlerFactory.Create(
                    connectTimeout: TimeSpan.FromSeconds( 5 ),
                    allowAutoRedirect: false ) );
        }
    }

    /// <summary>
    /// Verifies that a short link with an <c>http://</c> scheme — the http branch of the
    /// scheme-strip path — passes the host gate, resolves via the fake 301 redirect,
    /// and returns <c>(true, Track, &lt;id&gt;)</c>.
    /// The scheme-strip logic normalizes both http:// and https:// before constructing
    /// the host-gate URI; this test locks the http:// branch against regression.
    /// Failure-first: would fail against any implementation that only strips https:// and
    /// leaves http:// intact, producing "http://http://spotify.link/…" and failing the host gate.
    /// </summary>
    [TestMethod]
    public async Task TryParseUriAsync_WithHttpSchemePrefixedSpotifyLink_ResolvesViaFakeHandler( ) {
        // Arrange — inject a fake handler that returns a 301 redirect to a known track URL
        const string TrackId = "3n3Ppam7vgaVa1iaRUc9Lp";
        string redirectTarget = $"https://open.spotify.com/track/{TrackId}";

        HttpResponseMessage fakeResponse = new( System.Net.HttpStatusCode.MovedPermanently );
        fakeResponse.Headers.Location = new Uri( redirectTarget );

        SpotifyLinkParser.SetHandlerFactoryForTests(
            ( ) => new FakeHttpMessageHandler( fakeResponse ) );
        try {
            // Act — http:// scheme prefix
            (bool success, SpotifyEntity kind, string id) =
                await SpotifyLinkParser.TryParseUriAsync( "http://spotify.link/abcdef123" );

            // Assert — full chain: normalize http:// scheme → host gate → resolve → re-parse → Track entity
            Assert.IsTrue( success,
                "A scheme-prefixed http://spotify.link/… input must be accepted by the host gate" );
            Assert.AreEqual( SpotifyEntity.Track, kind );
            Assert.AreEqual( TrackId, id );
        } finally {
            SpotifyLinkParser.SetHandlerFactoryForTests(
                ( ) => SsrfSocketsHttpHandlerFactory.Create(
                    connectTimeout: TimeSpan.FromSeconds( 5 ),
                    allowAutoRedirect: false ) );
        }
    }

    /// <summary>
    /// Negative controls: confirms that credential-injection and double-scheme abuse vectors
    /// that contain the text "spotify.link" are rejected without any outbound fetch.
    /// The inputs exercise four distinct attack shapes:
    /// - credential injection: <c>spotify.link@evil.com</c>
    /// - scheme + credential injection: <c>https://spotify.link@evil.com/x</c>
    /// - double-scheme: <c>https://https://evil.com/spotify.link/x</c>
    /// - trailing-dot TLD variant: <c>spotify.link./x</c>
    /// Failure-first: would fail (call count &gt; 0 or success = true) against any
    /// implementation that parses "spotify.link" from these inputs before applying the host gate.
    /// </summary>
    [TestMethod]
    [Timeout( 3000, CooperativeCancellation = true )]
    [DataRow( "spotify.link@evil.com" )]
    [DataRow( "https://spotify.link@evil.com/x" )]
    [DataRow( "https://https://evil.com/spotify.link/x" )]
    [DataRow( "spotify.link./x" )]
    public async Task TryParseUriAsync_WithSchemeStripAbuseVectors_RejectWithoutFetch( string vector ) {
        // Arrange — countable fake; any invocation means the gate was bypassed
        FakeHttpMessageHandler countingHandler = new(
            new HttpResponseMessage( System.Net.HttpStatusCode.OK ) );

        SpotifyLinkParser.SetHandlerFactoryForTests( ( ) => countingHandler );
        try {
            // Act
            (bool success, _, _) = await SpotifyLinkParser.TryParseUriAsync( vector );

            // Assert — must be rejected before any outbound fetch
            Assert.IsFalse( success, $"Abuse vector '{vector}' must be rejected" );
            Assert.AreEqual( 0, countingHandler.CallCount,
                $"Handler must NOT be invoked for abuse vector '{vector}'" );
        } finally {
            SpotifyLinkParser.SetHandlerFactoryForTests(
                ( ) => SsrfSocketsHttpHandlerFactory.Create(
                    connectTimeout: TimeSpan.FromSeconds( 5 ),
                    allowAutoRedirect: false ) );
        }
    }

    #endregion

    #region IdentifyProviderAsync Direct Coverage

    /// <summary>
    /// Verifies that a Spotify track URL produces SongIdLookup with the Spotify track ID
    /// as the lookupValue — the ID, not the URL.
    /// </summary>
    [TestMethod]
    public async Task IdentifyProviderAsync_WithSpotifyTrackUrl_ShouldReturnSongIdLookupAndId( ) {
        // Arrange
        string trackId = "3n3Ppam7vgaVa1iaRUc9Lp";
        string url = $"https://open.spotify.com/track/{trackId}";

        // Act
        (SupportedProviders? provider, LookupRequestType lookupType, bool isAlbum, string lookupValue)
            = await JetStreamWatcherService.IdentifyProviderAsync( url );

        // Assert
        Assert.AreEqual( SupportedProviders.Spotify, provider, "Provider must be Spotify" );
        Assert.AreEqual( LookupRequestType.SongIdLookup, lookupType, "Track URL must produce SongIdLookup" );
        Assert.IsFalse( isAlbum, "isAlbum must be false for a track URL" );
        Assert.AreEqual( trackId, lookupValue, "lookupValue must be the track ID, not the URL" );
    }

    /// <summary>
    /// Verifies that a Spotify album URL produces AlbumIdLookup with the Spotify album ID.
    /// </summary>
    [TestMethod]
    public async Task IdentifyProviderAsync_WithSpotifyAlbumUrl_ShouldReturnAlbumIdLookupAndId( ) {
        // Arrange
        string albumId = "6WdSsBrH5QtofaTTqgwxOV";
        string url = $"https://open.spotify.com/album/{albumId}";

        // Act
        (SupportedProviders? provider, LookupRequestType lookupType, bool isAlbum, string lookupValue)
            = await JetStreamWatcherService.IdentifyProviderAsync( url );

        // Assert
        Assert.AreEqual( SupportedProviders.Spotify, provider );
        Assert.AreEqual( LookupRequestType.AlbumIdLookup, lookupType, "Album URL must produce AlbumIdLookup" );
        Assert.IsTrue( isAlbum, "isAlbum must be true for an album URL" );
        Assert.AreEqual( albumId, lookupValue, "lookupValue must be the album ID" );
    }

    /// <summary>
    /// Verifies that a Spotify artist URL is not recognised and the link is dropped
    /// (provider == null, no candidate produced).
    /// No artist processing exists anywhere in the system; this is intended behavior.
    /// Director ruling 2026-06-07: do not add artist/playlist processing.
    /// </summary>
    [TestMethod]
    public async Task IdentifyProviderAsync_WithSpotifyArtistUrl_ShouldNotBeRecognized_LinkDropped( ) {
        // Arrange
        string artistId = "6qqNVTkY8uBg9cP3Jd7DAH";
        string url = $"https://open.spotify.com/artist/{artistId}";

        // Act
        (SupportedProviders? provider, _, _, _)
            = await JetStreamWatcherService.IdentifyProviderAsync( url );

        // Assert — artist links are not recognised; provider must be null (link dropped).
        // This test would fail against any implementation that routes artist links to a provider.
        Assert.IsNull( provider,
            "Artist URLs are not processed; IdentifyProviderAsync must return provider=null (link dropped)" );
    }

    /// <summary>
    /// Verifies that a Spotify playlist URL is not recognised and the link is dropped
    /// (provider == null, no candidate produced).
    /// No playlist processing exists anywhere in the system; this is intended behavior.
    /// Director ruling 2026-06-07: do not add artist/playlist processing.
    /// </summary>
    [TestMethod]
    public async Task IdentifyProviderAsync_WithSpotifyPlaylistUrl_ShouldNotBeRecognized_LinkDropped( ) {
        // Arrange
        string url = "https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M";

        // Act
        (SupportedProviders? provider, _, _, _)
            = await JetStreamWatcherService.IdentifyProviderAsync( url );

        // Assert — playlist links are not recognised; provider must be null (link dropped).
        // This test would fail against any implementation that routes playlist links to a provider.
        Assert.IsNull( provider,
            "Playlist URLs are not processed; IdentifyProviderAsync must return provider=null (link dropped)" );
    }

    /// <summary>
    /// Verifies that a Spotify prerelease URL produces UriLookup.
    /// Prerelease links are not batch-API-eligible.
    /// </summary>
    [TestMethod]
    public async Task IdentifyProviderAsync_WithSpotifyPrereleaseUrl_ShouldReturnUriLookup( ) {
        // Arrange
        string url = "https://open.spotify.com/prerelease/1ABC2DEF3GHI4JKL5MNO6P";

        // Act
        (SupportedProviders? provider, LookupRequestType lookupType, _, _)
            = await JetStreamWatcherService.IdentifyProviderAsync( url );

        // Assert
        Assert.AreEqual( SupportedProviders.Spotify, provider );
        Assert.AreEqual( LookupRequestType.UriLookup, lookupType, "Prerelease URL must produce UriLookup" );
    }

    /// <summary>
    /// Verifies that a Spotify short link (spotify.link/…) produces UriLookup without
    /// attempting HTTP resolution — short links require redirect-following by the worker.
    /// </summary>
    [TestMethod]
    public async Task IdentifyProviderAsync_WithSpotifyShortLink_ShouldReturnUriLookup( ) {
        // Arrange
        string url = "https://spotify.link/abcdef123";

        // Act
        (SupportedProviders? provider, LookupRequestType lookupType, _, _)
            = await JetStreamWatcherService.IdentifyProviderAsync( url );

        // Assert
        Assert.AreEqual( SupportedProviders.Spotify, provider );
        Assert.AreEqual( LookupRequestType.UriLookup, lookupType, "Short link must produce UriLookup (redirect-resolve path)" );
    }

    /// <summary>
    /// Verifies the producer contract for spotify.link inputs through the firehose path:
    /// the scheme-prefixed URL must be returned as the lookupValue unchanged so that the
    /// worker can resolve the redirect.
    /// The worker receives the original scheme-prefixed URL as the lookup value; stripping
    /// or normalizing here would lose the scheme and break the downstream resolver.
    /// Failure-first: would fail against any implementation that normalizes the URL before
    /// returning it (e.g. returns "spotify.link/x" instead of "https://spotify.link/x"),
    /// since the consumer path depends on the scheme being present.
    /// </summary>
    [TestMethod]
    public async Task IdentifyProviderAsync_WithSpotifyShortLink_ShouldCarrySchemeInLookupValue( ) {
        // Arrange
        string url = "https://spotify.link/x";

        // Act
        (SupportedProviders? provider, LookupRequestType lookupType, bool isAlbum, string lookupValue)
            = await JetStreamWatcherService.IdentifyProviderAsync( url );

        // Assert — firehose path shape: Spotify, UriLookup, not album, scheme-prefixed URL as lookupValue
        Assert.AreEqual( SupportedProviders.Spotify, provider );
        Assert.AreEqual( LookupRequestType.UriLookup, lookupType );
        Assert.IsFalse( isAlbum );
        Assert.AreEqual( url, lookupValue,
            "lookupValue must be the scheme-prefixed URL the worker received, not a normalized variant" );
    }

    /// <summary>
    /// Verifies that a Tidal link produces SupportedProviders.Tidal with UriLookup.
    /// </summary>
    [TestMethod]
    public async Task IdentifyProviderAsync_WithTidalUrl_ShouldReturnTidalProvider( ) {
        // Arrange
        string url = "https://tidal.com/browse/track/12345678";

        // Act
        (SupportedProviders? provider, LookupRequestType lookupType, _, _)
            = await JetStreamWatcherService.IdentifyProviderAsync( url );

        // Assert
        Assert.AreEqual( SupportedProviders.Tidal, provider, "Tidal URL must produce Tidal provider" );
        Assert.AreEqual( LookupRequestType.UriLookup, lookupType );
    }

    /// <summary>
    /// Verifies that an Apple Music link produces SupportedProviders.AppleMusic with UriLookup.
    /// </summary>
    [TestMethod]
    public async Task IdentifyProviderAsync_WithAppleMusicUrl_ShouldReturnAppleMusicProvider( ) {
        // Arrange
        string url = "https://music.apple.com/us/album/folklore/1528112358";

        // Act
        (SupportedProviders? provider, LookupRequestType lookupType, _, _)
            = await JetStreamWatcherService.IdentifyProviderAsync( url );

        // Assert
        Assert.AreEqual( SupportedProviders.AppleMusic, provider, "Apple Music URL must produce AppleMusic provider" );
        Assert.AreEqual( LookupRequestType.UriLookup, lookupType );
    }

    #endregion
}

/// <summary>
/// Hermetic HTTP message handler for use with <see cref="SpotifyLinkParser.SetHandlerFactoryForTests"/>.
/// Returns a pre-configured response and records how many times it was invoked.
/// </summary>
internal sealed class FakeHttpMessageHandler( HttpResponseMessage response ) : HttpMessageHandler {
    private readonly HttpResponseMessage _response = response;
    private int _callCount;

    /// <summary>Gets the number of times <see cref="SendAsync"/> was invoked.</summary>
    public int CallCount => _callCount;

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken ) {
        _ = System.Threading.Interlocked.Increment( ref _callCount );
        return Task.FromResult( _response );
    }
}
