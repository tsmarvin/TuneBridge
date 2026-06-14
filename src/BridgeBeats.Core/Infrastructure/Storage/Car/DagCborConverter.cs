using System.Formats.Cbor;
using System.Text.Json.Nodes;

namespace BridgeBeats.Core.Infrastructure.Storage.Car;

/// <summary>
/// Converts a DAG-CBOR encoded block into a <see cref="System.Text.Json.Nodes.JsonNode"/> tree,
/// following the IPLD dag-json conventions atproto records use.
/// </summary>
/// <remarks>
/// DAG-CBOR is a deterministic subset of CBOR (RFC 7049) used by atproto, in which the spec
/// requires map key ordering to be length-first, then lexicographic. This decoder uses
/// <see cref="System.Formats.Cbor.CborConformanceMode.Lax"/> and does <b>not</b> enforce
/// canonical key ordering; the ordering property describes the format spec, not an invariant
/// the decoder verifies. Scalars (integers, booleans, floats, text strings, null) map to their
/// JSON equivalents. Two cases use the dag-json sentinel-object convention so the JSON is
/// unambiguous: a CBOR byte string becomes <c>{"$bytes": "&lt;base64&gt;"}</c> and a CBOR tag-42
/// CID link becomes <c>{"$link": "&lt;base32 cid&gt;"}</c>. Any other CBOR tag is rejected.
/// Nesting is capped at depth 32 to bound work against maliciously deep input.
/// </remarks>
internal static class DagCborConverter {

    /// <summary>
    /// Maximum nesting depth for CBOR maps and arrays. atproto records are shallow, so 32 levels is
    /// generous; decoding past this throws, preventing a deeply nested or recursive structure from
    /// causing an uncatchable StackOverflow (a denial-of-service guard).
    /// </summary>
    private const int MaxDepth = 32;

    /// <summary>
    /// Decodes DAG-CBOR bytes into a JSON node tree.
    /// </summary>
    /// <param name="bytes">The DAG-CBOR encoded block bytes.</param>
    /// <returns>The decoded <see cref="JsonNode"/> tree, or <see langword="null"/> if the top-level value is CBOR null.</returns>
    /// <exception cref="CarParseException">
    /// Thrown when nesting exceeds depth 32, an unsupported CBOR tag or state is encountered, or the
    /// underlying CBOR is malformed.
    /// </exception>
    internal static JsonNode? ToJsonNode( ReadOnlyMemory<byte> bytes ) {
        try {
            CborReader reader = new( bytes, CborConformanceMode.Lax );
            return ReadValue( reader, 0 );
        } catch (CarParseException) {
            throw;
        } catch (Exception ex) when (ex is CborContentException or OverflowException or InvalidOperationException) {
            throw new CarParseException( "Failed to decode DAG-CBOR bytes.", ex );
        }
    }

    /// <summary>
    /// Reads a single CBOR value at the current reader position and dispatches by its type,
    /// recursing into maps and arrays.
    /// </summary>
    /// <param name="reader">The CBOR reader positioned at the value to read.</param>
    /// <param name="depth">The current nesting depth, checked against <see cref="MaxDepth"/>.</param>
    /// <returns>The decoded <see cref="JsonNode"/>, or <see langword="null"/> for a CBOR null value.</returns>
    /// <exception cref="CarParseException">Thrown when depth exceeds <see cref="MaxDepth"/> or the CBOR state is unsupported.</exception>
    private static JsonNode? ReadValue( CborReader reader, int depth ) {
        if (depth > MaxDepth) {
            throw new CarParseException(
                $"DAG-CBOR nesting depth exceeded maximum of {MaxDepth}; possible malicious or malformed input." );
        }

        CborReaderState state = reader.PeekState( );

        return state switch {
            CborReaderState.UnsignedInteger => JsonValue.Create( reader.ReadUInt64( ) ),
            CborReaderState.NegativeInteger => JsonValue.Create( reader.ReadInt64( ) ),
            CborReaderState.Boolean => JsonValue.Create( reader.ReadBoolean( ) ),
            CborReaderState.Null => ReadNull( reader ),
            CborReaderState.SinglePrecisionFloat => JsonValue.Create( (double)reader.ReadSingle( ) ),
            CborReaderState.DoublePrecisionFloat => JsonValue.Create( reader.ReadDouble( ) ),
            CborReaderState.HalfPrecisionFloat => JsonValue.Create( (double)reader.ReadHalf( ) ),
            CborReaderState.TextString => JsonValue.Create( reader.ReadTextString( ) ),
            CborReaderState.ByteString => ReadByteString( reader ),
            CborReaderState.StartMap => ReadMap( reader, depth ),
            CborReaderState.StartArray => ReadArray( reader, depth ),
            CborReaderState.Tag => ReadTag( reader ),
            _ => throw new CarParseException( $"Unsupported CBOR state: {state}." )
        };
    }

    /// <summary>
    /// Consumes a CBOR null and returns a JSON null.
    /// </summary>
    /// <param name="reader">The CBOR reader positioned at a null value.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    private static JsonNode? ReadNull( CborReader reader ) {
        reader.ReadNull( );
        return null;
    }

    /// <summary>
    /// Reads a CBOR byte string and wraps it in the dag-json <c>$bytes</c> sentinel object with the
    /// bytes base64-encoded.
    /// </summary>
    /// <param name="reader">The CBOR reader positioned at a byte string.</param>
    /// <returns>A <c>{"$bytes": "&lt;base64&gt;"}</c> object.</returns>
    private static JsonNode ReadByteString( CborReader reader ) {
        byte[] bytes = reader.ReadByteString( );
        return JsonValue.Create( Convert.ToBase64String( bytes ) ) is JsonNode node
            ? new JsonObject { ["$bytes"] = node }
            : new JsonObject { ["$bytes"] = JsonValue.Create( "" ) };
    }

    /// <summary>
    /// Reads a CBOR map into a <see cref="JsonObject"/>, recursing on each value at the next depth.
    /// </summary>
    /// <param name="reader">The CBOR reader positioned at the start of a map.</param>
    /// <param name="depth">The current nesting depth; child values are read at <paramref name="depth"/> + 1.</param>
    /// <returns>The decoded object.</returns>
    private static JsonNode ReadMap( CborReader reader, int depth ) {
        _ = reader.ReadStartMap( );
        JsonObject obj = [];

        while (reader.PeekState( ) != CborReaderState.EndMap) {
            string key = reader.ReadTextString( );
            JsonNode? value = ReadValue( reader, depth + 1 );
            obj[key] = value;
        }

        reader.ReadEndMap( );
        return obj;
    }

    /// <summary>
    /// Reads a CBOR array into a <see cref="JsonArray"/>, recursing on each element at the next depth.
    /// </summary>
    /// <param name="reader">The CBOR reader positioned at the start of an array.</param>
    /// <param name="depth">The current nesting depth; elements are read at <paramref name="depth"/> + 1.</param>
    /// <returns>The decoded array.</returns>
    private static JsonNode ReadArray( CborReader reader, int depth ) {
        _ = reader.ReadStartArray( );
        JsonArray arr = [];

        while (reader.PeekState( ) != CborReaderState.EndArray) {
            arr.Add( ReadValue( reader, depth + 1 ) );
        }

        reader.ReadEndArray( );
        return arr;
    }

    /// <summary>
    /// Reads a CBOR tagged value. Only tag 42 (an IPLD CID link) is supported; it must be followed
    /// by a byte string carrying the <c>0x00</c> multibase prefix and the CID, which is re-encoded
    /// to a base32 string and wrapped in the dag-json <c>$link</c> sentinel object.
    /// </summary>
    /// <param name="reader">The CBOR reader positioned at a tag.</param>
    /// <returns>A <c>{"$link": "&lt;base32 cid&gt;"}</c> object for a tag-42 CID link.</returns>
    /// <exception cref="CarParseException">
    /// Thrown for any tag other than 42, when a tag-42 value is not a byte string, or when the link
    /// bytes lack the <c>0x00</c> multibase prefix or are not a valid CID.
    /// </exception>
    private static JsonNode ReadTag( CborReader reader ) {
        CborTag tag = reader.ReadTag( );

        if (tag == (CborTag)42) {
            // Tag-42: IPLD CID link — value is a byte string with 0x00 multibase prefix + CID bytes
            if (reader.PeekState( ) != CborReaderState.ByteString) {
                throw new CarParseException( "Tag-42 CID link must be followed by a byte string." );
            }

            byte[] cidBytes = reader.ReadByteString( );
            // Strip 0x00 multibase prefix; return as {"$link": "base32multibase"}
            if (cidBytes.Length < 1 || cidBytes[0] != 0x00) {
                throw new CarParseException(
                    $"Tag-42 link byte string missing 0x00 multibase prefix (first byte: 0x{(cidBytes.Length > 0 ? cidBytes[0] : 0xFF):x2})." );
            }

            Cid cid = Cid.ParseFromBytes( cidBytes.AsSpan( 1 ) );
            return new JsonObject { ["$link"] = JsonValue.Create( cid.ToString( ) ) };
        }

        // Other tags: reject. Tag-42 reads a byte string directly (no recursive ReadValue), so depth
        // is not applicable; any other unsupported tag causes an immediate rejection.
        throw new CarParseException( $"Unsupported CBOR tag: {(ulong)tag}." );
    }
}
