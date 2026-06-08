using System.Text.RegularExpressions;

namespace BridgeBeats.Core.Infrastructure.Utilities;

/// <summary>
/// Extension methods for sanitizing log input.
/// </summary>
public static partial class LogSanitizerExtensions {

    /// <summary>
    /// Sanitizes user input for safe logging by removing characters that could be used for log injection attacks.
    /// </summary>
    /// <param name="input">The user-provided string to sanitize</param>
    /// <returns>A sanitized string safe for logging, or <see cref="string.Empty"/> if the input is null or whitespace</returns>
    public static string SanitizeForLogging( this string? input ) {
        if (string.IsNullOrWhiteSpace( input )) { return string.Empty; }
        // Remove C0 controls (U+0000-U+001F), DEL (U+007F), C1 controls (U+0080-U+009F),
        // and Unicode line/paragraph separators (U+2028, U+2029) to prevent log injection and forging
        return LogSanitizerRegex( ).Replace( input, string.Empty );
    }

    [GeneratedRegex( @"[\p{Cc}\p{Zl}\p{Zp}]" )]
    private static partial Regex LogSanitizerRegex( );
}
