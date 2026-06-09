using System.Formats.Cbor;
using System.Text.Json.Nodes;

namespace BridgeBeats.Core.Infrastructure.Storage.Car;

/// <summary>
/// Converts DAG-CBOR encoded bytes into a <see cref="JsonNode"/> tree.
/// </summary>
/// <remarks>
/// DAG-CBOR is a deterministic subset of CBOR (RFC 7049) used by atproto.
/// Tag 42 encodes CID links; bare byte strings encode binary data.
/// Key ordering in maps is length-first, then lexicographic (deterministic CBOR).
/// </remarks>
internal static class DagCborConverter {

    /// <summary>
    /// Maximum nesting depth for CBOR maps and arrays.
    /// atproto records are shallow; 32 levels is generous and prevents
    /// unbounded recursion that would cause an uncatchable StackOverflow.
    /// </summary>
    private const int MaxDepth = 32;

    /// <summary>
    /// Converts DAG-CBOR bytes to a <see cref="JsonNode"/> representation.
    /// Returns null for a CBOR null value.
    /// Throws <see cref="CarParseException"/> for malformed or unsupported CBOR.
    /// </summary>
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

    private static JsonNode? ReadNull( CborReader reader ) {
        reader.ReadNull( );
        return null;
    }

    private static JsonNode ReadByteString( CborReader reader ) {
        byte[] bytes = reader.ReadByteString( );
        return JsonValue.Create( Convert.ToBase64String( bytes ) ) is JsonNode node
            ? new JsonObject { ["$bytes"] = node }
            : new JsonObject { ["$bytes"] = JsonValue.Create( "" ) };
    }

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

    private static JsonNode ReadArray( CborReader reader, int depth ) {
        _ = reader.ReadStartArray( );
        JsonArray arr = [];

        while (reader.PeekState( ) != CborReaderState.EndArray) {
            arr.Add( ReadValue( reader, depth + 1 ) );
        }

        reader.ReadEndArray( );
        return arr;
    }

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

        // Other tags: reject.
        // Tag-42 reads a byte string directly (no recursive ReadValue), so depth is not applicable.
        // Any other unsupported tag causes an immediate rejection.
        throw new CarParseException( $"Unsupported CBOR tag: {(ulong)tag}." );
    }
}
