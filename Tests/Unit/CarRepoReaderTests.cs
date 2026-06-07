using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Storage.Car;
using BridgeBeats.Tests.Unit.Helpers;

#pragma warning disable CS1591

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Tests for <see cref="CarRepoReader.EnumerateCollection"/>.
/// </summary>
[TestClass]
public class CarRepoReaderTests {

    /// <summary>Gets or sets the test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    private static MediaLinkResultRecord MakeRecord( string title ) =>
        new(
            results: [
                new ProviderResultRecord(
                    provider: "spotify",
                    artist: "Artist",
                    title: title,
                    url: "https://open.spotify.com/track/x",
                    marketRegion: "US"
                )
            ],
            lookedUpAt: new DateTimeOffset( 2024, 6, 1, 0, 0, 0, TimeSpan.Zero )
        );

    [TestMethod]
    public void EnumerateCollection_MixedCollections_FiltersToRequestedCollection( ) {
        // Build a repo with both lookup records and playlist records
        // They share the same CAR but are in different collections
        byte[] lookupRecord1Bytes = TestCarBuilder.BuildRecordBlock( MakeRecord( "Lookup Track 1" ) );
        byte[] lookupRecord2Bytes = TestCarBuilder.BuildRecordBlock( MakeRecord( "Lookup Track 2" ) );
        byte[] playlistRecordBytes = TestCarBuilder.BuildRecordBlock( MakeRecord( "Playlist" ) );

        byte[] lookupCid1 = TestCarBuilder.ComputeCidBytes( lookupRecord1Bytes );
        byte[] lookupCid2 = TestCarBuilder.ComputeCidBytes( lookupRecord2Bytes );
        byte[] playlistCid = TestCarBuilder.ComputeCidBytes( playlistRecordBytes );

        // MST entries for two collections
        List<TestCarBuilder.MstEntry> entries = [
            new TestCarBuilder.MstEntry( 0, "link.bridgebeats.lookup/key1", lookupCid1 ),
            new TestCarBuilder.MstEntry( 0, "link.bridgebeats.playlist/pl1", playlistCid ),
            new TestCarBuilder.MstEntry( 0, "link.bridgebeats.lookup/key2", lookupCid2 )
        ];

        // Sort entries by key (required for MST)
        entries.Sort( ( a, b ) => string.Compare( a.KeySuffix, b.KeySuffix, StringComparison.Ordinal ) );

        // Apply prefix compression
        List<TestCarBuilder.MstEntry> compressed = [];
        string prevKey = "";
        foreach (TestCarBuilder.MstEntry e in entries) {
            int prefixLen = CommonPrefixLength( prevKey, e.KeySuffix );
            compressed.Add( new TestCarBuilder.MstEntry( prefixLen, e.KeySuffix[prefixLen..], e.ValueCidBytes ) );
            prevKey = e.KeySuffix;
        }

        byte[] mstNodeBytes = TestCarBuilder.BuildMstNode( null, compressed );
        byte[] mstCid = TestCarBuilder.ComputeCidBytes( mstNodeBytes );
        byte[] commitBytes = TestCarBuilder.BuildCommit( "did:plc:test", mstCid );
        byte[] commitCid = TestCarBuilder.ComputeCidBytes( commitBytes );

        Dictionary<byte[], byte[]> blocks = new( ByteArrayKeyComparer.Instance ) {
            [commitCid] = commitBytes,
            [mstCid] = mstNodeBytes,
            [lookupCid1] = lookupRecord1Bytes,
            [lookupCid2] = lookupRecord2Bytes,
            [playlistCid] = playlistRecordBytes
        };

        byte[] carBytes = TestCarBuilder.BuildCar( commitCid, blocks );
        ReadOnlyMemory<byte> carMemory = carBytes;

        // Act: enumerate only the lookup collection
        List<(string Rkey, System.Text.Json.Nodes.JsonNode Record)> lookupResults = [
            .. CarRepoReader.EnumerateCollection( carMemory, "link.bridgebeats.lookup", TestContext.CancellationToken ).Records
        ];

        // Assert: only 2 lookup records returned, playlist filtered out
        Assert.HasCount( 2, lookupResults );
        // Both should have rkeys from the lookup collection
        Assert.IsTrue( lookupResults.All( r => !r.Rkey.Contains( "pl1" ) ) );
    }

    [TestMethod]
    public void EnumerateCollection_EmptyRepo_YieldsNothing( ) {
        byte[] carBytes = TestCarBuilder.BuildRepoCarWithRecords(
            "did:plc:test",
            "link.bridgebeats.lookup",
            []
        );

        List<(string Rkey, System.Text.Json.Nodes.JsonNode Record)> results = [
            .. CarRepoReader.EnumerateCollection( carBytes, "link.bridgebeats.lookup", TestContext.CancellationToken ).Records
        ];

        Assert.IsEmpty( results );
    }

    [TestMethod]
    public void EnumerateCollection_LookupRecords_YieldsCorrectRkeys( ) {
        byte[] carBytes = TestCarBuilder.BuildRepoCarWithRecords(
            "did:plc:test",
            "link.bridgebeats.lookup",
            [
                ("rkey_alpha", MakeRecord( "Alpha" )),
                ("rkey_beta", MakeRecord( "Beta" ))
            ]
        );

        List<(string Rkey, System.Text.Json.Nodes.JsonNode Record)> results = [
            .. CarRepoReader.EnumerateCollection( carBytes, "link.bridgebeats.lookup", TestContext.CancellationToken ).Records
        ];

        Assert.HasCount( 2, results );
        List<string> rkeys = [.. results.Select( r => r.Rkey ).OrderBy( s => s )];
        Assert.AreEqual( "rkey_alpha", rkeys[0] );
        Assert.AreEqual( "rkey_beta", rkeys[1] );
    }

    private static int CommonPrefixLength( string a, string b ) {
        int len = Math.Min( a.Length, b.Length );
        int i = 0;
        while (i < len && a[i] == b[i]) { i++; }
        return i;
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
