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
/// Unit tests for <see cref="ATProtoStorageService.ListAllRecordsAsync"/> using
/// a mocked HTTP layer (no real network calls).
/// </summary>
[TestClass]
public class ATProtoStorageServiceListRecordsTests {

    /// <summary>Gets or sets the test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    private Mock<IATProtoSessionManager> _sessionManagerMock = null!;
    private Mock<ILogger<ATProtoStorageService>> _loggerMock = null!;
    private Mock<HttpMessageHandler> _httpHandlerMock = null!;
    private Mock<IHttpClientFactory> _httpClientFactoryMock = null!;
    private HttpClient _httpClient = null!;

    private const string TestDid = "did:plc:testuser12345";
    private static readonly Uri s_testPdsUri = new( "https://pds.test.example" );

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

    [TestCleanup]
    public void Cleanup( ) {
        _httpClient?.Dispose( );
    }

    // ─── Happy-path tests ─────────────────────────────────────────────────────

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

    private static int CommonPrefix( string a, string b ) {
        int len = Math.Min( a.Length, b.Length );
        int i = 0;
        while (i < len && a[i] == b[i]) { i++; }
        return i;
    }

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
    /// Builds a DAG-CBOR block that is structurally valid CBOR but fails
    /// <see cref="BridgeBeats.Contracts.Records.MediaLinkResultRecord"/> deserialization
    /// because <c>lookedUpAt</c> is an integer rather than an ISO 8601 string.
    /// </summary>
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

    private sealed class ByteArrayKeyComparerLocal : System.Collections.Generic.IEqualityComparer<byte[]> {
        internal static readonly ByteArrayKeyComparerLocal Instance = new( );
        public bool Equals( byte[]? x, byte[]? y ) => x is not null && y is not null && x.AsSpan( ).SequenceEqual( y );
        public int GetHashCode( byte[] obj ) {
            System.HashCode hc = new( );
            hc.AddBytes( obj );
            return hc.ToHashCode( );
        }
    }

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
    /// Verifies that cancelling the token passed to ListAllRecordsAsync propagates
    /// OperationCanceledException and NOT a CarParseException wrapping the cancellation.
    /// Failure-first evidence: if the cancellation were swallowed by the CarParseException catch
    /// in FetchAllViaCarAsync, this test would fail (no exception thrown, or wrong exception type).
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
    /// SEC-005 regression: an rkey containing control characters or path separators must be
    /// skipped (with a warning) rather than passed to BuildLookupRecordUri or logged verbatim.
    /// The good record in the same CAR must still be returned.
    /// Failure-first evidence: without IsValidRkey in FetchAllViaCarAsync, the invalid rkey
    /// would reach ATProtoUriHelper.BuildLookupRecordUri (potentially constructing a malformed URI)
    /// and be logged verbatim (log injection risk). The test would return 2 results instead of 1.
    /// </summary>
    [TestMethod]
    public async Task ListAllRecordsAsync_RkeyWithControlChars_SkipsInvalidRkeyAndYieldsGoodRecord( ) {
        // Arrange: build a CAR with one good rkey and one rkey containing a slash (invalid).
        // The invalid rkey is "bad\x01rkey" (contains control char 0x01).
        byte[] goodCarBytes = TestCarBuilder.BuildCarWithRawRkey(
            "link.bridgebeats.lookup",
            "valid-rkey-123"
        );

        // Merge the invalid-rkey block into a single CAR by building both entries in one MST node.
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

    private ATProtoStorageService CreateService( ) =>
        new(
            _sessionManagerMock.Object,
            _loggerMock.Object,
            _httpClientFactoryMock.Object
        );

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
