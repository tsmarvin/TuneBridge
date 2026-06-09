using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Storage.Car;
using BridgeBeats.Tests.Unit.Helpers;

#pragma warning disable CS1591

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Tests for <see cref="MstWalker.EnumerateRecords"/>.
/// </summary>
[TestClass]
public class MstWalkerTests {

    /// <summary>Gets or sets the test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    private static MediaLinkResultRecord MakeRecord( string artist = "Test", string title = "Track" ) =>
        new(
            results: [
                new ProviderResultRecord(
                    provider: "spotify",
                    artist: artist,
                    title: title,
                    url: "https://open.spotify.com/track/x",
                    marketRegion: "US"
                )
            ],
            lookedUpAt: new DateTimeOffset( 2024, 1, 1, 0, 0, 0, TimeSpan.Zero )
        );

    private List<(string Key, string ValueCidHex)> EnumerateAll( CarFile car ) =>
        [.. MstWalker.EnumerateRecords( car, TestContext.CancellationToken ).Records];

    [TestMethod]
    public void EnumerateRecords_EmptyRepo_YieldsNothing( ) {
        byte[] carBytes = TestCarBuilder.BuildRepoCarWithRecords(
            "did:plc:test",
            "link.bridgebeats.lookup",
            []
        );
        CarFile car = CarV1Reader.Read( carBytes );

        List<(string Key, string ValueCidHex)> records = EnumerateAll( car );

        Assert.IsEmpty( records );
    }

    [TestMethod]
    public void EnumerateRecords_SingleRecord_YieldsOneKey( ) {
        byte[] carBytes = TestCarBuilder.BuildRepoCarWithRecords(
            "did:plc:test",
            "link.bridgebeats.lookup",
            [("rkey1", MakeRecord( ))]
        );
        CarFile car = CarV1Reader.Read( carBytes );

        List<(string Key, string ValueCidHex)> records = EnumerateAll( car );

        Assert.HasCount( 1, records );
        Assert.AreEqual( "link.bridgebeats.lookup/rkey1", records[0].Key );
    }

    [TestMethod]
    public void EnumerateRecords_MultipleRecords_YieldsInKeyOrder( ) {
        (string Rkey, MediaLinkResultRecord Record)[] entries = [
            ("zzz", MakeRecord( title: "zzz" )),
            ("aaa", MakeRecord( title: "aaa" )),
            ("mmm", MakeRecord( title: "mmm" ))
        ];
        byte[] carBytes = TestCarBuilder.BuildRepoCarWithRecords(
            "did:plc:test",
            "link.bridgebeats.lookup",
            entries
        );
        CarFile car = CarV1Reader.Read( carBytes );

        List<(string Key, string ValueCidHex)> records = EnumerateAll( car );

        Assert.HasCount( 3, records );
        Assert.AreEqual( "link.bridgebeats.lookup/aaa", records[0].Key );
        Assert.AreEqual( "link.bridgebeats.lookup/mmm", records[1].Key );
        Assert.AreEqual( "link.bridgebeats.lookup/zzz", records[2].Key );
    }

    [TestMethod]
    public void EnumerateRecords_PrefixCompression_CorrectlyReconstructs( ) {
        (string Rkey, MediaLinkResultRecord Record)[] entries = [
            ("spotify:track:abc123def456", MakeRecord( title: "Track 1" )),
            ("spotify:track:abc123xyz789", MakeRecord( title: "Track 2" ))
        ];
        byte[] carBytes = TestCarBuilder.BuildRepoCarWithRecords(
            "did:plc:test",
            "link.bridgebeats.lookup",
            entries
        );
        CarFile car = CarV1Reader.Read( carBytes );

        List<(string Key, string ValueCidHex)> records = EnumerateAll( car );

        Assert.HasCount( 2, records );
        Assert.AreEqual( "link.bridgebeats.lookup/spotify:track:abc123def456", records[0].Key );
        Assert.AreEqual( "link.bridgebeats.lookup/spotify:track:abc123xyz789", records[1].Key );
    }

    [TestMethod]
    public void EnumerateRecords_MultiNodeTree_InOrderTraversal( ) {
        // Build a three-node MST:
        //   root: l=leftNode, entries=[{p=0, k="link.bridgebeats.lookup/mmm", v=rec2, t=rightNode}]
        //   leftNode: l=null, entries=[{p=0, k="link.bridgebeats.lookup/aaa", v=rec1, t=null}]
        //   rightNode: l=null, entries=[{p=0, k="link.bridgebeats.lookup/zzz", v=rec3, t=null}]
        // In-order: leftNode(aaa) → root(mmm) → rightNode(zzz)
        // Also validates prevKeyInNode propagation: root entry p=0 reconstructs "link.bridgebeats.lookup/mmm"
        // from scratch (not relative to "aaa"), and rightNode entry p=0 reconstructs "link.bridgebeats.lookup/zzz".
        byte[] rec1Bytes = TestCarBuilder.BuildRecordBlock( MakeRecord( title: "aaa" ) );
        byte[] rec2Bytes = TestCarBuilder.BuildRecordBlock( MakeRecord( title: "mmm" ) );
        byte[] rec3Bytes = TestCarBuilder.BuildRecordBlock( MakeRecord( title: "zzz" ) );

        byte[] cid1 = TestCarBuilder.ComputeCidBytes( rec1Bytes );
        byte[] cid2 = TestCarBuilder.ComputeCidBytes( rec2Bytes );
        byte[] cid3 = TestCarBuilder.ComputeCidBytes( rec3Bytes );

        byte[] leftNodeBytes = TestCarBuilder.BuildMstNode(
            null,
            [new TestCarBuilder.MstEntry( 0, "link.bridgebeats.lookup/aaa", cid1 )]
        );
        byte[] leftCid = TestCarBuilder.ComputeCidBytes( leftNodeBytes );

        byte[] rightNodeBytes = TestCarBuilder.BuildMstNode(
            null,
            [new TestCarBuilder.MstEntry( 0, "link.bridgebeats.lookup/zzz", cid3 )]
        );
        byte[] rightCid = TestCarBuilder.ComputeCidBytes( rightNodeBytes );

        byte[] rootNodeBytes = TestCarBuilder.BuildMstNode(
            leftCid,
            [new TestCarBuilder.MstEntry( 0, "link.bridgebeats.lookup/mmm", cid2, rightCid )]
        );
        byte[] rootCid = TestCarBuilder.ComputeCidBytes( rootNodeBytes );

        byte[] commitBytes = TestCarBuilder.BuildCommit( "did:plc:test", rootCid );
        byte[] commitCid = TestCarBuilder.ComputeCidBytes( commitBytes );

        Dictionary<byte[], byte[]> blocks = new( ByteArrayKeyComparer.Instance ) {
            [commitCid] = commitBytes,
            [rootCid] = rootNodeBytes,
            [leftCid] = leftNodeBytes,
            [rightCid] = rightNodeBytes,
            [cid1] = rec1Bytes,
            [cid2] = rec2Bytes,
            [cid3] = rec3Bytes
        };

        byte[] carBytes = TestCarBuilder.BuildCar( commitCid, blocks );
        CarFile car = CarV1Reader.Read( carBytes );

        List<(string Key, string ValueCidHex)> records = EnumerateAll( car );

        Assert.HasCount( 3, records );
        Assert.AreEqual( "link.bridgebeats.lookup/aaa", records[0].Key );
        Assert.AreEqual( "link.bridgebeats.lookup/mmm", records[1].Key );
        Assert.AreEqual( "link.bridgebeats.lookup/zzz", records[2].Key );
    }

    [TestMethod]
    public void EnumerateRecords_MissingValueBlock_ThrowsCarParseException( ) {
        // Build a CAR where an MST entry's value CID is not included in the CAR blocks.
        // The MST references a value block that was never added — this exercises
        // CarRepoReader's throw at "Value block ... not found in CAR."
        byte[] recordBytes = TestCarBuilder.BuildRecordBlock( MakeRecord( ) );
        byte[] validRecordCid = TestCarBuilder.ComputeCidBytes( recordBytes );

        // Create a "dangling" CID that doesn't correspond to any block in the CAR
        byte[] danglingCid = TestCarBuilder.ComputeCidBytes( new byte[] { 0xDE, 0xAD, 0xBE, 0xEF } );

        // MST node references danglingCid as the value — no corresponding block present
        byte[] mstNodeBytes = TestCarBuilder.BuildMstNode(
            null,
            [new TestCarBuilder.MstEntry( 0, "link.bridgebeats.lookup/rkey1", danglingCid )]
        );
        byte[] mstCid = TestCarBuilder.ComputeCidBytes( mstNodeBytes );
        byte[] commitBytes = TestCarBuilder.BuildCommit( "did:plc:test", mstCid );
        byte[] commitCid = TestCarBuilder.ComputeCidBytes( commitBytes );

        // Deliberately omit the value block whose CID is danglingCid
        Dictionary<byte[], byte[]> blocks = new( ByteArrayKeyComparer.Instance ) {
            [commitCid] = commitBytes,
            [mstCid] = mstNodeBytes
            // danglingCid block intentionally absent
        };

        byte[] carBytes = TestCarBuilder.BuildCar( commitCid, blocks );

        _ = Assert.ThrowsExactly<CarParseException>( ( ) => {
            _ = CarRepoReader.EnumerateCollection( carBytes, "link.bridgebeats.lookup", CancellationToken.None ).Records.ToList( );
        } );
    }

    [TestMethod]
    public void EnumerateRecords_InvalidPrefixLen_ThrowsCarParseException( ) {
        byte[] recordBytes = TestCarBuilder.BuildRecordBlock( MakeRecord( ) );
        byte[] recordCid = TestCarBuilder.ComputeCidBytes( recordBytes );

        // Second entry: p=100 (way bigger than the previous key length)
        byte[] mstNodeBytes = TestCarBuilder.BuildMstNode(
            null,
            [
                new TestCarBuilder.MstEntry( 0, "link.bridgebeats.lookup/rkey1", recordCid ),
                new TestCarBuilder.MstEntry( 100, "overflow", recordCid )
            ]
        );
        byte[] mstCid = TestCarBuilder.ComputeCidBytes( mstNodeBytes );
        byte[] commitBytes = TestCarBuilder.BuildCommit( "did:plc:test", mstCid );
        byte[] commitCid = TestCarBuilder.ComputeCidBytes( commitBytes );

        Dictionary<byte[], byte[]> blocks = new( ByteArrayKeyComparer.Instance ) {
            [commitCid] = commitBytes,
            [mstCid] = mstNodeBytes,
            [recordCid] = recordBytes
        };

        byte[] carBytes = TestCarBuilder.BuildCar( commitCid, blocks );
        CarFile car = CarV1Reader.Read( carBytes );

        _ = Assert.ThrowsExactly<CarParseException>( ( ) => {
            _ = MstWalker.EnumerateRecords( car, CancellationToken.None ).Records.ToList( );
        } );
    }

    // ─── QA Major: commit version != 3 warn-and-proceed contract ─────────────

    /// <summary>
    /// Verifies that a commit with version=4 still yields records (warn-and-proceed contract).
    /// Failure-first evidence: before the version field was added to CarEnumerationResult and the
    /// warn-and-proceed branch added to ATProtoStorageService, this test had no surface to assert.
    /// </summary>
    [TestMethod]
    public void EnumerateRecords_CommitVersion4_YieldsRecordsAndReportsVersion4( ) {
        byte[] carBytes = TestCarBuilder.BuildRepoCarWithRecordsAndVersion(
            "did:plc:test",
            "link.bridgebeats.lookup",
            [("rkey1", MakeRecord( ))],
            commitVersion: 4
        );
        CarFile car = CarV1Reader.Read( carBytes );

        (IEnumerable<(string Key, string ValueCidHex)> records, int commitVersion) =
            MstWalker.EnumerateRecords( car, TestContext.CancellationToken );

        List<(string Key, string ValueCidHex)> list = [.. records];
        Assert.AreEqual( 4, commitVersion, "CommitVersion must surface the actual version from the commit block." );
        Assert.HasCount( 1, list );
        Assert.AreEqual( "link.bridgebeats.lookup/rkey1", list[0].Key );
    }

    // ─── SEC-001 regression: doubling DAG ────────────────────────────────────

    /// <summary>
    /// SEC-001 regression: a CAR where one MST node is referenced twice (once via 'l' and once via
    /// an entry's 't') must throw CarParseException, not loop indefinitely or OOM.
    /// The test uses [Timeout] to ensure termination within 5 seconds even if the guard fails.
    /// Failure-first evidence: without the visited-set check in WalkNode, this test would loop until
    /// the depth cap fires (depth 64) rather than immediately detecting the re-visit.
    /// </summary>
    [TestMethod]
    [Timeout( 5000 )]
    public void EnumerateRecords_DoublingDagCar_ThrowsCarParseException( ) {
        byte[] carBytes = TestCarBuilder.BuildDoublingDagCar( );
        CarFile car = CarV1Reader.Read( carBytes );

        _ = Assert.ThrowsExactly<CarParseException>( ( ) => {
            _ = MstWalker.EnumerateRecords( car, CancellationToken.None ).Records.ToList( );
        } );
    }

    // ─── SEC-003 regression: negative p ──────────────────────────────────────

    /// <summary>
    /// SEC-003 regression: a non-first MST entry with p=-1 must produce CarParseException,
    /// NOT ArgumentOutOfRangeException (which would escape the CarParseException/OCE-only catch
    /// in ATProtoStorageService and propagate as an unhandled exception).
    /// Failure-first evidence: without the `prefixLen &lt; 0` guard in ReadEntries, this test fails
    /// because ThrowsExactly&lt;CarParseException&gt; does not match ArgumentOutOfRangeException.
    /// </summary>
    [TestMethod]
    public void EnumerateRecords_NegativePrefixLen_ThrowsCarParseException( ) {
        byte[] carBytes = TestCarBuilder.BuildCarWithNegativePrefixLen( -1 );
        CarFile car = CarV1Reader.Read( carBytes );

        _ = Assert.ThrowsExactly<CarParseException>( ( ) => {
            _ = MstWalker.EnumerateRecords( car, CancellationToken.None ).Records.ToList( );
        } );
    }

    // ─── Hardening: null/absent "data" link rejected ─────────────────────────

    /// <summary>
    /// Verifies that a commit block whose "data" field is explicitly CBOR null is rejected with
    /// CarParseException, regardless of the commit version.
    /// Failure-first evidence: before Fix 3, the condition was `mstRootHex is null &amp;&amp; commitVersion == 0`;
    /// with a version=3 commit, a null data link was accepted and EnumerateRecords returned an empty
    /// sequence instead of throwing — so this test would have failed on ThrowsExactly.
    /// </summary>
    [TestMethod]
    public void EnumerateRecords_CommitWithNullDataLink_ThrowsCarParseException( ) {
        byte[] commitBytes = TestCarBuilder.BuildCommitWithNullData( "did:plc:test", version: 3 );
        byte[] commitCid = TestCarBuilder.ComputeCidBytes( commitBytes );

        Dictionary<byte[], byte[]> blocks = new( ByteArrayKeyComparer.Instance ) {
            [commitCid] = commitBytes
        };
        byte[] carBytes = TestCarBuilder.BuildCar( commitCid, blocks );
        CarFile car = CarV1Reader.Read( carBytes );

        _ = Assert.ThrowsExactly<CarParseException>( ( ) => {
            _ = MstWalker.EnumerateRecords( car, CancellationToken.None ).Records.ToList( );
        } );
    }

    // ─── Resource-exhaustion guards ──────────────────────────────────────────

    /// <summary>
    /// Verifies that a left-child chain of 66 MST nodes throws CarParseException due to the
    /// MaxWalkDepth guard at the start of WalkNode (depth &gt; MaxWalkDepth),
    /// before the duplicate-Add cycle check or the block-not-found throw.
    ///
    /// Construction: nodes[0]..nodes[65] form a pure left-child chain (no entries).
    /// nodes[65] is a leaf (l=null, e=[]). Commit → nodes[0].
    /// Walk: nodes[0]@depth=0 → nodes[1]@depth=1 → ... → nodes[64]@depth=64 →
    ///        nodes[65]@depth=65 → 65 &gt; 64 → CarParseException.
    ///
    /// Failure-first evidence: without the MaxWalkDepth guard, the walk enters
    /// nodes[65] at depth=65 without throwing, parses it as a leaf (l=null, e=[]), yields nothing,
    /// and unwinds back to nodes[0]. The enumeration returns an empty list — no exception is thrown —
    /// so ThrowsExactly&lt;CarParseException&gt; fails, confirming the guard is load-bearing.
    /// </summary>
    [TestMethod]
    [Timeout( 5000 )]
    public void EnumerateRecords_DepthExceeded_ThrowsCarParseException( ) {
        // Build a chain of 66 nodes in reverse (leaf first, root last).
        // nodes[0]..nodes[64] each have l=nodes[i+1]; nodes[65] is the leaf.
        // Depth 65 (nodes[65]) is the first call that exceeds MaxWalkDepth (64).
        const int ChainLength = 66;
        byte[][] nodeCids = new byte[ChainLength][];
        byte[][] nodeBlocks = new byte[ChainLength][];

        nodeBlocks[ChainLength - 1] = TestCarBuilder.BuildMstNode( null, [] );
        nodeCids[ChainLength - 1] = TestCarBuilder.ComputeCidBytes( nodeBlocks[ChainLength - 1] );

        for (int i = ChainLength - 2; i >= 0; i--) {
            nodeBlocks[i] = TestCarBuilder.BuildMstNode( nodeCids[i + 1], [] );
            nodeCids[i] = TestCarBuilder.ComputeCidBytes( nodeBlocks[i] );
        }

        byte[] commitBytes = TestCarBuilder.BuildCommit( "did:plc:depth-test", nodeCids[0] );
        byte[] commitCid = TestCarBuilder.ComputeCidBytes( commitBytes );

        Dictionary<byte[], byte[]> blocks = new( ByteArrayKeyComparer.Instance );
        blocks[commitCid] = commitBytes;
        for (int i = 0; i < ChainLength; i++) {
            blocks[nodeCids[i]] = nodeBlocks[i];
        }

        byte[] carBytes = TestCarBuilder.BuildCar( commitCid, blocks );
        CarFile car = CarV1Reader.Read( carBytes );

        _ = Assert.ThrowsExactly<CarParseException>( ( ) => {
            _ = MstWalker.EnumerateRecords( car, CancellationToken.None ).Records.ToList( );
        } );
    }

    /// <summary>
    /// Verifies that a single MST node whose entry count exceeds car.Blocks.Count throws
    /// CarParseException due to the recordCount cap in the entry loop
    /// (recordCount[0] &gt; maxNodes).
    ///
    /// Construction: 1 commit block + 1 MST node = 2 blocks total; maxNodes = 2.
    /// The MST node contains 3 entries (all with p=0, distinct keys, shared placeholder value CID).
    /// Walk sequence: entry[0] → recordCount=1 ≤ 2, yield; entry[1] → recordCount=2 ≤ 2, yield;
    ///                entry[2] → recordCount=3 &gt; 2 → CarParseException.
    /// MstWalker does not resolve value-block CIDs during WalkNode (they are yielded as hex
    /// strings), so the placeholder CID not being in car.Blocks is irrelevant.
    ///
    /// Guard ordering: the MaxWalkDepth guard and the duplicate-Add cycle check do not fire
    /// first because there is only one root node at depth=0 visited exactly once.
    ///
    /// Failure-first evidence: without the recordCount cap in the entry loop, all 3
    /// entries yield normally, the enumeration returns a 3-element list, and
    /// ThrowsExactly&lt;CarParseException&gt; fails because no exception is thrown.
    /// </summary>
    [TestMethod]
    public void EnumerateRecords_RecordCountExceedsBlockCount_ThrowsCarParseException( ) {
        // Placeholder value CID — not in blocks; WalkNode never resolves value-block CIDs.
        byte[] placeholderValueCid = TestCarBuilder.ComputeCidBytes( [0xDE, 0xAD] );

        // 3 entries in a single root node; 1 commit + 1 MST node = 2 blocks → maxNodes = 2.
        byte[] mstNodeBytes = TestCarBuilder.BuildMstNode(
            null,
            [
                new TestCarBuilder.MstEntry( 0, "col/key1", placeholderValueCid ),
                new TestCarBuilder.MstEntry( 0, "col/key2", placeholderValueCid ),
                new TestCarBuilder.MstEntry( 0, "col/key3", placeholderValueCid ),
            ] );
        byte[] mstCid = TestCarBuilder.ComputeCidBytes( mstNodeBytes );

        byte[] commitBytes = TestCarBuilder.BuildCommit( "did:plc:amp-test", mstCid );
        byte[] commitCid = TestCarBuilder.ComputeCidBytes( commitBytes );

        Dictionary<byte[], byte[]> blocks = new( ByteArrayKeyComparer.Instance ) {
            [commitCid] = commitBytes,
            [mstCid] = mstNodeBytes
            // placeholderValueCid block intentionally absent — value blocks are not resolved by WalkNode
        };

        byte[] carBytes = TestCarBuilder.BuildCar( commitCid, blocks );
        CarFile car = CarV1Reader.Read( carBytes );

        _ = Assert.ThrowsExactly<CarParseException>( ( ) => {
            _ = MstWalker.EnumerateRecords( car, CancellationToken.None ).Records.ToList( );
        } );
    }

    // ─── CancellationToken propagation ───────────────────────────────────────

    /// <summary>
    /// Verifies that passing an already-cancelled token to EnumerateRecords propagates
    /// OperationCanceledException, NOT CarParseException.
    /// </summary>
    [TestMethod]
    public void EnumerateRecords_CancelledToken_ThrowsOperationCanceledException( ) {
        byte[] carBytes = TestCarBuilder.BuildRepoCarWithRecords(
            "did:plc:test",
            "link.bridgebeats.lookup",
            [("rkey1", MakeRecord( )), ("rkey2", MakeRecord( ))]
        );
        CarFile car = CarV1Reader.Read( carBytes );

        using CancellationTokenSource cts = new( );
        cts.Cancel( );

        _ = Assert.ThrowsExactly<OperationCanceledException>( ( ) => {
            _ = MstWalker.EnumerateRecords( car, cts.Token ).Records.ToList( );
        } );
    }

    private sealed class ByteArrayKeyComparer : System.Collections.Generic.IEqualityComparer<byte[]> {
        internal static readonly ByteArrayKeyComparer Instance = new( );
        public bool Equals( byte[]? x, byte[]? y ) => x is not null && y is not null && x.AsSpan( ).SequenceEqual( y );
        public int GetHashCode( byte[] obj ) {
            System.HashCode hc = new( );
            hc.AddBytes( obj );
            return hc.ToHashCode( );
        }
    }
}

#pragma warning restore CS1591
