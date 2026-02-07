using System.Security.Cryptography;
using System.Text.Json;

namespace BridgeBeats.Core.Infrastructure.Identity;

/// <summary>
/// Provides the persistent ES256 signing key used for ATProto OAuth confidential client assertions.
/// This key is shared across all sessions and is distinct from per-session DPoP keys.
/// </summary>
public sealed class ATProtoSigningKeyProvider : IDisposable {

    private static readonly JsonSerializerOptions s_indentedJsonOptions = new( ) { WriteIndented = true };

    /// <summary>
    /// The ECDSA signing key.
    /// </summary>
    public ECDsa SigningKey { get; }

    /// <summary>
    /// The key identifier (kid) matching the published JWKS.
    /// </summary>
    public string KeyId { get; }

    /// <summary>
    /// The base64url-encoded X coordinate of the public key.
    /// </summary>
    public string PublicKeyX { get; }

    /// <summary>
    /// The base64url-encoded Y coordinate of the public key.
    /// </summary>
    public string PublicKeyY { get; }

    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ATProtoSigningKeyProvider"/> class
    /// from a JWK JSON string containing an ES256 private key.
    /// </summary>
    /// <param name="jwkJson">The JWK JSON string containing the private key (kty, crv, x, y, d, kid).</param>
    public ATProtoSigningKeyProvider( string jwkJson ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( jwkJson );

        using JsonDocument doc = JsonDocument.Parse( jwkJson );
        JsonElement jwk = doc.RootElement;

        // Validate required JWK parameters
        if (!jwk.TryGetProperty( "kty", out JsonElement ktyProp ) || ktyProp.GetString( ) != "EC") {
            throw new ArgumentException( "ATProto OAuth signing key JWK must have 'kty' set to 'EC'.", nameof( jwkJson ) );
        }
        if (!jwk.TryGetProperty( "crv", out JsonElement crvProp ) || crvProp.GetString( ) != "P-256") {
            throw new ArgumentException( "ATProto OAuth signing key JWK must have 'crv' set to 'P-256'.", nameof( jwkJson ) );
        }
        if (!jwk.TryGetProperty( "x", out JsonElement xProp ) || string.IsNullOrWhiteSpace( xProp.GetString( ) )) {
            throw new ArgumentException( "ATProto OAuth signing key JWK does not contain required parameter 'x'.", nameof( jwkJson ) );
        }
        if (!jwk.TryGetProperty( "y", out JsonElement yProp ) || string.IsNullOrWhiteSpace( yProp.GetString( ) )) {
            throw new ArgumentException( "ATProto OAuth signing key JWK does not contain required parameter 'y'.", nameof( jwkJson ) );
        }
        if (!jwk.TryGetProperty( "d", out JsonElement dProp ) || string.IsNullOrWhiteSpace( dProp.GetString( ) )) {
            throw new ArgumentException( "ATProto OAuth signing key JWK does not contain required private key parameter 'd'.", nameof( jwkJson ) );
        }
        if (!jwk.TryGetProperty( "kid", out JsonElement kidProp ) || string.IsNullOrWhiteSpace( kidProp.GetString( ) )) {
            throw new ArgumentException( "ATProto OAuth signing key JWK does not contain required parameter 'kid'.", nameof( jwkJson ) );
        }

        string x = xProp.GetString( )!;
        string y = yProp.GetString( )!;
        string d = dProp.GetString( )!;

        PublicKeyX = x;
        PublicKeyY = y;
        KeyId = kidProp.GetString( )!;

        // Create the ECDsa key from JWK parameters
        byte[] xBytes = Base64UrlDecode( x );
        byte[] yBytes = Base64UrlDecode( y );
        byte[] dBytes = Base64UrlDecode( d );

        SigningKey = ECDsa.Create( new ECParameters {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = xBytes, Y = yBytes },
            D = dBytes
        } );
    }

    /// <summary>
    /// Returns the JWKS JSON containing only the public key for publishing at the jwks_uri endpoint.
    /// </summary>
    /// <returns>A JSON string in JWKS format (keys array with a single public key).</returns>
    public string GetPublicJwks( ) {
        var jwks = new {
            keys = new[] {
                new {
                    kty = "EC",
                    crv = "P-256",
                    x = PublicKeyX,
                    y = PublicKeyY,
                    kid = KeyId,
                    alg = "ES256",
                    use = "sig"
                }
            }
        };

        return JsonSerializer.Serialize( jwks, s_indentedJsonOptions );
    }

    /// <summary>
    /// Generates a new ES256 signing key in JWK format suitable for persisting to a secret file.
    /// Call this once during installation to create the key, then load it via the constructor.
    /// </summary>
    /// <returns>A JWK JSON string containing the full key (including private key 'd' parameter).</returns>
    public static string GenerateNewSigningKeyJwk( ) {
        using ECDsa ecdsa = ECDsa.Create( ECCurve.NamedCurves.nistP256 );
        ECParameters parameters = ecdsa.ExportParameters( includePrivateParameters: true );

        var jwk = new {
            kty = "EC",
            crv = "P-256",
            x = Base64UrlEncode( parameters.Q.X! ),
            y = Base64UrlEncode( parameters.Q.Y! ),
            d = Base64UrlEncode( parameters.D! ),
            kid = Guid.NewGuid( ).ToString( "N" ),
            alg = "ES256",
            use = "sig"
        };

        return JsonSerializer.Serialize( jwk );
    }

    /// <inheritdoc/>
    public void Dispose( ) {
        if (!_disposed) {
            SigningKey.Dispose( );
            _disposed = true;
        }
    }

    /// <summary>
    /// Base64 URL decodes a string.
    /// </summary>
    private static byte[] Base64UrlDecode( string base64Url ) {
        string padded = base64Url.PadRight( base64Url.Length + ((4 - (base64Url.Length % 4)) % 4), '=' );
        string base64 = padded.Replace( '-', '+' ).Replace( '_', '/' );
        return Convert.FromBase64String( base64 );
    }

    /// <summary>
    /// Base64 URL encodes the given bytes.
    /// </summary>
    private static string Base64UrlEncode( byte[] bytes ) {
        return Convert.ToBase64String( bytes )
            .TrimEnd( '=' )
            .Replace( '+', '-' )
            .Replace( '/', '_' );
    }
}
