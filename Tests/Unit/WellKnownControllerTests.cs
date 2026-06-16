using System.Text.Json;
using BridgeBeats.Core.Infrastructure.Identity;
using BridgeBeats.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="WellKnownController"/>, which serves the AT Protocol OAuth discovery
/// documents at <c>/.well-known/client-metadata.json</c> and <c>/.well-known/jwks.json</c>. Covers
/// the confidential-client shape emitted when a signing key is present, the public-client shape
/// emitted when it is absent, the not-found responses when the configured domain or signing key is
/// missing, dynamic-domain substitution into every emitted URI, and the JWKS guarantee that the
/// private key component is never exposed.
/// </summary>
[TestClass]
public class WellKnownControllerTests {
    /// <summary>Domain used to build the configuration under test and to assert emitted absolute URIs.</summary>
    private const string TestDomain = "https://example.com";

    /// <summary>
    /// Builds an in-memory <see cref="IConfiguration"/> with <c>BridgeBeats:Domain</c> set to
    /// <paramref name="domain"/> (or absent when null), the single setting the controller reads.
    /// </summary>
    /// <param name="domain">The domain value to seed, or null to omit the key entirely.</param>
    /// <returns>A configuration exposing only the <c>BridgeBeats:Domain</c> setting.</returns>
    private static IConfiguration CreateConfiguration( string? domain ) {
        Dictionary<string, string?> settings = new( ) {
            ["BridgeBeats:Domain"] = domain
        };

        return new ConfigurationBuilder( )
            .AddInMemoryCollection( settings )
            .Build( );
    }

    /// <summary>
    /// Creates an <see cref="ATProtoSigningKeyProvider"/> backed by a freshly generated ES256 JWK,
    /// giving the controller a key so it produces confidential-client metadata and a JWKS response.
    /// </summary>
    /// <returns>A signing-key provider holding a new private key.</returns>
    private static ATProtoSigningKeyProvider CreateTestSigningKeyProvider( ) {
        string jwk = ATProtoSigningKeyProvider.GenerateNewSigningKeyJwk( );
        return new ATProtoSigningKeyProvider( jwk );
    }

    /// <summary>
    /// Verifies that when a signing key is supplied, the client-metadata document describes a
    /// confidential client: <c>private_key_jwt</c> auth method with an <c>ES256</c> signing
    /// algorithm, a <c>jwks_uri</c>, DPoP-bound access tokens, the single callback redirect URI,
    /// and the expected client id, name, and scope.
    /// </summary>
    [TestMethod]
    public void ClientMetadata_WithSigningKey_ShouldReturnConfidentialClientMetadata( ) {
        // Arrange
        IConfiguration config = CreateConfiguration(TestDomain);
        using ATProtoSigningKeyProvider signingKey = CreateTestSigningKeyProvider( );
        WellKnownController controller = new( config, signingKey );

        // Act
        IActionResult result = controller.ClientMetadata( );

        // Assert
        JsonResult jsonResult = (JsonResult)result;
        Assert.IsNotNull( jsonResult.Value );

        string json = JsonSerializer.Serialize( jsonResult.Value );
        using JsonDocument doc = JsonDocument.Parse( json );
        JsonElement root = doc.RootElement;

        Assert.AreEqual( $"{TestDomain}/.well-known/client-metadata.json", root.GetProperty( "client_id" ).GetString( ) );
        Assert.AreEqual( "BridgeBeats", root.GetProperty( "client_name" ).GetString( ) );
        Assert.AreEqual( "private_key_jwt", root.GetProperty( "token_endpoint_auth_method" ).GetString( ) );
        Assert.AreEqual( "ES256", root.GetProperty( "token_endpoint_auth_signing_alg" ).GetString( ) );
        Assert.AreEqual( $"{TestDomain}/.well-known/jwks.json", root.GetProperty( "jwks_uri" ).GetString( ) );
        Assert.IsTrue( root.GetProperty( "dpop_bound_access_tokens" ).GetBoolean( ) );

        // Verify redirect_uris
        JsonElement redirectUris = root.GetProperty( "redirect_uris" );
        Assert.AreEqual( 1, redirectUris.GetArrayLength( ) );
        Assert.AreEqual( $"{TestDomain}/account/atproto-callback", redirectUris[0].GetString( ) );

        // Verify scope
        Assert.AreEqual( "atproto repo:link.bridgebeats.playlist", root.GetProperty( "scope" ).GetString( ) );
    }

    /// <summary>
    /// Verifies that when no signing key is supplied, the client-metadata document describes a
    /// public client: the auth method is <c>none</c> and neither <c>token_endpoint_auth_signing_alg</c>
    /// nor <c>jwks_uri</c> is present.
    /// </summary>
    [TestMethod]
    public void ClientMetadata_WithoutSigningKey_ShouldReturnPublicClientMetadata( ) {
        // Arrange
        IConfiguration config = CreateConfiguration(TestDomain);
        WellKnownController controller = new( config );

        // Act
        IActionResult result = controller.ClientMetadata( );

        // Assert
        JsonResult jsonResult = (JsonResult)result;
        Assert.IsNotNull( jsonResult.Value );

        string json = JsonSerializer.Serialize( jsonResult.Value );
        using JsonDocument doc = JsonDocument.Parse( json );
        JsonElement root = doc.RootElement;

        Assert.AreEqual( "none", root.GetProperty( "token_endpoint_auth_method" ).GetString( ) );
        Assert.IsFalse( root.TryGetProperty( "token_endpoint_auth_signing_alg", out _ ) );
        Assert.IsFalse( root.TryGetProperty( "jwks_uri", out _ ) );
    }

    /// <summary>
    /// Verifies that when <c>BridgeBeats:Domain</c> is not configured, the client-metadata endpoint
    /// returns a not-found result rather than emitting a document with an empty or invalid base URI.
    /// </summary>
    [TestMethod]
    public void ClientMetadata_WithoutDomain_ShouldReturnNotFound( ) {
        // Arrange
        IConfiguration config = CreateConfiguration( null );
        WellKnownController controller = new( config );

        // Act
        IActionResult result = controller.ClientMetadata( );

        // Assert
        _ = Assert.IsInstanceOfType<NotFoundObjectResult>( result );
    }

    /// <summary>
    /// Verifies that the configured domain is substituted into every emitted URI: the
    /// <c>client_id</c>, <c>client_uri</c>, and redirect URI all derive from the custom domain
    /// rather than from a hard-coded host.
    /// </summary>
    [TestMethod]
    public void ClientMetadata_ShouldUseDynamicDomain( ) {
        // Arrange
        string customUrl = "https://custom.example.org";
        IConfiguration config = CreateConfiguration( customUrl );
        using ATProtoSigningKeyProvider signingKey = CreateTestSigningKeyProvider( );
        WellKnownController controller = new( config, signingKey );

        // Act
        IActionResult result = controller.ClientMetadata( );

        // Assert
        JsonResult jsonResult = (JsonResult)result;
        string json = JsonSerializer.Serialize( jsonResult.Value! );
        using JsonDocument doc = JsonDocument.Parse( json );
        JsonElement root = doc.RootElement;

        Assert.AreEqual( $"{customUrl}/.well-known/client-metadata.json", root.GetProperty( "client_id" ).GetString( ) );
        Assert.AreEqual( customUrl, root.GetProperty( "client_uri" ).GetString( ) );
        Assert.AreEqual( $"{customUrl}/account/atproto-callback", root.GetProperty( "redirect_uris" )[0].GetString( ) );
    }

    /// <summary>
    /// Verifies that with a signing key present, the JWKS endpoint returns an
    /// <c>application/json</c> document containing exactly one EC P-256 / ES256 key, and that the
    /// private component (<c>d</c>) is omitted so only the public key is published.
    /// </summary>
    [TestMethod]
    public void Jwks_WithSigningKey_ShouldReturnPublicJwks( ) {
        // Arrange
        IConfiguration config = CreateConfiguration(TestDomain);
        using ATProtoSigningKeyProvider signingKey = CreateTestSigningKeyProvider( );
        WellKnownController controller = new( config, signingKey );

        // Act
        IActionResult result = controller.Jwks( );

        // Assert
        ContentResult contentResult = (ContentResult)result;
        Assert.AreEqual( "application/json", contentResult.ContentType );
        Assert.IsNotNull( contentResult.Content );

        using JsonDocument doc = JsonDocument.Parse( contentResult.Content );
        JsonElement root = doc.RootElement;

        Assert.IsTrue( root.TryGetProperty( "keys", out JsonElement keysArray ) );
        Assert.AreEqual( 1, keysArray.GetArrayLength( ) );

        JsonElement key = keysArray[0];
        Assert.AreEqual( "EC", key.GetProperty( "kty" ).GetString( ) );
        Assert.AreEqual( "P-256", key.GetProperty( "crv" ).GetString( ) );
        Assert.AreEqual( "ES256", key.GetProperty( "alg" ).GetString( ) );

        // Verify no private key
        Assert.IsFalse( key.TryGetProperty( "d", out _ ), "JWKS endpoint must not expose private key" );
    }

    /// <summary>
    /// Verifies that without a signing key, the JWKS endpoint returns a not-found result: there is
    /// no public key to publish for a public client.
    /// </summary>
    [TestMethod]
    public void Jwks_WithoutSigningKey_ShouldReturnNotFound( ) {
        // Arrange
        IConfiguration config = CreateConfiguration(TestDomain);
        WellKnownController controller = new( config );

        // Act
        IActionResult result = controller.Jwks( );

        // Assert
        _ = Assert.IsInstanceOfType<NotFoundObjectResult>( result );
    }
}
