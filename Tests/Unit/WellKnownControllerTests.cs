using System.Text.Json;
using BridgeBeats.Core.Infrastructure.Identity;
using BridgeBeats.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for WellKnownController to verify dynamic client-metadata.json
/// and jwks.json endpoint behavior for ATProto OAuth confidential client support.
/// </summary>
[TestClass]
public class WellKnownControllerTests {
    private const string TestBaseUrl = "https://example.com";

    /// <summary>
    /// Creates an IConfiguration with the specified BaseUrl.
    /// </summary>
    private static IConfiguration CreateConfiguration( string? baseUrl ) {
        Dictionary<string, string?> settings = new( ) {
            ["BridgeBeats:BaseUrl"] = baseUrl
        };

        return new ConfigurationBuilder( )
            .AddInMemoryCollection( settings )
            .Build( );
    }

    /// <summary>
    /// Creates a test signing key provider with a generated ES256 key.
    /// </summary>
    private static ATProtoSigningKeyProvider CreateTestSigningKeyProvider( ) {
        string jwk = ATProtoSigningKeyProvider.GenerateNewSigningKeyJwk( );
        return new ATProtoSigningKeyProvider( jwk );
    }

    /// <summary>
    /// Verifies that ClientMetadata returns JSON with confidential client fields when signing key is present.
    /// </summary>
    [TestMethod]
    public void ClientMetadata_WithSigningKey_ShouldReturnConfidentialClientMetadata( ) {
        // Arrange
        IConfiguration config = CreateConfiguration( TestBaseUrl );
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

        Assert.AreEqual( $"{TestBaseUrl}/.well-known/client-metadata.json", root.GetProperty( "client_id" ).GetString( ) );
        Assert.AreEqual( "BridgeBeats", root.GetProperty( "client_name" ).GetString( ) );
        Assert.AreEqual( "private_key_jwt", root.GetProperty( "token_endpoint_auth_method" ).GetString( ) );
        Assert.AreEqual( "ES256", root.GetProperty( "token_endpoint_auth_signing_alg" ).GetString( ) );
        Assert.AreEqual( $"{TestBaseUrl}/.well-known/jwks.json", root.GetProperty( "jwks_uri" ).GetString( ) );
        Assert.IsTrue( root.GetProperty( "dpop_bound_access_tokens" ).GetBoolean( ) );

        // Verify redirect_uris
        JsonElement redirectUris = root.GetProperty( "redirect_uris" );
        Assert.AreEqual( 1, redirectUris.GetArrayLength( ) );
        Assert.AreEqual( $"{TestBaseUrl}/account/atproto-callback", redirectUris[0].GetString( ) );

        // Verify scope
        Assert.AreEqual( "atproto repo:link.bridgebeats.playlist", root.GetProperty( "scope" ).GetString( ) );
    }

    /// <summary>
    /// Verifies that ClientMetadata returns public client fields when no signing key is present.
    /// </summary>
    [TestMethod]
    public void ClientMetadata_WithoutSigningKey_ShouldReturnPublicClientMetadata( ) {
        // Arrange
        IConfiguration config = CreateConfiguration( TestBaseUrl );
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
    /// Verifies that ClientMetadata returns NotFound when BaseUrl is not configured.
    /// </summary>
    [TestMethod]
    public void ClientMetadata_WithoutBaseUrl_ShouldReturnNotFound( ) {
        // Arrange
        IConfiguration config = CreateConfiguration( null );
        WellKnownController controller = new( config );

        // Act
        IActionResult result = controller.ClientMetadata( );

        // Assert
        _ = Assert.IsInstanceOfType<NotFoundObjectResult>( result );
    }

    /// <summary>
    /// Verifies that ClientMetadata uses the configured BaseUrl for all URLs.
    /// </summary>
    [TestMethod]
    public void ClientMetadata_ShouldUseDynamicBaseUrl( ) {
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
    /// Verifies that Jwks returns valid JWKS JSON with the public key.
    /// </summary>
    [TestMethod]
    public void Jwks_WithSigningKey_ShouldReturnPublicJwks( ) {
        // Arrange
        IConfiguration config = CreateConfiguration( TestBaseUrl );
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
    /// Verifies that Jwks returns NotFound when no signing key is configured.
    /// </summary>
    [TestMethod]
    public void Jwks_WithoutSigningKey_ShouldReturnNotFound( ) {
        // Arrange
        IConfiguration config = CreateConfiguration( TestBaseUrl );
        WellKnownController controller = new( config );

        // Act
        IActionResult result = controller.Jwks( );

        // Assert
        _ = Assert.IsInstanceOfType<NotFoundObjectResult>( result );
    }
}
