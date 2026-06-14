using System.Net;
using System.Net.Http;
using System.Text.Json;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Infrastructure.Storage;
using BridgeBeats.Core.Infrastructure.Storage.Car;
using BridgeBeats.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

#pragma warning disable CS1591

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Tests <see cref="ATProtoStorageService.ListAllRecordsAsync"/>, which downloads a user's entire PDS
/// repository as a CAR over the named sync HTTP client and streams the records in the lookup
/// collection as <c>(AT-URI, MediaLinkResult)</c> tuples.
/// </summary>
/// <remarks>
/// The HTTP layer is mocked to return CAR bytes built with the test helper <c>TestCarBuilder</c>. The
/// tests verify that enumeration filters to the <c>link.bridgebeats.lookup</c> collection and builds
/// each AT-URI as <c>at://{did}/{collection}/{rkey}</c>; handles an empty repository; propagates an
/// HTTP error as <see cref="HttpRequestException"/> and malformed CAR bytes as
/// <see cref="CarParseException"/>; skips an individual record that fails to deserialize while still
/// yielding the good ones; rejects an invalid DID with <see cref="ArgumentException"/> before making
/// any HTTP call; honors a cancelled token; and skips records whose rkey contains invalid characters
/// (such as a control character).
/// </remarks>
[TestClass]
public class ATProtoStorageServiceListRecordsTests {

    /// <summary>MSTest-injected context, used to flow the test's cancellation token into the service.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Mock session manager dependency of the service.</summary>
    private Mock<IATProtoSessionManager> _sessionManagerMock = null!;

    /// <summary>Mock logger injected into the service.</summary>
    private Mock<ILogger<ATProtoStorageService>> _loggerMock = null!;

    /// <summary>Mock HTTP message handler backing the sync client so CAR responses can be canned.</summary>
    private Mock<HttpMessageHandler> _httpHandlerMock = null!;

    /// <summary>Mock HTTP client factory that hands out the mocked sync client.</summary>
    private Mock<IHttpClientFactory> _httpClientFactoryMock = null!;

    /// <summary>HTTP client wrapping the mocked handler, supplied to the service.</summary>
    private HttpClient _httpClient = null!;

    /// <summary>Test DID whose repository is enumerated.</summary>
    private const string TestDid = "did:plc:testuser12345";

    /// <summary>Test PDS URI the CAR is downloaded from.</summary>
    private static readonly Uri s_testPdsUri = new( "https://pds.test.example" );

    /// <summary>
    /// Constructs the mocks and HTTP client before each test, wiring the client factory to return the
    /// mocked sync client under the service's well-known client name.
    /// </summary>
    [TestInitialize]
    public void Initialize( ) {
        _sessionManagerMock = new Mock<IATProtoSessionManager>( );
        _loggerMock = new Mock<ILogger<ATProtoStorageService>>( );
        _ = _loggerMock.Setup( x => x.IsEnabled( It.IsAny<LogLevel>( ) ) ).Returns( true );

        _httpHandlerMock = new Mock<HttpMessageHandler>( );
        _httpClient = new HttpClient( _httpHandlerMock.Object );

        _httpClientFactoryMock = new Mock<IHttpClientFactory>( );
        _ = _httpClientFactoryMock
            .Setup( f => f.CreateClient( ATProtoStorageService.ATProtoSyncHttpClientName ) )
            .Returns( _httpClient );
    }

    /// <summary>Disposes the HTTP client created for the test.</summary>
    [TestCleanup]
    public void Cleanup( ) {
        _httpClient?.Dispose( );
    }

    // ─── Happy-path tests ─────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that a repository holding three lookup records and one playlist record yields exactly
    /// the three lookup records, each with an AT-URI under
    /// <c>at://{did}/link.bridgebeats.lookup/</c>, and that the repo is fetched with a single HTTP call.
    /// </summary>
    [TestMethod]
    public async Task ListAllRecordsAsync_ThreeLookupAndOnePlaylistRecord_ReturnsThreeTuplesWithCorrectAtUris( ) {
        // Arrange: 3 lookup + 1 playlist record in the CAR.
        // The playlist record must be filtered out by the service (collection = link.bridgebeats.lookup only).
        BridgeBeats.Contracts.Records.MediaLinkResultRecord r1 = MakeRecord( "Track 1" );
        BridgeBeats.Contracts.Records.MediaLinkResultRecord r2 = MakeRecord( "Track 2" );
        BridgeBeats.Contracts.Records.MediaLinkResultRecord r3 = MakeRecord( "Track 3" );
        BridgeBeats.Contracts.Records.MediaLinkResultRecord playlist = MakeRecord( "My Playlist" );

        // Build the CAR manually to include a record in the playlist collection alongside lookups
        byte[] rec1Bytes = TestCarBuilder.BuildRecordBlock( r1 );
        byte[] rec2Bytes = TestCarBuilder.BuildRecordBlock( r2 );
        byte[] rec3Bytes = TestCarBuilder.BuildRecordBlock( r3 );
        byte[] playlistBytes = TestCarBuilder.BuildRecordBlock( playlist );

        byte[] cid1 = TestCarBuilder.ComputeCidBytes( rec1Bytes );
        byte[] cid2 = TestCarBuilder.ComputeCidBytes( rec2Bytes );
        byte[] cid3 = TestCarBuilder.ComputeCidBytes( rec3Bytes );
        byte[] plCid = TestCarBuilder.ComputeCidBytes( playlistBytes );

        // Keys sorted: link.bridgebeats.lookup/rkey1 < link.bridgebeats.lookup/rkey2 <
        //              link.bridgebeats.lookup/rkey3 < link.bridgebeats.playlist/pl1
        // Apply prefix compression with p=0 for the first, share prefix for subsequent
        string k1 = "link.bridgebeats.lookup/rkey1";
        string k2 = "link.bridgebeats.lookup/rkey2";
        string k3 = "link.bridgebeats.lookup/rkey3";
        string kpl = "link.bridgebeats.playlist/pl1";

        byte[] mstNodeBytes = TestCarBuilder.BuildMstNode(
            null,
            [
                new TestCarBuilder.MstEntry( 0, k1, cid1 ),
                new TestCarBuilder.MstEntry( CommonPrefix( k1, k2 ), k2[CommonPrefix( k1, k2 )..], cid2 ),
                new TestCarBuilder.MstEntry( CommonPrefix( k2, k3 ), k3[CommonPrefix( k2, k3 )..], cid3 ),
                new TestCarBuilder.MstEntry( CommonPrefix( k3, kpl ), kpl[CommonPrefix( k3, kpl )..], plCid )
            ]
        );
        byte[] mstCid = TestCarBuilder.ComputeCidBytes( mstNodeBytes );
        byte[] commitBytes = TestCarBuilder.BuildCommit( TestDid, mstCid );
        byte[] commitCid = TestCarBuilder.ComputeCidBytes( commitBytes );

        Dictionary<byte[], byte[]> blocks = new( ByteArrayKeyComparerLocal.Instance ) {
            [commitCid] = commitBytes,
            [mstCid] = mstNodeBytes,
            [cid1] = rec1Bytes,
            [cid2] = rec2Bytes,
            [cid3] = rec3Bytes,
            [plCid] = playlistBytes
        };
        byte[] carBytes = TestCarBuilder.BuildCar( commitCid, blocks );

        SetupHttpSuccess( carBytes );
        ATProtoStorageService service = CreateService( );

        // Act
        List<(string AtUri, MediaLinkResult Result)> results = [];
        await foreach ((string atUri, MediaLinkResult result) in
            service.ListAllRecordsAsync( s_testPdsUri, TestDid, TestContext.CancellationToken )) {
            results.Add( (atUri, result) );
        }

        // Assert: exactly 3 tuples with correct AT-URI format; playlist filtered out
        Assert.HasCount( 3, results );
        Assert.IsTrue( results.All( r => r.AtUri.StartsWith( $"at://{TestDid}/link.bridgebeats.lookup/", StringComparison.Ordinal ) ) );

        // Verify exactly one HTTP request was made
        _httpHandlerMock.Protected( ).Verify(
            "SendAsync",
            Times.Once( ),
            ItExpr.IsAny<HttpRequestMessage>( ),
            ItExpr.IsAny<CancellationToken>( )
        );
    }

    /// <summary>
    /// Computes the length of the shared leading prefix of two keys, used to build MST
    /// prefix-compressed entries in the fixtures.
    /// </summary>
    /// <param name="a">The first key.</param>
    /// <param name="b">The second key.</param>
    /// <returns>The number of leading characters the two keys share.</returns>
    private static int CommonPrefix( string a, string b ) {
        int len = Math.Min( a.Length, b.Length );
        int i = 0;
        while (i < len && a[i] == b[i]) { i++; }
        return i;
    }

    /// <summary>Verifies that an empty repository yields no records (still fetched with one HTTP call).</summary>
    [TestMethod]
    public async Task ListAllRecordsAsync_EmptyRepo_YieldsNoResults( ) {
        // Arrange
        byte[] carBytes = TestCarBuilder.BuildRepoCarWithRecords( TestDid, "link.bridgebeats.lookup", [] );
        SetupHttpSuccess( carBytes );
        ATProtoStorageService service = CreateService( );

        // Act
        List<(string AtUri, MediaLinkResult Result)> results = [];
        await foreach ((string atUri, MediaLinkResult result) in
            service.ListAllRecordsAsync( s_testPdsUri, TestDid, TestContext.CancellationToken )) {
            results.Add( (atUri, result) );
        }

        // Assert
        Assert.IsEmpty( results );

        _httpHandlerMock.Protected( ).Verify(
            "SendAsync",
            Times.Once( ),
            ItExpr.IsAny<HttpRequestMessage>( ),
            ItExpr.IsAny<CancellationToken>( )
        );
    }

    // ─── Error propagation tests ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that an HTTP 500 response from the repo fetch surfaces as
    /// <see cref="HttpRequestException"/> when the stream is enumerated.
    /// </summary>
    [TestMethod]
    public async Task ListAllRecordsAsync_Http500Response_ThrowsHttpRequestException( ) {
        // Arrange
        SetupHttpError( HttpStatusCode.InternalServerError );
        ATProtoStorageService service = CreateService( );

        // Act & Assert: download failure must throw (no fallback, no silent empty yield)
        _ = await Assert.ThrowsExactlyAsync<HttpRequestException>( async ( ) => {
            await foreach ((string _, MediaLinkResult _) in
                service.ListAllRecordsAsync( s_testPdsUri, TestDid, TestContext.CancellationToken )) {
                // Enumerate to trigger the download
            }
        } );
    }

    /// <summary>
    /// Verifies that a response body of garbage bytes (not a valid CAR) surfaces as
    /// <see cref="CarParseException"/> when the stream is enumerated.
    /// </summary>
    [TestMethod]
    public async Task ListAllRecordsAsync_MalformedCarBytes_ThrowsCarParseException( ) {
        // Arrange: return garbage bytes that won't parse as a valid CAR
        byte[] garbage = [0xFF, 0xFE, 0xFD, 0x00, 0x01, 0x02];
        SetupHttpSuccess( garbage );
        ATProtoStorageService service = CreateService( );

        // Act & Assert
        _ = await Assert.ThrowsExactlyAsync<CarParseException>( async ( ) => {
            await foreach ((string _, MediaLinkResult _) in
                service.ListAllRecordsAsync( s_testPdsUri, TestDid, TestContext.CancellationToken )) { }
        } );
    }

    /// <summary>
    /// Verifies that a record block that cannot be deserialized into a result is skipped while the
    /// well-formed record in the same repository is still yielded, so one bad record does not abort the
    /// enumeration.
    /// </summary>
    [TestMethod]
    public async Task ListAllRecordsAsync_OneBadRecordAmongGood_SkipsBadAndYieldsRest( ) {
        // Arrange: build a CAR with one good record and one block that is valid DAG-CBOR
        // but fails MediaLinkResultRecord deserialization (lookedUpAt is an integer, not an ISO 8601 string).
        BridgeBeats.Contracts.Records.MediaLinkResultRecord goodRecord = MakeRecord( "Good Track" );
        byte[] goodBytes = TestCarBuilder.BuildRecordBlock( goodRecord );
        byte[] goodCid = TestCarBuilder.ComputeCidBytes( goodBytes );

        // Build a malformed DAG-CBOR block: lookedUpAt is a number, not a string — valid CBOR, invalid record
        byte[] badBytes = BuildMalformedRecordBlock( );
        byte[] badCid = TestCarBuilder.ComputeCidBytes( badBytes );

        // Keys sorted lexicographically: "link.bridgebeats.lookup/bad_rkey_alpha" < "link.bridgebeats.lookup/good_rkey_beta"
        // They share the 24-char prefix "link.bridgebeats.lookup/"
        byte[] mstNodeBytes = TestCarBuilder.BuildMstNode(
            null,
            [
                new TestCarBuilder.MstEntry( 0, "link.bridgebeats.lookup/bad_rkey_alpha", badCid ),
                new TestCarBuilder.MstEntry( 24, "good_rkey_beta", goodCid )
            ]
        );
        byte[] mstCid = TestCarBuilder.ComputeCidBytes( mstNodeBytes );
        byte[] commitBytes = TestCarBuilder.BuildCommit( TestDid, mstCid );
        byte[] commitCid = TestCarBuilder.ComputeCidBytes( commitBytes );

        Dictionary<byte[], byte[]> blocks = new( ByteArrayKeyComparerLocal.Instance ) {
            [commitCid] = commitBytes,
            [mstCid] = mstNodeBytes,
            [goodCid] = goodBytes,
            [badCid] = badBytes
        };
        byte[] carBytes = TestCarBuilder.BuildCar( commitCid, blocks );

        SetupHttpSuccess( carBytes );
        ATProtoStorageService service = CreateService( );

        // Act
        List<(string AtUri, MediaLinkResult Result)> results = [];
        await foreach ((string atUri, MediaLinkResult result) in
            service.ListAllRecordsAsync( s_testPdsUri, TestDid, TestContext.CancellationToken )) {
            results.Add( (atUri, result) );
        }

        // Assert: only the good record returned; bad record skipped via LogCarRecordSkipped path
        Assert.HasCount( 1, results );
        Assert.IsTrue( results[0].AtUri.Contains( "good_rkey_beta", StringComparison.Ordinal ) );
    }

    /// <summary>
    /// Builds a CBOR record block whose shape does not match a valid persisted lookup record (here
    /// <c>lookedUpAt</c> is an integer rather than a timestamp), used to exercise the skip-bad-record
    /// path. The block is structurally valid CBOR but fails
    /// <see cref="BridgeBeats.Contracts.Records.MediaLinkResultRecord"/> deserialization.
    /// </summary>
    /// <returns>The encoded malformed record bytes.</returns>
    private static byte[] BuildMalformedRecordBlock( ) {
        System.Formats.Cbor.CborWriter writer = new( System.Formats.Cbor.CborConformanceMode.Lax );
        writer.WriteStartMap( null );
        // lookedUpAt as integer — valid CBOR, but System.Text.Json cannot parse it as DateTimeOffset
        writer.WriteTextString( "lookedUpAt" );
        writer.WriteInt32( 9999999 );
        // results array — empty but present
        writer.WriteTextString( "results" );
        writer.WriteStartArray( null );
        writer.WriteEndArray( );
        writer.WriteEndMap( );
        return writer.Encode( );
    }

    /// <summary>
    /// Equality comparer that treats byte arrays as keys by their contents, so a CID's raw bytes can key
    /// the in-memory block dictionary used to assemble a CAR fixture.
    /// </summary>
    private sealed class ByteArrayKeyComparerLocal : System.Collections.Generic.IEqualityComparer<byte[]> {
        /// <summary>Shared singleton instance.</summary>
        internal static readonly ByteArrayKeyComparerLocal Instance = new( );

        /// <summary>Compares two byte arrays for content equality.</summary>
        /// <param name="x">The first array.</param>
        /// <param name="y">The second array.</param>
        /// <returns><see langword="true"/> when both are non-null and have identical contents.</returns>
        public bool Equals( byte[]? x, byte[]? y ) => x is not null && y is not null && x.AsSpan( ).SequenceEqual( y );

        /// <summary>Computes a content-based hash code for a byte array.</summary>
        /// <param name="obj">The array to hash.</param>
        /// <returns>A hash code derived from the array contents.</returns>
        public int GetHashCode( byte[] obj ) {
            System.HashCode hc = new( );
            hc.AddBytes( obj );
            return hc.ToHashCode( );
        }
    }

    /// <summary>
    /// Verifies that a malformed DID is rejected with <see cref="ArgumentException"/> before any HTTP
    /// request is made, confirming input validation happens up front.
    /// </summary>
    [TestMethod]
    public async Task ListAllRecordsAsync_InvalidDid_ThrowsArgumentException( ) {
        // Arrange: invalid DID format
        ATProtoStorageService service = CreateService( );

        // Act & Assert: should throw before making any HTTP call
        _ = await Assert.ThrowsExactlyAsync<ArgumentException>( async ( ) => {
            await foreach ((string _, MediaLinkResult _) in
                service.ListAllRecordsAsync( s_testPdsUri, "not-a-valid-did", TestContext.CancellationToken )) { }
        } );

        // No HTTP call should have been made
        _httpHandlerMock.Protected( ).Verify(
            "SendAsync",
            Times.Never( ),
            ItExpr.IsAny<HttpRequestMessage>( ),
            ItExpr.IsAny<CancellationToken>( )
        );
    }

    // ─── CancellationToken propagation ───────────────────────────────────────

    /// <summary>
    /// Verifies that enumerating with an already-cancelled token propagates
    /// <see cref="OperationCanceledException"/> (and not a <see cref="CarParseException"/> wrapping the
    /// cancellation), confirming cancellation is honored rather than swallowed by the parse catch.
    /// </summary>
    [TestMethod]
    public async Task ListAllRecordsAsync_CancelledToken_PropagatesOperationCanceledException( ) {
        // Arrange: build a valid CAR so the parse path is exercised, then cancel before iteration completes.
        byte[] carBytes = TestCarBuilder.BuildRepoCarWithRecords(
            TestDid,
            "link.bridgebeats.lookup",
            [("rkey1", MakeRecord( "Track 1" ))]
        );
        SetupHttpSuccess( carBytes );
        ATProtoStorageService service = CreateService( );

        using CancellationTokenSource cts = new( );
        cts.Cancel( ); // pre-cancelled

        // Act & Assert
        _ = await Assert.ThrowsExactlyAsync<OperationCanceledException>( async ( ) => {
            await foreach ((string _, MediaLinkResult _) in
                service.ListAllRecordsAsync( s_testPdsUri, TestDid, cts.Token )) { }
        } );
    }

    // ─── SEC-005 regression: rkey validation ─────────────────────────────────

    /// <summary>
    /// Verifies that a record whose rkey contains an invalid character (a control character) is skipped
    /// while a record with a valid rkey is still yielded, confirming rkey validation filters bad keys
    /// without failing the whole enumeration. Without rkey validation, the invalid rkey would reach the
    /// AT-URI builder (constructing a malformed URI) and be logged verbatim (a log-injection risk).
    /// </summary>
    [TestMethod]
    public async Task ListAllRecordsAsync_RkeyWithControlChars_SkipsInvalidRkeyAndYieldsGoodRecord( ) {
        // Arrange: build a single CAR containing one good rkey and one rkey containing a control char (0x01, invalid).
        byte[] recGoodBytes = TestCarBuilder.BuildRecordBlock( MakeRecord( "Good" ) );
        byte[] recBadBytes = TestCarBuilder.BuildRecordBlock( MakeRecord( "Bad" ) );
        byte[] cidGood = TestCarBuilder.ComputeCidBytes( recGoodBytes );
        byte[] cidBad = TestCarBuilder.ComputeCidBytes( recBadBytes );

        // Keys must be sorted. "link.bridgebeats.lookup/good-rkey" < "link.bridgebeats.lookup/\x01bad"
        // is not guaranteed, so use unique keys with known sort order.
        string keyGood = "link.bridgebeats.lookup/good-rkey";
        string keyBad = "link.bridgebeats.lookup/bad\x01rkey"; // contains control char

        // Sort entries lexicographically
        (string, byte[])[] sorted = [(keyGood, cidGood), (keyBad, cidBad)];
        System.Array.Sort( sorted, ( a, b ) => string.Compare( a.Item1, b.Item1, StringComparison.Ordinal ) );

        List<TestCarBuilder.MstEntry> mstEntries = [];
        string prev = "";
        foreach ((string k, byte[] c) in sorted) {
            int p = CommonPrefix( prev, k );
            mstEntries.Add( new TestCarBuilder.MstEntry( p, k[p..], c ) );
            prev = k;
        }

        byte[] mstNodeBytes = TestCarBuilder.BuildMstNode( null, mstEntries );
        byte[] mstCid = TestCarBuilder.ComputeCidBytes( mstNodeBytes );
        byte[] commitBytes = TestCarBuilder.BuildCommit( TestDid, mstCid );
        byte[] commitCid = TestCarBuilder.ComputeCidBytes( commitBytes );

        Dictionary<byte[], byte[]> blocks = new( ByteArrayKeyComparerLocal.Instance ) {
            [commitCid] = commitBytes,
            [mstCid] = mstNodeBytes,
            [cidGood] = recGoodBytes,
            [cidBad] = recBadBytes
        };
        byte[] carBytes = TestCarBuilder.BuildCar( commitCid, blocks );

        SetupHttpSuccess( carBytes );
        ATProtoStorageService service = CreateService( );

        // Act
        List<(string AtUri, MediaLinkResult Result)> results = [];
        await foreach ((string atUri, MediaLinkResult result) in
            service.ListAllRecordsAsync( s_testPdsUri, TestDid, TestContext.CancellationToken )) {
            results.Add( (atUri, result) );
        }

        // Assert: only the record with a valid rkey is returned; the one with control chars is skipped
        Assert.HasCount( 1, results );
        Assert.IsTrue( results[0].AtUri.Contains( "good-rkey", StringComparison.Ordinal ) );
    }

    // ─── Helper methods ───────────────────────────────────────────────────────

    /// <summary>Builds the service under test from the configured mocks.</summary>
    /// <returns>A new <see cref="ATProtoStorageService"/> instance.</returns>
    private ATProtoStorageService CreateService( ) =>
        new(
            _sessionManagerMock.Object,
            _loggerMock.Object,
            _httpClientFactoryMock.Object
        );

    /// <summary>
    /// Configures the HTTP handler to return a 200 response whose body is the supplied CAR bytes with
    /// the IPLD CAR content type.
    /// </summary>
    /// <param name="carBytes">The CAR payload to return.</param>
    private void SetupHttpSuccess( byte[] carBytes ) {
        _ = _httpHandlerMock
            .Protected( )
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>( ),
                ItExpr.IsAny<CancellationToken>( )
            )
            .ReturnsAsync( new HttpResponseMessage( HttpStatusCode.OK ) {
                Content = new ByteArrayContent( carBytes ) {
                    Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue( "application/vnd.ipld.car" ) }
                }
            } );
    }

    /// <summary>Configures the HTTP handler to return a response with the supplied error status code.</summary>
    /// <param name="statusCode">The HTTP status code to return.</param>
    private void SetupHttpError( HttpStatusCode statusCode ) {
        _ = _httpHandlerMock
            .Protected( )
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>( ),
                ItExpr.IsAny<CancellationToken>( )
            )
            .ReturnsAsync( new HttpResponseMessage( statusCode ) );
    }

    /// <summary>
    /// Builds a well-formed persisted lookup record with a single Spotify provider result, used as
    /// block content in the CAR fixtures.
    /// </summary>
    /// <param name="title">The track title to embed in the record.</param>
    /// <returns>A populated <see cref="BridgeBeats.Contracts.Records.MediaLinkResultRecord"/>.</returns>
    private static BridgeBeats.Contracts.Records.MediaLinkResultRecord MakeRecord( string title ) =>
        new(
            results: [
                new BridgeBeats.Contracts.Records.ProviderResultRecord(
                    provider: "spotify",
                    artist: "Test Artist",
                    title: title,
                    url: "https://open.spotify.com/track/x",
                    marketRegion: "US",
                    externalId: "ISRC123",
                    artUrl: null,
                    isAlbum: false
                )
            ],
            lookedUpAt: new DateTimeOffset( 2024, 3, 1, 12, 0, 0, TimeSpan.Zero )
        );
}

#pragma warning restore CS1591
