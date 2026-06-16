using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Storage.Car;
using BridgeBeats.Tests.Unit.Helpers;

#pragma warning disable CS1591

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Tests <see cref="MstWalker"/>, which walks the Merkle Search Tree of an ATProto repo export to enumerate
/// records in key order. Covers empty/single/multi-record enumeration, key ordering, prefix-compression
/// reconstruction, multi-node in-order traversal, the reported commit version, and the parser's integrity and
/// anti-DoS defenses: missing value blocks, invalid/negative prefix lengths, the shared-node ("doubling") DAG
/// guard, null commit-data links, the max-depth cap, the record-count-versus-block-count cap, and cooperative
/// cancellation.
/// </summary>
[TestClass]
public class MstWalkerTests {
    /// <summary>MSTest-injected test context, used to source the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Builds a minimal single-provider <see cref="MediaLinkResultRecord"/> for use as an MST value block.
    /// </summary>
    /// <param name="artist">The provider-result artist.</param>
    /// <param name="title">The provider-result title.</param>
    /// <returns>A populated record.</returns>
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

    /// <summary>
    /// Walks the whole CAR and materializes the (key, value-CID-hex) pairs the walker yields.
    /// </summary>
    /// <param name="car">The parsed CAR file to walk.</param>
    /// <returns>The enumerated key/value-CID pairs.</returns>
    private List<(string Key, string ValueCidHex)> EnumerateAll( CarFile car ) =>
        [.. MstWalker.EnumerateRecords( car, TestContext.CancellationToken ).Records];

    /// <summary>
    /// Verifies an empty repo (no MST entries) yields no records.
    /// </summary>
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

    /// <summary>
    /// Verifies a single-record repo yields one key, the full <c>{collection}/{rkey}</c>.
    /// </summary>
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

    /// <summary>
    /// Verifies records out of input order are yielded in ascending key order (the MST is key-sorted).
    /// </summary>
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

    /// <summary>
    /// Verifies keys sharing a long common prefix are reconstructed correctly from the MST prefix-compression
    /// (<c>p</c> shared length + <c>k</c> suffix).
    /// </summary>
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

    /// <summary>
    /// Verifies a hand-built three-node tree (root with a left subtree and an entry's right subtree) is traversed
    /// in order, yielding left, root entry, then right.
    /// </summary>
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

    /// <summary>
    /// Verifies that an MST entry pointing at a value CID with no corresponding block (a dangling reference) causes
    /// collection enumeration to throw a <see cref="CarParseException"/>.
    /// </summary>
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

    /// <summary>
    /// Verifies that a prefix length longer than the previous key (here 100, overflowing a short key) is rejected
    /// with a <see cref="CarParseException"/>.
    /// </summary>
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

    /// <summary>
    /// Verifies the walker surfaces the actual commit version (here 4) alongside the enumerated records rather than
    /// assuming version 3.
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

    /// <summary>
    /// Verifies the cycle/visited-set defense: a CAR whose MST reaches the same node by two paths is rejected with a
    /// <see cref="CarParseException"/> rather than walked twice (the 5-second timeout guards against runaway walks).
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

    /// <summary>
    /// Verifies a negative prefix length is rejected with a <see cref="CarParseException"/>.
    /// </summary>
    [TestMethod]
    public void EnumerateRecords_NegativePrefixLen_ThrowsCarParseException( ) {
        byte[] carBytes = TestCarBuilder.BuildCarWithNegativePrefixLen( -1 );
        CarFile car = CarV1Reader.Read( carBytes );

        _ = Assert.ThrowsExactly<CarParseException>( ( ) => {
            _ = MstWalker.EnumerateRecords( car, CancellationToken.None ).Records.ToList( );
        } );
    }

    /// <summary>
    /// Verifies a commit whose <c>data</c> link is null (no MST root) is rejected with a <see cref="CarParseException"/>.
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

    /// <summary>
    /// Verifies a left-subtree chain longer than the walker's max depth (66 nodes, above the depth-64 cap) is
    /// rejected with a <see cref="CarParseException"/> rather than recursing without bound.
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
    /// Verifies the record-count cap: an MST claiming more entries than the CAR has blocks (here three entries sharing
    /// one absent value block) is rejected with a <see cref="CarParseException"/>, bounding an amplification attack.
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

    /// <summary>
    /// Verifies the walk honors a pre-cancelled token by throwing an <see cref="OperationCanceledException"/>.
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

    /// <summary>
    /// Structural equality comparer for <c>byte[]</c> CID keys, so the block dictionaries are keyed by content rather
    /// than array reference.
    /// </summary>
    private sealed class ByteArrayKeyComparer : System.Collections.Generic.IEqualityComparer<byte[]> {
        /// <summary>Shared singleton instance.</summary>
        internal static readonly ByteArrayKeyComparer Instance = new( );
        /// <summary>Returns true when both arrays are non-null and have identical contents.</summary>
        /// <param name="x">The first array.</param>
        /// <param name="y">The second array.</param>
        /// <returns><see langword="true"/> if the arrays are element-wise equal; otherwise <see langword="false"/>.</returns>
        public bool Equals( byte[]? x, byte[]? y ) => x is not null && y is not null && x.AsSpan( ).SequenceEqual( y );
        /// <summary>Computes a content-based hash code over the array's bytes.</summary>
        /// <param name="obj">The array to hash.</param>
        /// <returns>A hash code derived from the array contents.</returns>
        public int GetHashCode( byte[] obj ) {
            System.HashCode hc = new( );
            hc.AddBytes( obj );
            return hc.ToHashCode( );
        }
    }
}

#pragma warning restore CS1591
