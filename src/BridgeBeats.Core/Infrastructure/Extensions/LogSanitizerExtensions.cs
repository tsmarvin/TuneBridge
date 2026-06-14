using System.Text.RegularExpressions;

namespace BridgeBeats.Core.Infrastructure.Utilities;

/// <summary>
/// Extension helpers that sanitize untrusted string values before they are written to logs,
/// reducing the risk of log forging and log-injection from control or line-separator characters.
/// </summary>
public static partial class LogSanitizerExtensions {

    /// <summary>
    /// Returns a copy of <paramref name="input"/> with Unicode control characters, line separators,
    /// and paragraph separators removed, for safe inclusion in a log message.
    /// </summary>
    /// <param name="input">The untrusted value to sanitize. May be null.</param>
    /// <returns>
    /// An empty string when <paramref name="input"/> is null, empty, or whitespace; otherwise the
    /// input with all matched control and separator characters stripped.
    /// </returns>
    /// <remarks>
    /// The removed set is defined by the Unicode categories <c>Cc</c> (control), <c>Zl</c> (line
    /// separator), and <c>Zp</c> (paragraph separator). This strips characters such as carriage
    /// returns and line feeds that could otherwise be used to forge additional log entries; it does
    /// not encode or escape other content.
    /// </remarks>
    public static string SanitizeForLogging( this string? input ) {
        if (string.IsNullOrWhiteSpace( input )) { return string.Empty; }
        // Remove C0 controls (U+0000-U+001F), DEL (U+007F), C1 controls (U+0080-U+009F),
        // and Unicode line/paragraph separators (U+2028, U+2029) to prevent log injection and forging
        return LogSanitizerRegex( ).Replace( input, string.Empty );
    }

    /// <summary>
    /// Source-generated regular expression that matches a single Unicode control character
    /// (category <c>Cc</c>), line separator (<c>Zl</c>), or paragraph separator (<c>Zp</c>).
    /// </summary>
    /// <returns>The compiled <see cref="Regex"/> used by <see cref="SanitizeForLogging(string?)"/>.</returns>
    [GeneratedRegex( @"[\p{Cc}\p{Zl}\p{Zp}]" )]
    private static partial Regex LogSanitizerRegex( );
}
