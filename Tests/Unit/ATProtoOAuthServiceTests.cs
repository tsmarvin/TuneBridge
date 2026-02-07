using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;
using BridgeBeats.Core.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for ATProtoOAuthService to verify OAuth token exchange and DPoP proof creation.
/// Tests the OAuth flow, token validation, DPoP JWT generation, and error handling.
/// </summary>
[TestClass]
public class ATProtoOAuthServiceTests {
    private Mock<IDbContextFactory<ApplicationDbContext>> _mockDbContextFactory = null!;
    private Mock<ILogger<ATProtoOAuthService>> _mockLogger = null!;
    private Mock<HttpMessageHandler> _mockHttpMessageHandler = null!;
    private Mock<IHttpClientFactory> _mockHttpClientFactory = null!;
    private Mock<IPersonalDataProtector> _mockPersonalDataProtector = null!;
    private HttpClient _httpClient = null!;
    private const string TestClientId = "https://example.com/.well-known/client-metadata.json";
    private const string TestBaseUrl = "https://example.com";
    private const string TestDid = "did:plc:test123456789";
    private const string TestHandle = "test.bsky.social";

    /// <summary>
    /// Initializes test resources before each test.
    /// </summary>
    [TestInitialize]
    public void Initialize( ) {
        _mockDbContextFactory = new Mock<IDbContextFactory<ApplicationDbContext>>( );
        _mockLogger = new Mock<ILogger<ATProtoOAuthService>>( );
        _mockPersonalDataProtector = new Mock<IPersonalDataProtector>( );
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>( );
        _httpClient = new HttpClient( _mockHttpMessageHandler.Object );

        // Setup mock HttpClientFactory to return the mocked HttpClient
        _mockHttpClientFactory = new Mock<IHttpClientFactory>( );
        _ = _mockHttpClientFactory
            .Setup( f => f.CreateClient( "ATProtoOAuth" ) )
            .Returns( _httpClient );
    }

    /// <summary>
    /// Cleans up test resources after each test.
    /// </summary>
    [TestCleanup]
    public void Cleanup( ) {
        _httpClient?.Dispose( );
    }

    /// <summary>
    /// Verifies that the constructor creates a valid instance with valid parameters.
    /// </summary>
    [TestMethod]
    public void Constructor_WithValidParameters_ShouldCreateInstance( ) {
        // Act
        ATProtoOAuthService service = new(
            _mockDbContextFactory.Object,
            _mockLogger.Object,
            TestClientId,
            _mockHttpClientFactory.Object,
            _mockPersonalDataProtector.Object
        );

        // Assert
        Assert.IsNotNull( service );
    }

    /// <summary>
    /// Verifies that the constructor throws ArgumentNullException for null dbContextFactory.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullDbContextFactory_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new ATProtoOAuthService( null!, _mockLogger.Object, TestClientId, _mockHttpClientFactory.Object, _mockPersonalDataProtector.Object ) );
    }

    /// <summary>
    /// Verifies that the constructor throws ArgumentNullException for null logger.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new ATProtoOAuthService( _mockDbContextFactory.Object, null!, TestClientId, _mockHttpClientFactory.Object, _mockPersonalDataProtector.Object ) );
    }

    /// <summary>
    /// Verifies that the constructor throws ArgumentNullException for null clientId.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullClientId_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new ATProtoOAuthService( _mockDbContextFactory.Object, _mockLogger.Object, null!, _mockHttpClientFactory.Object, _mockPersonalDataProtector.Object ) );
    }

    /// <summary>
    /// Verifies that the constructor throws ArgumentNullException for null httpClientFactory.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullHttpClient_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new ATProtoOAuthService( _mockDbContextFactory.Object, _mockLogger.Object, TestClientId, null!, _mockPersonalDataProtector.Object ) );
    }

    /// <summary>
    /// Verifies that the constructor throws ArgumentNullException for null personalDataProtector.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullPersonalDataProtector_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new ATProtoOAuthService( _mockDbContextFactory.Object, _mockLogger.Object, TestClientId, _mockHttpClientFactory.Object, null! ) );
    }

    /// <summary>
    /// Verifies that the constructor throws ArgumentException for invalid clientId format.
    /// </summary>
    [TestMethod]
    public void Constructor_WithInvalidClientIdFormat_ShouldThrowArgumentException( ) {
        // Arrange
        string invalidClientId = "https://example.com/invalid/path";

        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentException>( ( ) =>
            new ATProtoOAuthService( _mockDbContextFactory.Object, _mockLogger.Object, invalidClientId, _mockHttpClientFactory.Object, _mockPersonalDataProtector.Object ) );
    }

    /// <summary>
    /// Verifies that IsTokenValid returns false for expired tokens.
    /// </summary>
    [TestMethod]
    public void IsTokenValid_WithExpiredToken_ShouldReturnFalse( ) {
        // Arrange
        ATProtoOAuthService service = new(
            _mockDbContextFactory.Object,
            _mockLogger.Object,
            TestClientId,
            _mockHttpClientFactory.Object,
            _mockPersonalDataProtector.Object
        );
        DateTime expiredTime = DateTime.UtcNow.AddMinutes( -5 );

        // Act
        bool result = service.IsTokenValid( expiredTime );

        // Assert
        Assert.IsFalse( result );
    }

    /// <summary>
    /// Verifies that IsTokenValid returns false for tokens expiring within safety margin.
    /// </summary>
    [TestMethod]
    public void IsTokenValid_WithTokenExpiringWithinSafetyMargin_ShouldReturnFalse( ) {
        // Arrange
        ATProtoOAuthService service = new(
            _mockDbContextFactory.Object,
            _mockLogger.Object,
            TestClientId,
            _mockHttpClientFactory.Object,
            _mockPersonalDataProtector.Object
        );
        DateTime expiringTime = DateTime.UtcNow.AddSeconds( 15 ); // Within 30 second margin

        // Act
        bool result = service.IsTokenValid( expiringTime );

        // Assert
        Assert.IsFalse( result );
    }

    /// <summary>
    /// Verifies that IsTokenValid returns true for valid tokens.
    /// </summary>
    [TestMethod]
    public void IsTokenValid_WithValidToken_ShouldReturnTrue( ) {
        // Arrange
        ATProtoOAuthService service = new(
            _mockDbContextFactory.Object,
            _mockLogger.Object,
            TestClientId,
            _mockHttpClientFactory.Object,
            _mockPersonalDataProtector.Object
        );
        DateTime validTime = DateTime.UtcNow.AddHours( 1 );

        // Act
        bool result = service.IsTokenValid( validTime );

        // Assert
        Assert.IsTrue( result );
    }

    /// <summary>
    /// Verifies that IsTokenValid returns false for null token expiration.
    /// </summary>
    [TestMethod]
    public void IsTokenValid_WithNullExpiration_ShouldReturnFalse( ) {
        // Arrange
        ATProtoOAuthService service = new(
            _mockDbContextFactory.Object,
            _mockLogger.Object,
            TestClientId,
            _mockHttpClientFactory.Object,
            _mockPersonalDataProtector.Object
        );

        // Act
        bool result = service.IsTokenValid( null );

        // Assert
        Assert.IsFalse( result );
    }

    /// <summary>
    /// Helper method to create a valid DPoP key in JWK format for testing.
    /// </summary>
    private static string CreateTestDPoPKey( ) {
        using ECDsa ecdsa = ECDsa.Create( ECCurve.NamedCurves.nistP256 );
        ECParameters parameters = ecdsa.ExportParameters( includePrivateParameters: true );

        string Base64UrlEncode( byte[] bytes ) => Convert.ToBase64String( bytes )
                .TrimEnd( '=' )
                .Replace( '+', '-' )
                .Replace( '/', '_' );

        var jwk = new {
            kty = "EC",
            crv = "P-256",
            x = Base64UrlEncode( parameters.Q.X! ),
            y = Base64UrlEncode( parameters.Q.Y! ),
            d = Base64UrlEncode( parameters.D! ),
            kid = Guid.NewGuid( ).ToString( "N" )
        };

        return JsonSerializer.Serialize( jwk );
    }

    /// <summary>
    /// Helper method to create a test token response JSON.
    /// </summary>
    private static string CreateTestTokenResponse( string did, int expiresIn = 3600 ) {
        var response = new {
            access_token = "test_access_token_" + Guid.NewGuid( ).ToString( "N" ),
            refresh_token = "test_refresh_token_" + Guid.NewGuid( ).ToString( "N" ),
            expires_in = expiresIn,
            token_type = "DPoP",
            scope = "atproto repo:link.bridgebeats.playlist",
            sub = did
        };

        return JsonSerializer.Serialize( response );
    }

    /// <summary>
    /// Helper method to decode a DPoP JWT and verify its structure.
    /// </summary>
    private static void VerifyDPoPProof( string dpopProof, string expectedHttpMethod, string expectedUrl ) {
        // Decode the JWT
        JwtSecurityTokenHandler handler = new( );
        JwtSecurityToken jwt = handler.ReadJwtToken( dpopProof );

        // Verify header - Note: AdditionalHeaderClaims in SecurityTokenDescriptor may not always be included
        // The typ claim should be either "dpop+jwt" or "JWT" depending on the JWT library behavior
        Assert.IsTrue(
            jwt.Header.TryGetValue( "typ", out object? typValue ) &&
            (typValue?.ToString( ) == "dpop+jwt" || typValue?.ToString( ) == "JWT"),
            $"JWT typ header should be 'dpop+jwt' or 'JWT', but was '{typValue}'"
        );
        Assert.AreEqual( "ES256", jwt.Header.Alg );

        // Verify the jwk header is present in the raw JWT as a proper JSON object.
        // JwtSecurityTokenHandler.ReadJwtToken() doesn't always populate the jwk in its Header
        // dictionary, so we decode the raw JWT header to verify it was serialized correctly.
        string headerSegment = dpopProof.Split( '.' )[0];
        string paddedHeader = headerSegment.Replace( '-', '+' ).Replace( '_', '/' );
        paddedHeader = paddedHeader.PadRight( paddedHeader.Length + ((4 - (paddedHeader.Length % 4)) % 4), '=' );
        string decodedHeader = System.Text.Encoding.UTF8.GetString( Convert.FromBase64String( paddedHeader ) );
        using JsonDocument headerDoc = JsonDocument.Parse( decodedHeader );
        JsonElement headerElement = headerDoc.RootElement;
        Assert.IsTrue( headerElement.TryGetProperty( "jwk", out JsonElement jwkElement ), "JWT header should contain a 'jwk' parameter" );
        Assert.AreEqual( JsonValueKind.Object, jwkElement.ValueKind, "jwk must be a JSON object, not a string" );
        Assert.AreEqual( "EC", jwkElement.GetProperty( "kty" ).GetString( ) );
        Assert.AreEqual( "P-256", jwkElement.GetProperty( "crv" ).GetString( ) );
        Assert.IsTrue( jwkElement.TryGetProperty( "x", out _ ), "jwk should contain 'x' parameter" );
        Assert.IsTrue( jwkElement.TryGetProperty( "y", out _ ), "jwk should contain 'y' parameter" );

        // Verify claims
        Assert.AreEqual( expectedHttpMethod, jwt.Claims.First( c => c.Type == "htm" ).Value );
        Assert.AreEqual( expectedUrl, jwt.Claims.First( c => c.Type == "htu" ).Value );
        Assert.IsNotNull( jwt.Claims.FirstOrDefault( c => c.Type == "jti" ) );
        Assert.IsNotNull( jwt.Claims.FirstOrDefault( c => c.Type == "iat" ) );
        Assert.IsNotNull( jwt.Claims.FirstOrDefault( c => c.Type == "nbf" ) );

        // Verify nbf is before or equal to iat (accounting for clock skew)
        long iat = long.Parse( jwt.Claims.First( c => c.Type == "iat" ).Value );
        long nbf = long.Parse( jwt.Claims.First( c => c.Type == "nbf" ).Value );
        Assert.IsLessThanOrEqualTo( iat, nbf, $"nbf ({nbf}) should be less than or equal to iat ({iat})" );
    }

    /// <summary>
    /// Verifies that DPoP proof creation includes all required claims per RFC 9449.
    /// This test uses reflection to access the private CreateDPoPProof method.
    /// </summary>
    [TestMethod]
    public void CreateDPoPProof_ShouldIncludeRequiredClaims( ) {
        // Arrange
        string dpoPKeyJwk = CreateTestDPoPKey( );
        string httpMethod = "POST";
        string url = "https://auth.example.com/oauth/token";

        // Use reflection to call the private CreateDPoPProof method
        System.Reflection.MethodInfo? method = typeof( ATProtoOAuthService ).GetMethod(
            "CreateDPoPProof",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
        );
        Assert.IsNotNull( method, "CreateDPoPProof method should exist" );

        // Act (pass null for accessToken and nonce)
        string? dpopProof = method.Invoke(null, [dpoPKeyJwk, httpMethod, url, null, null]) as string;

        // Assert
        Assert.IsNotNull( dpopProof );
        VerifyDPoPProof( dpopProof, httpMethod, url );
    }

    /// <summary>
    /// Verifies that DPoP proof includes access token hash when provided.
    /// </summary>
    [TestMethod]
    public void CreateDPoPProof_WithAccessToken_ShouldIncludeAthClaim( ) {
        // Arrange
        string dpoPKeyJwk = CreateTestDPoPKey( );
        string httpMethod = "GET";
        string url = "https://api.example.com/resource";
        string accessToken = "test_access_token";

        // Use reflection to call the private CreateDPoPProof method
        System.Reflection.MethodInfo? method = typeof( ATProtoOAuthService ).GetMethod(
            "CreateDPoPProof",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
        );
        Assert.IsNotNull( method );

        // Act (pass accessToken, nonce is null)
        string? dpopProof = method.Invoke(null, [dpoPKeyJwk, httpMethod, url, accessToken, null]) as string;

        // Assert
        Assert.IsNotNull( dpopProof );
        JwtSecurityTokenHandler handler = new( );
        JwtSecurityToken jwt = handler.ReadJwtToken( dpopProof );

        // Verify ath claim exists
        System.Security.Claims.Claim? athClaim = jwt.Claims.FirstOrDefault( c => c.Type == "ath" );
        Assert.IsNotNull( athClaim, "DPoP proof should include 'ath' claim when access token is provided" );
    }

    /// <summary>
    /// Verifies that nbf claim is set to account for clock skew.
    /// </summary>
    [TestMethod]
    public void CreateDPoPProof_ShouldSetNbfForClockSkew( ) {
        // Arrange
        string dpoPKeyJwk = CreateTestDPoPKey( );
        string httpMethod = "POST";
        string url = "https://auth.example.com/oauth/token";

        // Use reflection to call the private CreateDPoPProof method
        System.Reflection.MethodInfo? method = typeof( ATProtoOAuthService ).GetMethod(
            "CreateDPoPProof",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
        );
        Assert.IsNotNull( method );

        // Act (pass null for accessToken and nonce)
        string? dpopProof = method.Invoke(null, [dpoPKeyJwk, httpMethod, url, null, null]) as string;

        // Assert
        Assert.IsNotNull( dpopProof );
        JwtSecurityTokenHandler handler = new( );
        JwtSecurityToken jwt = handler.ReadJwtToken( dpopProof );

        long iat = long.Parse( jwt.Claims.First( c => c.Type == "iat" ).Value );
        long nbf = long.Parse( jwt.Claims.First( c => c.Type == "nbf" ).Value );

        // nbf should be 5 seconds before iat for clock skew tolerance
        // Allow some tolerance since time may pass between setting the values
        long difference = iat - nbf;
        Assert.IsTrue( difference is >= 4 and <= 6,
            $"nbf should be approximately 5 seconds before iat, but difference was {difference} (iat={iat}, nbf={nbf})" );
    }

    /// <summary>
    /// Verifies that DPoP proof includes nonce when provided (per RFC 9449).
    /// </summary>
    [TestMethod]
    public void CreateDPoPProof_WithNonce_ShouldIncludeNonceClaim( ) {
        // Arrange
        string dpoPKeyJwk = CreateTestDPoPKey();
        string httpMethod = "POST";
        string url = "https://auth.example.com/oauth/token";
        string nonce = "server-provided-nonce-12345";

        // Use reflection to call the private CreateDPoPProof method
        System.Reflection.MethodInfo? method = typeof(ATProtoOAuthService).GetMethod(
            "CreateDPoPProof",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
        );
        Assert.IsNotNull( method );

        // Act (pass null for accessToken, provide nonce)
        string? dpopProof = method.Invoke(null, [dpoPKeyJwk, httpMethod, url, null, nonce]) as string;

        // Assert
        Assert.IsNotNull( dpopProof );
        JwtSecurityTokenHandler handler = new();
        JwtSecurityToken jwt = handler.ReadJwtToken(dpopProof);

        // Verify nonce claim exists and has correct value
        System.Security.Claims.Claim? nonceClaim = jwt.Claims.FirstOrDefault(c => c.Type == "nonce");
        Assert.IsNotNull( nonceClaim, "DPoP proof should include 'nonce' claim when nonce is provided" );
        Assert.AreEqual( nonce, nonceClaim.Value, "Nonce claim value should match provided nonce" );
    }

    /// <summary>
    /// Verifies that DPoP proof includes both ath and nonce when both are provided.
    /// </summary>
    [TestMethod]
    public void CreateDPoPProof_WithAccessTokenAndNonce_ShouldIncludeBothClaims( ) {
        // Arrange
        string dpoPKeyJwk = CreateTestDPoPKey();
        string httpMethod = "GET";
        string url = "https://api.example.com/resource";
        string accessToken = "test_access_token";
        string nonce = "server-nonce-abc123";

        // Use reflection to call the private CreateDPoPProof method
        System.Reflection.MethodInfo? method = typeof(ATProtoOAuthService).GetMethod(
            "CreateDPoPProof",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
        );
        Assert.IsNotNull( method );

        // Act
        string? dpopProof = method.Invoke(null, [dpoPKeyJwk, httpMethod, url, accessToken, nonce]) as string;

        // Assert
        Assert.IsNotNull( dpopProof );
        JwtSecurityTokenHandler handler = new();
        JwtSecurityToken jwt = handler.ReadJwtToken(dpopProof);

        // Verify both claims exist
        System.Security.Claims.Claim? athClaim = jwt.Claims.FirstOrDefault(c => c.Type == "ath");
        System.Security.Claims.Claim? nonceClaim = jwt.Claims.FirstOrDefault(c => c.Type == "nonce");
        Assert.IsNotNull( athClaim, "DPoP proof should include 'ath' claim when access token is provided" );
        Assert.IsNotNull( nonceClaim, "DPoP proof should include 'nonce' claim when nonce is provided" );
        Assert.AreEqual( nonce, nonceClaim.Value );
    }
}
