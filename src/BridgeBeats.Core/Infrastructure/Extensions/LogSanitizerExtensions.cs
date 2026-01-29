using System.Text.RegularExpressions;

namespace BridgeBeats.Infrastructure.Utilities;

/// <summary>
/// Extension methods for sanitizing log input.
/// </summary>
public static partial class LogSanitizerExtensions {

    /// <summary>
    /// Sanitizes user input for safe logging by removing or replacing characters that could be used for log injection attacks.
    /// </summary>
    /// <param name="input">The user-provided string to sanitize</param>
    /// <returns>A sanitized string safe for logging</returns>
    public static string SanitizeForLogging( this string? input ) {
        if (string.IsNullOrWhiteSpace( input )) { return string.Empty; }
        // Remove all ASCII control characters (0x00-0x1F, 0x7F) to prevent log injection and forging
        return LogSanitizerRegex( ).Replace( input, string.Empty );
    }

    [GeneratedRegex( @"[\x00-\x1F\x7F]" )]
    private static partial Regex LogSanitizerRegex( );
}
