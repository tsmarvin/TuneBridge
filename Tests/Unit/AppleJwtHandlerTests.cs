using System.Security.Cryptography;
using BridgeBeats.Core.Domain.Providers.AppleMusic;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Tests <see cref="AppleJwtHandler"/>, which signs the Apple Music developer token (an ES256 JWT
/// built from a P-256 <c>.p8</c> private key) used to authenticate provider requests.
/// </summary>
/// <remarks>
/// Each test constructs the handler from a freshly generated PEM-encoded P-256 key produced in
/// <see cref="Initialize"/>, then exercises construction validation and the authentication-header
/// output. No network call is involved; the token is signed locally.
/// </remarks>
[TestClass]
public class AppleJwtHandlerTests {
    /// <summary>Filesystem path of the temporary <c>.p8</c> key file written for the test.</summary>
    private string _testKeyPath = null!;

    /// <summary>PEM-encoded contents of the generated P-256 private key passed to the handler.</summary>
    private string _testKeyContents = null!;

    /// <summary>Placeholder Apple developer team id used as the token issuer.</summary>
    private const string TestTeamId = "TEST123456";

    /// <summary>Placeholder Apple key id used in the token header.</summary>
    private const string TestKeyId = "KEY1234567";

    /// <summary>
    /// Generates a fresh P-256 ECDSA key, serializes its PKCS#8 private key into PEM form (the
    /// <c>.p8</c> shape Apple issues), records the contents for direct use, and writes a copy to a
    /// temporary file so each test runs against a valid signing key.
    /// </summary>
    [TestInitialize]
    public void Initialize( ) {
        // Generate a valid ES256 (P-256) private key for testing
        using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        // Export as PKCS8 format which is what .p8 files use
        byte[] privateKeyBytes = ecdsa.ExportPkcs8PrivateKey( );

        // Create a properly formatted PEM string
        string base64Key = Convert.ToBase64String(privateKeyBytes);
        System.Text.StringBuilder formattedKey = new( );
        _ = formattedKey.AppendLine( "-----BEGIN PRIVATE KEY-----" );

        // Split into 64-character lines as per PEM format
        for (int i = 0; i < base64Key.Length; i += 64) {
            int length = Math.Min(64, base64Key.Length - i);
            _ = formattedKey.AppendLine( base64Key.Substring( i, length ) );
        }

        _ = formattedKey.AppendLine( "-----END PRIVATE KEY-----" );
        _testKeyContents = formattedKey.ToString( );

        _testKeyPath = TestArtifacts.CreateFilePath( "test-key", ".p8" );
        File.WriteAllText( _testKeyPath, _testKeyContents );
    }

    /// <summary>
    /// Verifies that constructing an <see cref="AppleJwtHandler"/> with a valid team id, key id, and
    /// PEM-encoded P-256 key succeeds and produces a non-null instance.
    /// </summary>
    [TestMethod]
    public void Constructor_WithValidParameters_ShouldCreateInstance( ) {
        // Act & Assert
        AppleJwtHandler handler = new(TestTeamId, TestKeyId, _testKeyContents);
        Assert.IsNotNull( handler );
    }

    /// <summary>
    /// Verifies that constructing the handler with PEM contents whose body is not a valid key throws
    /// <see cref="ArgumentException"/>, so a malformed signing key fails fast at construction.
    /// </summary>
    [TestMethod]
    public void Constructor_WithInvalidKey_ShouldThrowException( ) {
        // Arrange
        string invalidKeyContents = "-----BEGIN PRIVATE KEY-----\nINVALID\n-----END PRIVATE KEY-----";

        // Act & Assert - Invalid key should throw either CryptographicException or ArgumentException
        _ = Assert.ThrowsExactly<ArgumentException>( ( ) =>
            new AppleJwtHandler( TestTeamId, TestKeyId, invalidKeyContents ) );
    }

    /// <summary>
    /// Verifies that <see cref="AppleJwtHandler.NewAuthenticationHeader"/> returns a <c>Bearer</c>
    /// authentication header carrying a non-empty token parameter.
    /// </summary>
    [TestMethod]
    public void GetAuthHeader_ShouldReturnBearerToken( ) {
        // Arrange
        AppleJwtHandler handler = new(TestTeamId, TestKeyId, _testKeyContents);

        // Act
        System.Net.Http.Headers.AuthenticationHeaderValue authHeader = handler.NewAuthenticationHeader( );

        // Assert
        Assert.IsNotNull( authHeader );
        Assert.AreEqual( "Bearer", authHeader.Scheme );
        Assert.IsNotNull( authHeader.Parameter );
        Assert.IsGreaterThan( 0, authHeader.Parameter.Length );
    }

    /// <summary>
    /// Verifies that the token produced by <see cref="AppleJwtHandler.NewAuthenticationHeader"/> has the
    /// three dot-separated, non-empty segments (header, payload, signature) of a well-formed JWS.
    /// </summary>
    [TestMethod]
    public void GetAuthHeader_ShouldReturnValidJwtStructure( ) {
        // Arrange
        AppleJwtHandler handler = new(TestTeamId, TestKeyId, _testKeyContents);

        // Act
        System.Net.Http.Headers.AuthenticationHeaderValue authHeader = handler.NewAuthenticationHeader( );
        string? token = authHeader.Parameter;

        // Assert - JWT should have 3 parts separated by dots
        Assert.IsNotNull( token );
        string[] parts = token.Split( '.' );
        Assert.HasCount( 3, parts );

        // Each part should be base64url encoded (not empty)
        foreach (string part in parts) {
            Assert.IsGreaterThan( 0, part.Length );
        }
    }

    /// <summary>
    /// Verifies that two header requests separated by a one-second delay yield different tokens,
    /// confirming each call mints a fresh JWT with current time-based claims rather than caching one.
    /// </summary>
    [TestMethod]
    public void GetAuthHeader_CalledMultipleTimes_ShouldReturnDifferentTokens( ) {
        // Arrange
        AppleJwtHandler handler = new(TestTeamId, TestKeyId, _testKeyContents);

        // Act
        string? token1 = handler.NewAuthenticationHeader( ).Parameter;
        System.Threading.Thread.Sleep( 1000 ); // Ensure different timestamp
        string? token2 = handler.NewAuthenticationHeader( ).Parameter;

        // Assert - Tokens should be different due to different timestamps
        Assert.AreNotEqual( token1, token2 );
    }

    /// <summary>Deletes the temporary <c>.p8</c> key file written in <see cref="Initialize"/>, if present.</summary>
    [TestCleanup]
    public void Cleanup( ) {
        if (File.Exists( _testKeyPath )) {
            File.Delete( _testKeyPath );
        }
    }
}
