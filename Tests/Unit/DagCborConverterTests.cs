using System.Formats.Cbor;
using System.Text.Json;
using System.Text.Json.Nodes;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Storage.Car;
using BridgeBeats.Tests.Unit.Helpers;

#pragma warning disable CS1591

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Tests <see cref="DagCborConverter"/>, which converts DAG-CBOR blocks to <see cref="JsonNode"/> using the
/// atproto/IPLD dag-json sentinel conventions. Covers record round-trips, the <c>$bytes</c> and <c>$link</c>
/// sentinels, nested structures, null handling, rejection of unsupported CBOR tags, and the 32-level maximum
/// nesting-depth guard.
/// </summary>
[TestClass]
public class DagCborConverterTests {

    /// <summary>
    /// Verifies a CBOR-encoded record block converts to JSON that deserializes back into an equivalent
    /// <see cref="MediaLinkResultRecord"/>, preserving every provider-result field and the lookup timestamp.
    /// </summary>
    [TestMethod]
    public void ToJsonNode_RecordBlock_DeserializesToMediaLinkResultRecord( ) {
        // Arrange: build a real DAG-CBOR record block using TestCarBuilder
        MediaLinkResultRecord original = new(
            results: [
                new ProviderResultRecord(
                    provider: "spotify",
                    artist: "Test Artist",
                    title: "Test Track",
                    url: "https://open.spotify.com/track/abc123",
                    marketRegion: "US",
                    externalId: "ISRC123456789",
                    artUrl: null,
                    isAlbum: false
                )
            ],
            lookedUpAt: new DateTimeOffset( 2024, 3, 15, 12, 0, 0, TimeSpan.Zero )
        );
        byte[] cborBytes = TestCarBuilder.BuildRecordBlock( original );

        // Act: convert to JsonNode then deserialize
        JsonNode? node = DagCborConverter.ToJsonNode( cborBytes );
        Assert.IsNotNull( node );

        // Strip $type if present (atproto may include it; System.Text.Json ignores unknown props by default)
        if (node is JsonObject obj && obj.ContainsKey( "$type" )) {
            _ = obj.Remove( "$type" );
        }

        string json = node.ToJsonString( );
        MediaLinkResultRecord? deserialized = JsonSerializer.Deserialize<MediaLinkResultRecord>( json );

        // Assert: round-trips correctly
        Assert.IsNotNull( deserialized );
        Assert.HasCount( 1, deserialized.Results );
        ProviderResultRecord firstResult = deserialized.Results.First( );
        Assert.AreEqual( "spotify", firstResult.Provider );
        Assert.AreEqual( "Test Artist", firstResult.Artist );
        Assert.AreEqual( "Test Track", firstResult.Title );
        Assert.AreEqual( "https://open.spotify.com/track/abc123", firstResult.Url );
        Assert.AreEqual( "US", firstResult.MarketRegion );
        Assert.AreEqual( "ISRC123456789", firstResult.ExternalId );
        Assert.IsFalse( firstResult.IsAlbum );
        Assert.AreEqual(
            new DateTimeOffset( 2024, 3, 15, 12, 0, 0, TimeSpan.Zero ),
            deserialized.LookedUpAt );
    }

    /// <summary>
    /// Verifies an ISO-8601 timestamp stored as a CBOR text string round-trips to the identical JSON string value.
    /// </summary>
    [TestMethod]
    public void ToJsonNode_IsoDateString_RoundTripsAsString( ) {
        // Arrange
        CborWriter w = new( CborConformanceMode.Lax );
        w.WriteStartMap( null );
        w.WriteTextString( "ts" );
        w.WriteTextString( "2024-03-15T12:00:00.0000000+00:00" );
        w.WriteEndMap( );
        byte[] bytes = w.Encode( );

        // Act
        JsonNode? node = DagCborConverter.ToJsonNode( bytes );

        // Assert
        Assert.IsNotNull( node );
        Assert.AreEqual( "2024-03-15T12:00:00.0000000+00:00", node["ts"]?.GetValue<string>( ) );
    }

    /// <summary>
    /// Verifies a CBOR tag-42 CID link converts to a <c>{"$link": "&lt;base32 cid&gt;"}</c> sentinel object.
    /// </summary>
    [TestMethod]
    public void ToJsonNode_Tag42Link_ReturnsDollarLinkObject( ) {
        // Arrange: a map with a tag-42 CID link
        byte[] recordBytes = System.Text.Encoding.UTF8.GetBytes( "dummy block" );
        byte[] cidBytes = TestCarBuilder.ComputeCidBytes( recordBytes );
        byte[] linkBytes = TestCarBuilder.MakeLinkBytes( cidBytes );

        CborWriter w = new( CborConformanceMode.Lax );
        w.WriteStartMap( null );
        w.WriteTextString( "data" );
        w.WriteTag( (CborTag)42 );
        w.WriteByteString( linkBytes );
        w.WriteEndMap( );
        byte[] bytes = w.Encode( );

        // Act
        JsonNode? node = DagCborConverter.ToJsonNode( bytes );

        // Assert
        Assert.IsNotNull( node );
        JsonNode? dataNode = node["data"];
        Assert.IsNotNull( dataNode );
        Assert.IsNotNull( dataNode["$link"] );
    }

    /// <summary>
    /// Verifies a CBOR byte string converts to a <c>{"$bytes": "&lt;base64&gt;"}</c> sentinel object carrying the
    /// base64 encoding of the bytes.
    /// </summary>
    [TestMethod]
    public void ToJsonNode_ByteString_ReturnsDollarBytesObject( ) {
        // Arrange
        CborWriter w = new( CborConformanceMode.Lax );
        w.WriteByteString( new byte[] { 0xDE, 0xAD, 0xBE, 0xEF } );
        byte[] bytes = w.Encode( );

        // Act
        JsonNode? node = DagCborConverter.ToJsonNode( bytes );

        // Assert
        Assert.IsNotNull( node );
        Assert.IsNotNull( node["$bytes"] );
        Assert.AreEqual( Convert.ToBase64String( new byte[] { 0xDE, 0xAD, 0xBE, 0xEF } ), node["$bytes"]?.GetValue<string>( ) );
    }

    /// <summary>
    /// Verifies a nested map-of-array containing a string, an unsigned integer, and a boolean converts to the
    /// matching JSON array, preserving element types and order.
    /// </summary>
    [TestMethod]
    public void ToJsonNode_NestedStructures_RoundTrips( ) {
        // Arrange: nested array inside map
        CborWriter w = new( CborConformanceMode.Lax );
        w.WriteStartMap( null );
        w.WriteTextString( "items" );
        w.WriteStartArray( null );
        w.WriteTextString( "alpha" );
        w.WriteUInt64( 42 );
        w.WriteBoolean( true );
        w.WriteEndArray( );
        w.WriteEndMap( );
        byte[] bytes = w.Encode( );

        // Act
        JsonNode? node = DagCborConverter.ToJsonNode( bytes );

        // Assert
        Assert.IsNotNull( node );
        JsonArray? arr = node["items"] as JsonArray;
        Assert.IsNotNull( arr );
        Assert.AreEqual( 3, arr.Count );
        Assert.AreEqual( "alpha", arr[0]?.GetValue<string>( ) );
        Assert.AreEqual( 42UL, arr[1]?.GetValue<ulong>( ) );
        Assert.IsTrue( arr[2]?.GetValue<bool>( ) );
    }

    /// <summary>
    /// Verifies that any CBOR tag other than the supported tag 42 (a CID link) is rejected with a
    /// <see cref="CarParseException"/>.
    /// </summary>
    [TestMethod]
    public void ToJsonNode_UnsupportedTag_ThrowsCarParseException( ) {
        // Arrange
        CborWriter w = new( CborConformanceMode.Lax );
        w.WriteTag( (CborTag)1 ); // epoch-based datetime — not supported
        w.WriteInt64( 1000000 );
        byte[] bytes = w.Encode( );

        // Act & Assert
        _ = Assert.ThrowsExactly<CarParseException>( ( ) => DagCborConverter.ToJsonNode( bytes ) );
    }

    /// <summary>
    /// Verifies a CBOR null converts to a null <see cref="JsonNode"/>.
    /// </summary>
    [TestMethod]
    public void ToJsonNode_NullValue_ReturnsNull( ) {
        // Arrange
        CborWriter w = new( CborConformanceMode.Lax );
        w.WriteNull( );
        byte[] bytes = w.Encode( );

        // Act
        JsonNode? node = DagCborConverter.ToJsonNode( bytes );

        // Assert
        Assert.IsNull( node );
    }

    // ─── SEC-002 regression: depth guard converts deep nesting to CarParseException ──

    /// <summary>
    /// Verifies that CBOR nested beyond the converter's 32-level maximum depth (here 33 levels) is rejected with a
    /// <see cref="CarParseException"/>, enforcing the anti-DoS depth cap. Without the depth check this would
    /// StackOverflow and crash the test host rather than surfacing a catchable exception.
    /// </summary>
    [TestMethod]
    public void ToJsonNode_DeeplyNestedCbor_ThrowsCarParseException( ) {
        // 33 levels of nested single-element arrays exceeds MaxDepth=32.
        byte[] deepBytes = TestCarBuilder.BuildDeeplyNestedCborBytes( 33 );

        _ = Assert.ThrowsExactly<CarParseException>( ( ) => DagCborConverter.ToJsonNode( deepBytes ) );
    }

    /// <summary>
    /// Verifies that CBOR nested at exactly the 32-level maximum depth is accepted (the boundary case at the cap
    /// does not throw).
    /// </summary>
    [TestMethod]
    public void ToJsonNode_NestingAtMaxDepth_DoesNotThrow( ) {
        // 32 levels of nesting is exactly at the limit — should succeed.
        byte[] deepBytes = TestCarBuilder.BuildDeeplyNestedCborBytes( 32 );

        // Should not throw
        JsonNode? node = DagCborConverter.ToJsonNode( deepBytes );
        Assert.IsNotNull( node ); // outermost array
    }
}

#pragma warning restore CS1591
