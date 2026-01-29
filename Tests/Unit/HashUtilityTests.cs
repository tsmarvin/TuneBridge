using BridgeBeats.Core.Infrastructure.Utilities;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="HashUtility"/>.
/// </summary>
[TestClass]
public class HashUtilityTests {

    /// <summary>
    /// Verifies that ComputeSha256Base32 returns consistent hashes for the same input.
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
    /// Verifies that ComputeSha256Base32 returns different hashes for different inputs.
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
    /// Verifies that ComputeSha256Base32 returns lowercase output.
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
    /// Verifies that ComputeSha256Base32 returns exactly 52 characters.
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
    /// Verifies that ComputeSha256Base32 throws <see cref="ArgumentNullException"/> for null input.
    /// </summary>
    [TestMethod]
    public void ComputeSha256Base32_ThrowsArgumentNullException_ForNullInput( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) => HashUtility.ComputeSha256Base32( null! ) );
    }

    /// <summary>
    /// Verifies that HashUrl normalizes URLs to lowercase for consistent hashing.
    /// </summary>
    [TestMethod]
    public void HashUrl_NormalizesUrl_ToLowercase( ) {
        // Arrange
        string url1 = "https://EXAMPLE.COM/Path";
        string url2 = "https://example.com/path";

        // Act
        string hash1 = HashUtility.HashUrl( url1 );
        string hash2 = HashUtility.HashUrl( url2 );

        // Assert
        Assert.AreEqual( hash1, hash2 );
    }

    /// <summary>
    /// Verifies that HashUrl trims whitespace before hashing.
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
    /// Verifies that HashUrl throws <see cref="ArgumentNullException"/> for null URL.
    /// </summary>
    [TestMethod]
    public void HashUrl_ThrowsArgumentNullException_ForNullUrl( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) => HashUtility.HashUrl( null! ) );
    }

    /// <summary>
    /// Verifies that HashUrl throws <see cref="ArgumentException"/> for empty URL.
    /// </summary>
    [TestMethod]
    public void HashUrl_ThrowsArgumentException_ForEmptyUrl( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentException>( ( ) => HashUtility.HashUrl( "" ) );
    }

    /// <summary>
    /// Verifies that HashUrl throws <see cref="ArgumentException"/> for whitespace-only URL.
    /// </summary>
    [TestMethod]
    public void HashUrl_ThrowsArgumentException_ForWhitespaceUrl( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentException>( ( ) => HashUtility.HashUrl( "   " ) );
    }

    /// <summary>
    /// Verifies that ComputeSha256Base32 handles empty strings correctly.
    /// </summary>
    [TestMethod]
    public void ComputeSha256Base32_HandlesEmptyString( ) {
        // Arrange
        string input = "";

        // Act
        string hash = HashUtility.ComputeSha256Base32( input );

        // Assert
        Assert.IsNotNull( hash );
        Assert.AreEqual( 52, hash.Length );
    }

    /// <summary>
    /// Verifies that ComputeSha256Base32 handles Unicode characters correctly.
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
    /// Verifies that HashUrl handles very long URLs correctly.
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
