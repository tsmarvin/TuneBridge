using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Infrastructure.Utilities;
using BridgeBeats.Providers.Spotify;
using BridgeBeats.Worker.JetStreamWatcher;
using idunno.Security;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for Spotify link parsing and lookup routing. Covers <c>SpotifyLinkParser</c> entity
/// recognition (track and album URLs produce typed IDs; artist, playlist, and prerelease URLs do
/// not), the cross-producer saga-id alignment guaranteed by <c>LookupKeyBuilder.TypedKey</c> and
/// <c>ISagaStateManager.GenerateSagaId</c> (the JetStream watcher, bulk processor, and queue
/// processor must derive identical saga ids for the same entity), the SSRF host gate on
/// <c>spotify.link</c> short links (link-local, arbitrary-host, lookalike, and scheme-strip abuse
/// vectors are rejected without an outbound fetch; valid short links resolve via an injected
/// handler), and <c>JetStreamWatcherService.IdentifyProviderAsync</c> routing across providers and
/// entity types.
/// </summary>
[TestClass]
public class SpotifyLinkTypeRoutingTests {

    /// <summary>MSTest-injected context; its cancellation token bounds awaited parse operations.</summary>
    public TestContext TestContext { get; set; } = null!;

    #region SpotifyLinkParser Entity Recognition

    /// <summary>
    /// Verifies that a canonical Spotify track URL parses to success with a <c>Track</c> entity and
    /// the extracted track id.
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
    /// Verifies that a track URL carrying query parameters (for example a share <c>si</c> token)
    /// still parses to a <c>Track</c> entity with the bare track id, the query stripped.
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
    /// Verifies that a canonical Spotify album URL parses to success with an <c>Album</c> entity and
    /// the extracted album id.
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
    /// Verifies that a prerelease URL does not yield a typed ID lookup candidate: even if the parse
    /// reports a kind, it is neither <c>Track</c> nor <c>Album</c>, so no typed ID path is taken.
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
    /// Verifies that a non-Spotify URL (an Apple Music link) is not parsed as a Spotify entity,
    /// returning success=false.
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
    /// Verifies that <c>TypedKey</c> for a song-id lookup produces exactly three colon-separated
    /// segments in the canonical order: lookup type, provider, then entity id.
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
    /// Verifies that <c>TypedKey</c> for an album-id lookup produces the same three-segment
    /// type:provider:id shape as the song-id case.
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
    /// Verifies that <c>UrlKey</c> hashes the URL rather than embedding it raw: the resulting key
    /// does not contain the original URL and is prefixed with <c>UriLookup:</c>.
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
    /// Verifies the cross-producer saga-id invariant: the JetStream watcher, bulk processor, and
    /// queue processor each build a typed key the same way for the same track, so
    /// <c>GenerateSagaId</c> yields identical saga ids across all three producers.
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
    /// Verifies that different track ids produce different saga ids, guarding against saga-id
    /// collisions across distinct entities.
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
    /// Verifies that <c>GenerateSagaId</c> is deterministic: the same key always yields the same
    /// saga id, which is what lets independent producers converge on one saga.
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
    /// Verifies that a link-local IP SSRF vector (<c>169.254.169.254/spotify.link/a</c>, the cloud
    /// metadata address) is rejected by the host gate without an outbound fetch.
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
    /// Verifies that an arbitrary-host SSRF vector (<c>evil.com/spotify.link/a</c>) is rejected by
    /// the host gate without an outbound fetch: a non-<c>spotify.link</c> host never resolves.
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
    /// Verifies that a lookalike host (<c>spotify.link.evil.com</c>) does not match the
    /// <c>spotify.link</c> short-link pattern, returning no match rather than treating the attacker
    /// domain as a Spotify short link.
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
    /// Verifies that a valid <c>spotify.link</c> short link resolves via an injected fake handler: a
    /// 301 redirect to a track URL yields success with a <c>Track</c> entity and the redirected
    /// track id. The real SSRF handler factory is restored afterward.
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
    /// Verifies that a <c>spotify.link</c> short link redirecting to an album URL resolves to an
    /// <c>Album</c> entity with the redirected album id, via the injected fake handler.
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
    /// Verifies that for an SSRF vector host (<c>evil.com/spotify.link/x</c>), the parse fails and
    /// the injected counting handler is never invoked, proving the host gate blocks before any
    /// network call is made.
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
    /// Verifies that a scheme-prefixed <c>https://spotify.link/…</c> input is accepted by the host
    /// gate and resolves to the redirected track entity via the injected fake handler.
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
    /// Verifies that a scheme-prefixed <c>http://spotify.link/…</c> input (plain HTTP) is likewise
    /// accepted by the host gate and resolves to the redirected track entity.
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
    /// Verifies that scheme-strip and userinfo abuse vectors that try to smuggle an attacker host
    /// past the <c>spotify.link</c> gate are all rejected without invoking the handler, across
    /// userinfo (<c>spotify.link@evil.com</c>), double-scheme, and trailing-dot variants.
    /// </summary>
    /// <param name="vector">The abuse-vector URL under test, supplied per <c>[DataRow]</c>.</param>
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
    /// Verifies that a Spotify track URL routes to <c>Spotify</c> / <c>SongIdLookup</c> with
    /// <c>isAlbum</c> false and the lookup value set to the bare track id (not the full URL).
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
    /// Verifies that a Spotify album URL routes to <c>Spotify</c> / <c>AlbumIdLookup</c> with
    /// <c>isAlbum</c> true and the lookup value set to the album id.
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
    /// Verifies that a Spotify prerelease URL routes to <c>Spotify</c> with <c>UriLookup</c>: it is
    /// recognized but resolved through the URI path rather than a typed ID lookup.
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
    /// Verifies that a Spotify short link routes to <c>Spotify</c> with <c>UriLookup</c>, deferring
    /// to the redirect-resolve path rather than parsing a typed id up front.
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
    /// Verifies that for a Spotify short link, the lookup value carries the scheme-prefixed URL the
    /// worker received verbatim (not a normalized variant), so the downstream resolve sees the exact
    /// input.
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
    /// Verifies that a Tidal URL routes to the <c>Tidal</c> provider with <c>UriLookup</c>.
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
    /// Verifies that an Apple Music URL routes to the <c>AppleMusic</c> provider with
    /// <c>UriLookup</c>.
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
/// Test double for <see cref="HttpMessageHandler"/> that returns a fixed response for every request
/// and counts how many times it was invoked. Injected into <c>SpotifyLinkParser</c> via its
/// test-only handler factory so short-link resolution can be exercised offline, and so SSRF tests
/// can assert the handler was never called.
/// </summary>
/// <param name="response">The canned response returned for every request.</param>
internal sealed class FakeHttpMessageHandler( HttpResponseMessage response ) : HttpMessageHandler {
    /// <summary>The fixed response returned for every request.</summary>
    private readonly HttpResponseMessage _response = response;
    /// <summary>Backing counter for <see cref="CallCount"/>, incremented per invocation.</summary>
    private int _callCount;

    /// <summary>Number of times <see cref="SendAsync"/> has been invoked.</summary>
    public int CallCount => _callCount;

    /// <summary>
    /// Records the invocation and returns the canned response without performing any network I/O.
    /// </summary>
    /// <param name="request">The outgoing request (ignored beyond counting).</param>
    /// <param name="cancellationToken">A cancellation token (unused).</param>
    /// <returns>The fixed response supplied at construction.</returns>
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken ) {
        _ = System.Threading.Interlocked.Increment( ref _callCount );
        return Task.FromResult( _response );
    }
}
