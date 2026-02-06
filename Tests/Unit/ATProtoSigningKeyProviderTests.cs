using System.Security.Cryptography;
using System.Text.Json;
using BridgeBeats.Core.Infrastructure.Identity;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for ATProtoSigningKeyProvider to verify ES256 JWK loading,
/// public JWKS generation, and key generation utility.
/// </summary>
[TestClass]
public class ATProtoSigningKeyProviderTests {

    /// <summary>
    /// Helper to generate a valid ES256 JWK JSON string for testing.
    /// </summary>
    private static string CreateTestSigningKeyJwk( ) {
        return ATProtoSigningKeyProvider.GenerateNewSigningKeyJwk( );
    }

    /// <summary>
    /// Verifies that the constructor successfully loads a valid ES256 JWK.
    /// </summary>
    [TestMethod]
    public void Constructor_WithValidJwk_ShouldCreateInstance( ) {
        // Arrange
        string jwk = CreateTestSigningKeyJwk( );

        // Act
        using ATProtoSigningKeyProvider provider = new( jwk );

        // Assert
        Assert.IsNotNull( provider.SigningKey );
        Assert.IsNotNull( provider.KeyId );
        Assert.IsNotNull( provider.PublicKeyX );
        Assert.IsNotNull( provider.PublicKeyY );
    }

    /// <summary>
    /// Verifies that the constructor throws for null input.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullJwk_ShouldThrow( ) {
        Assert.ThrowsExactly<ArgumentNullException>( ( ) => new ATProtoSigningKeyProvider( null! ) );
    }

    /// <summary>
    /// Verifies that the constructor throws for empty input.
    /// </summary>
    [TestMethod]
    public void Constructor_WithEmptyJwk_ShouldThrow( ) {
        _ = Assert.ThrowsExactly<ArgumentException>( ( ) => new ATProtoSigningKeyProvider( "" ) );
    }

    /// <summary>
    /// Verifies that the constructor throws for JWK with wrong key type.
    /// </summary>
    [TestMethod]
    public void Constructor_WithWrongKeyType_ShouldThrow( ) {
        string invalidJwk = """{"kty":"RSA","crv":"P-256","x":"abc","y":"def","d":"ghi","kid":"test"}""";
        _ = Assert.ThrowsExactly<ArgumentException>( ( ) => new ATProtoSigningKeyProvider( invalidJwk ) );
    }

    /// <summary>
    /// Verifies that the constructor throws for JWK with wrong curve.
    /// </summary>
    [TestMethod]
    public void Constructor_WithWrongCurve_ShouldThrow( ) {
        string invalidJwk = """{"kty":"EC","crv":"P-384","x":"abc","y":"def","d":"ghi","kid":"test"}""";
        _ = Assert.ThrowsExactly<ArgumentException>( ( ) => new ATProtoSigningKeyProvider( invalidJwk ) );
    }

    /// <summary>
    /// Verifies that the constructor throws for JWK missing private key.
    /// </summary>
    [TestMethod]
    public void Constructor_WithMissingPrivateKey_ShouldThrow( ) {
        string invalidJwk = """{"kty":"EC","crv":"P-256","x":"abc","y":"def","kid":"test"}""";
        _ = Assert.ThrowsExactly<ArgumentException>( ( ) => new ATProtoSigningKeyProvider( invalidJwk ) );
    }

    /// <summary>
    /// Verifies that the constructor throws for JWK missing kid.
    /// </summary>
    [TestMethod]
    public void Constructor_WithMissingKid_ShouldThrow( ) {
        string invalidJwk = """{"kty":"EC","crv":"P-256","x":"abc","y":"def","d":"ghi"}""";
        _ = Assert.ThrowsExactly<ArgumentException>( ( ) => new ATProtoSigningKeyProvider( invalidJwk ) );
    }

    /// <summary>
    /// Verifies that GetPublicJwks returns valid JSON with only the public key (no 'd' parameter).
    /// </summary>
    [TestMethod]
    public void GetPublicJwks_ShouldReturnValidJson_WithoutPrivateKey( ) {
        // Arrange
        string jwk = CreateTestSigningKeyJwk( );
        using ATProtoSigningKeyProvider provider = new( jwk );

        // Act
        string jwksJson = provider.GetPublicJwks( );

        // Assert
        Assert.IsNotNull( jwksJson );

        using JsonDocument doc = JsonDocument.Parse( jwksJson );
        JsonElement root = doc.RootElement;

        // Verify it has the 'keys' array
        Assert.IsTrue( root.TryGetProperty( "keys", out JsonElement keysArray ) );
        Assert.AreEqual( JsonValueKind.Array, keysArray.ValueKind );
        Assert.AreEqual( 1, keysArray.GetArrayLength( ) );

        // Verify the key properties
        JsonElement key = keysArray[0];
        Assert.AreEqual( "EC", key.GetProperty( "kty" ).GetString( ) );
        Assert.AreEqual( "P-256", key.GetProperty( "crv" ).GetString( ) );
        Assert.AreEqual( "ES256", key.GetProperty( "alg" ).GetString( ) );
        Assert.AreEqual( "sig", key.GetProperty( "use" ).GetString( ) );
        Assert.IsNotNull( key.GetProperty( "x" ).GetString( ) );
        Assert.IsNotNull( key.GetProperty( "y" ).GetString( ) );
        Assert.IsNotNull( key.GetProperty( "kid" ).GetString( ) );

        // CRITICAL: Verify no private key 'd' parameter is present
        Assert.IsFalse( key.TryGetProperty( "d", out _ ), "Public JWKS must not contain private key parameter 'd'" );
    }

    /// <summary>
    /// Verifies that the kid in public JWKS matches the provider's KeyId.
    /// </summary>
    [TestMethod]
    public void GetPublicJwks_Kid_ShouldMatchProviderKeyId( ) {
        // Arrange
        string jwk = CreateTestSigningKeyJwk( );
        using ATProtoSigningKeyProvider provider = new( jwk );

        // Act
        string jwksJson = provider.GetPublicJwks( );

        // Assert
        using JsonDocument doc = JsonDocument.Parse( jwksJson );
        string? kid = doc.RootElement.GetProperty( "keys" )[0].GetProperty( "kid" ).GetString( );
        Assert.AreEqual( provider.KeyId, kid );
    }

    /// <summary>
    /// Verifies that GenerateNewSigningKeyJwk produces a valid JWK that can be loaded.
    /// </summary>
    [TestMethod]
    public void GenerateNewSigningKeyJwk_ShouldProduceLoadableKey( ) {
        // Act
        string jwk = ATProtoSigningKeyProvider.GenerateNewSigningKeyJwk( );

        // Assert - should be loadable
        using ATProtoSigningKeyProvider provider = new( jwk );
        Assert.IsNotNull( provider.SigningKey );
        Assert.IsNotNull( provider.KeyId );
    }

    /// <summary>
    /// Verifies that each call to GenerateNewSigningKeyJwk produces a unique key.
    /// </summary>
    [TestMethod]
    public void GenerateNewSigningKeyJwk_ShouldProduceUniqueKeys( ) {
        // Act
        string jwk1 = ATProtoSigningKeyProvider.GenerateNewSigningKeyJwk( );
        string jwk2 = ATProtoSigningKeyProvider.GenerateNewSigningKeyJwk( );

        // Assert
        Assert.AreNotEqual( jwk1, jwk2 );
    }

    /// <summary>
    /// Verifies that the signing key can actually produce valid ES256 signatures.
    /// </summary>
    [TestMethod]
    public void SigningKey_ShouldBeAbleToSign( ) {
        // Arrange
        string jwk = CreateTestSigningKeyJwk( );
        using ATProtoSigningKeyProvider provider = new( jwk );
        byte[] data = System.Text.Encoding.UTF8.GetBytes( "test data to sign" );

        // Act
        byte[] signature = provider.SigningKey.SignData( data, HashAlgorithmName.SHA256 );

        // Assert
        Assert.IsNotNull( signature );
        Assert.IsTrue( signature.Length > 0 );

        // Verify the signature is valid
        bool isValid = provider.SigningKey.VerifyData( data, signature, HashAlgorithmName.SHA256 );
        Assert.IsTrue( isValid );
    }

    /// <summary>
    /// Verifies that Dispose properly disposes the signing key.
    /// </summary>
    [TestMethod]
    public void Dispose_ShouldDisposeSigningKey( ) {
        // Arrange
        string jwk = CreateTestSigningKeyJwk( );
        ATProtoSigningKeyProvider provider = new( jwk );
        ECDsa key = provider.SigningKey;

        // Act
        provider.Dispose( );

        // Assert - second dispose should not throw
        provider.Dispose( );
    }
}
