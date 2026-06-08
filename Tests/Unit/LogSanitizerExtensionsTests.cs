using BridgeBeats.Core.Infrastructure.Utilities;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="LogSanitizerExtensions"/>.
/// </summary>
[TestClass]
public class LogSanitizerExtensionsTests {

    #region Null and whitespace input tests

    /// <summary>
    /// Verifies that SanitizeForLogging returns an empty string for null input.
    /// </summary>
    [TestMethod]
    public void SanitizeForLogging_ReturnsEmpty_ForNullInput( ) {
        // Arrange
        string? input = null;

        // Act
        string result = input.SanitizeForLogging( );

        // Assert
        Assert.AreEqual( string.Empty, result );
    }

    /// <summary>
    /// Verifies that SanitizeForLogging returns an empty string for empty-string input.
    /// </summary>
    [TestMethod]
    public void SanitizeForLogging_ReturnsEmpty_ForEmptyString( ) {
        // Arrange
        string input = string.Empty;

        // Act
        string result = input.SanitizeForLogging( );

        // Assert
        Assert.AreEqual( string.Empty, result );
    }

    /// <summary>
    /// Verifies that SanitizeForLogging returns an empty string for whitespace-only input.
    /// </summary>
    [TestMethod]
    public void SanitizeForLogging_ReturnsEmpty_ForWhitespaceOnlyInput( ) {
        // Arrange
        string input = "   \t  ";

        // Act
        string result = input.SanitizeForLogging( );

        // Assert
        Assert.AreEqual( string.Empty, result );
    }

    #endregion

    #region C0 control character tests

    /// <summary>
    /// Verifies that SanitizeForLogging removes C0 control characters (U+0000-U+001F).
    /// </summary>
    [TestMethod]
    public void SanitizeForLogging_RemovesC0ControlCharacters( ) {
        // Arrange - embed a null byte, carriage return, and line feed among normal text
        string input = "before" + (char)0x00 + (char)0x0D + (char)0x0A + "after";

        // Act
        string result = input.SanitizeForLogging( );

        // Assert
        Assert.AreEqual( "beforeafter", result );
    }

    /// <summary>
    /// Verifies that SanitizeForLogging removes DEL (U+007F).
    /// </summary>
    [TestMethod]
    public void SanitizeForLogging_RemovesDelCharacter( ) {
        // Arrange
        string input = "before" + (char)0x7F + "after";

        // Act
        string result = input.SanitizeForLogging( );

        // Assert
        Assert.AreEqual( "beforeafter", result );
    }

    #endregion

    #region C1 control character tests

    /// <summary>
    /// Verifies that SanitizeForLogging removes C1 control character CSI (U+009B).
    /// </summary>
    [TestMethod]
    public void SanitizeForLogging_RemovesC1ControlCharacter_Csi( ) {
        // Arrange - U+009B is CSI (Control Sequence Introducer), first byte of ANSI escape sequences
        string input = "before" + (char)0x9B + "after";

        // Act
        string result = input.SanitizeForLogging( );

        // Assert
        Assert.AreEqual( "beforeafter", result );
    }

    /// <summary>
    /// Verifies that SanitizeForLogging removes NEL (U+0085, C1 Next Line).
    /// </summary>
    [TestMethod]
    public void SanitizeForLogging_RemovesC1ControlCharacter_Nel( ) {
        // Arrange - U+0085 is NEL (Next Line), a Unicode newline variant
        string input = "before" + (char)0x85 + "after";

        // Act
        string result = input.SanitizeForLogging( );

        // Assert
        Assert.AreEqual( "beforeafter", result );
    }

    /// <summary>
    /// Verifies that SanitizeForLogging removes the full C1 range (U+0080-U+009F).
    /// </summary>
    [TestMethod]
    public void SanitizeForLogging_RemovesEntireC1Range( ) {
        // Arrange - build a string with every C1 character flanked by known-good chars
        string c1Block = string.Concat( Enumerable.Range( 0x80, 0x20 ).Select( i => (char)i ) );
        string input = "start" + c1Block + "end";

        // Act
        string result = input.SanitizeForLogging( );

        // Assert
        Assert.AreEqual( "startend", result );
    }

    #endregion

    #region Unicode line/paragraph separator tests

    /// <summary>
    /// Verifies that SanitizeForLogging removes U+2028 (LINE SEPARATOR).
    /// </summary>
    [TestMethod]
    public void SanitizeForLogging_RemovesLineSeparator_U2028( ) {
        // Arrange - U+2028 is the Unicode LINE SEPARATOR; used in log injection via multiline forging
        string input = "before" + (char)0x2028 + "after";

        // Act
        string result = input.SanitizeForLogging( );

        // Assert
        Assert.AreEqual( "beforeafter", result );
    }

    /// <summary>
    /// Verifies that SanitizeForLogging removes U+2029 (PARAGRAPH SEPARATOR).
    /// </summary>
    [TestMethod]
    public void SanitizeForLogging_RemovesParagraphSeparator_U2029( ) {
        // Arrange - U+2029 is the Unicode PARAGRAPH SEPARATOR
        string input = "before" + (char)0x2029 + "after";

        // Act
        string result = input.SanitizeForLogging( );

        // Assert
        Assert.AreEqual( "beforeafter", result );
    }

    /// <summary>
    /// Verifies that SanitizeForLogging removes both U+2028 and U+2029 when they appear together.
    /// </summary>
    [TestMethod]
    public void SanitizeForLogging_RemovesBothUnicodeSeparators( ) {
        // Arrange
        string input = "a" + (char)0x2028 + "b" + (char)0x2029 + "c";

        // Act
        string result = input.SanitizeForLogging( );

        // Assert
        Assert.AreEqual( "abc", result );
    }

    #endregion

    #region Legitimate Unicode passthrough tests

    /// <summary>
    /// Verifies that SanitizeForLogging preserves plain ASCII text without modification.
    /// </summary>
    [TestMethod]
    public void SanitizeForLogging_PreservesPlainAsciiText( ) {
        // Arrange
        string input = "Hello, world! 123 #$%";

        // Act
        string result = input.SanitizeForLogging( );

        // Assert
        Assert.AreEqual( input, result );
    }

    /// <summary>
    /// Verifies that SanitizeForLogging preserves non-ASCII letters (e.g., accented, CJK).
    /// </summary>
    [TestMethod]
    public void SanitizeForLogging_PreservesNonAsciiLetters( ) {
        // Arrange - accented characters (Latin Extended), CJK ideographs, Arabic
        string input = "café 中文 مرحبا";

        // Act
        string result = input.SanitizeForLogging( );

        // Assert
        Assert.AreEqual( input, result );
    }

    /// <summary>
    /// Verifies that SanitizeForLogging preserves emoji, which are well above the blocked ranges.
    /// </summary>
    [TestMethod]
    public void SanitizeForLogging_PreservesEmoji( ) {
        // Arrange - emoji lie well outside the blocked control-character ranges
        string input = "track " + char.ConvertFromUtf32( 0x1F3B5 ) + " found";

        // Act
        string result = input.SanitizeForLogging( );

        // Assert
        Assert.AreEqual( input, result );
    }

    /// <summary>
    /// Verifies that SanitizeForLogging preserves regular space (U+0020).
    /// </summary>
    [TestMethod]
    public void SanitizeForLogging_PreservesRegularSpace( ) {
        // Arrange - U+0020 (SPACE) is above the C0 range and must not be stripped
        string input = "hello world";

        // Act
        string result = input.SanitizeForLogging( );

        // Assert
        Assert.AreEqual( input, result );
    }

    #endregion
}
