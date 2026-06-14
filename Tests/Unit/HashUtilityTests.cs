using BridgeBeats.Core.Infrastructure.Utilities;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Tests <see cref="HashUtility"/>, which produces deterministic lowercase base32 SHA-256 hashes used as cache
/// keys. Covers <c>ComputeSha256Base32</c> (determinism, collision avoidance, lowercase output, fixed 52-character
/// length, null/empty/unicode handling) and <c>HashUrl</c> (host-case and whitespace normalization before hashing,
/// and its argument guards).
/// </summary>
[TestClass]
public class HashUtilityTests {

    /// <summary>
    /// Verifies <see cref="HashUtility.ComputeSha256Base32"/> returns the same hash for the same input (deterministic).
    /// </summary>
    [TestMethod]
    public void ComputeSha256Base32_ReturnsConsistentHash_ForSameInput( ) {
        // Arrange
        string input = "test string";

        // Act
        string hash1 = HashUtility.ComputeSha256Base32( input );
        string hash2 = HashUtility.ComputeSha256Base32( input );

        // Assert
        Assert.AreEqual( hash1, hash2 );
    }

    /// <summary>
    /// Verifies different inputs produce different hashes.
    /// </summary>
    [TestMethod]
    public void ComputeSha256Base32_ReturnsDifferentHash_ForDifferentInput( ) {
        // Arrange
        string input1 = "test string 1";
        string input2 = "test string 2";

        // Act
        string hash1 = HashUtility.ComputeSha256Base32( input1 );
        string hash2 = HashUtility.ComputeSha256Base32( input2 );

        // Assert
        Assert.AreNotEqual( hash1, hash2 );
    }

    /// <summary>
    /// Verifies the base32 hash is emitted in lowercase.
    /// </summary>
    [TestMethod]
    public void ComputeSha256Base32_ReturnsLowercase( ) {
        // Arrange
        string input = "Test String With Uppercase";

        // Act
        string hash = HashUtility.ComputeSha256Base32( input );

        // Assert
        Assert.AreEqual( hash, hash.ToLowerInvariant( ) );
    }

    /// <summary>
    /// Verifies the hash is the full base32 encoding of a 256-bit digest (52 characters).
    /// </summary>
    [TestMethod]
    public void ComputeSha256Base32_ReturnsFullHashLength( ) {
        // Arrange
        string input = "any input string";

        // Act
        string hash = HashUtility.ComputeSha256Base32( input );

        // Assert
        // SHA-256 produces 256 bits, base32 encodes 5 bits per character = 52 characters
        Assert.AreEqual( 52, hash.Length );
    }

    /// <summary>
    /// Verifies a null input is rejected with an <see cref="ArgumentNullException"/>.
    /// </summary>
    [TestMethod]
    public void ComputeSha256Base32_ThrowsArgumentNullException_ForNullInput( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) => HashUtility.ComputeSha256Base32( null! ) );
    }

    /// <summary>
    /// Verifies <see cref="HashUtility.HashUrl"/> normalizes the URL's host case before hashing (via
    /// <see cref="BridgeBeats.Core.Domain.Providers.Common.LinkNormalizer"/>), so URLs differing only in
    /// domain case hash equally. Path and ID case are preserved, not normalized.
    /// </summary>
    [TestMethod]
    public void HashUrl_NormalizesUrl_ToLowercase( ) {
        // Arrange - different domain case should normalize, but paths with different case should NOT
        string url1 = "https://EXAMPLE.COM/path";
        string url2 = "https://example.com/path";

        // Act
        string hash1 = HashUtility.HashUrl( url1 );
        string hash2 = HashUtility.HashUrl( url2 );

        // Assert - domain case-insensitive
        Assert.AreEqual( hash1, hash2, "URLs with different domain case should produce same hash" );
    }

    /// <summary>
    /// Verifies <see cref="HashUtility.HashUrl"/> trims surrounding whitespace before hashing.
    /// </summary>
    [TestMethod]
    public void HashUrl_TrimsWhitespace( ) {
        // Arrange
        string url1 = "  https://example.com/path  ";
        string url2 = "https://example.com/path";

        // Act
        string hash1 = HashUtility.HashUrl( url1 );
        string hash2 = HashUtility.HashUrl( url2 );

        // Assert
        Assert.AreEqual( hash1, hash2 );
    }

    /// <summary>
    /// Verifies a null URL is rejected with an <see cref="ArgumentNullException"/>.
    /// </summary>
    [TestMethod]
    public void HashUrl_ThrowsArgumentNullException_ForNullUrl( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) => HashUtility.HashUrl( null! ) );
    }

    /// <summary>
    /// Verifies an empty URL is rejected with an <see cref="ArgumentException"/>.
    /// </summary>
    [TestMethod]
    public void HashUrl_ThrowsArgumentException_ForEmptyUrl( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentException>( ( ) => HashUtility.HashUrl( "" ) );
    }

    /// <summary>
    /// Verifies a whitespace-only URL is rejected with an <see cref="ArgumentException"/> (it normalizes to empty).
    /// </summary>
    [TestMethod]
    public void HashUrl_ThrowsArgumentException_ForWhitespaceUrl( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentException>( ( ) => HashUtility.HashUrl( "   " ) );
    }

    /// <summary>
    /// Verifies an empty string hashes to a valid 52-character result (empty input is allowed, unlike for
    /// <see cref="HashUtility.HashUrl"/>).
    /// </summary>
    [TestMethod]
    public void ComputeSha256Base32_HandlesEmptyString( ) {
        // Arrange
        string input = string.Empty;

        // Act
        string hash = HashUtility.ComputeSha256Base32( input );

        // Assert
        Assert.IsNotNull( hash );
        Assert.AreEqual( 52, hash.Length );
    }

    /// <summary>
    /// Verifies multi-byte unicode input hashes to a valid 52-character result.
    /// </summary>
    [TestMethod]
    public void ComputeSha256Base32_HandlesUnicode( ) {
        // Arrange
        string input = "日本語テスト";

        // Act
        string hash = HashUtility.ComputeSha256Base32( input );

        // Assert
        Assert.IsNotNull( hash );
        Assert.AreEqual( 52, hash.Length );
    }

    /// <summary>
    /// Verifies a very long URL hashes to a fixed 52-character result (output length is independent of input length).
    /// </summary>
    [TestMethod]
    public void HashUrl_HandlesLongUrls( ) {
        // Arrange
        string longUrl = "https://example.com/" + new string( 'a', 2000 );

        // Act
        string hash = HashUtility.HashUrl( longUrl );

        // Assert
        Assert.IsNotNull( hash );
        Assert.AreEqual( 52, hash.Length );
    }
}
