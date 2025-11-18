using System.Text.RegularExpressions;

namespace TuneBridge.Common;

/// <summary>
/// Provides compiled regex patterns for detecting music links from supported providers.
/// These patterns are shared across all TuneBridge projects to ensure consistency.
/// </summary>
public static partial class MusicLinkPatterns {

    /// <summary>Regex pattern for validating HTTPS links.</summary>
    public static Regex ValidLink { get; } = ValidHttpsLink( );

    /// <summary>
    /// Regex pattern for detecting Apple Music links (music.apple.com).
    /// </summary>
    public static Regex AppleMusic { get; } = AppleMusicPattern( );

    /// <summary>
    /// Regex pattern for detecting Spotify links (open.spotify.com).
    /// </summary>
    public static Regex Spotify { get; } = SpotifyPattern( );

    /// <summary>
    /// Regex pattern for detecting Spotify short links (spotify.link).
    /// </summary>
    public static Regex SpotifyShortLink { get; } = SpotifyShortLinkPattern( );

    /// <summary>
    /// Regex pattern for detecting Tidal links (tidal.com or listen.tidal.com).
    /// </summary>
    public static Regex Tidal { get; } = TidalPattern( );

    [GeneratedRegex( @"[Hh][Tt]{2}[Pp][Ss]:\/\/(?<Link>\w[\w\/\=\?\.\:\-%&]*)" )]
    private static partial Regex ValidHttpsLink( );

    [GeneratedRegex( @"https?://music\.apple\.com/[_\w\d\/\=\?\.\:\-%&]*", RegexOptions.IgnoreCase | RegexOptions.Compiled )]
    private static partial Regex AppleMusicPattern( );

    [GeneratedRegex( @"https?://open\.spotify\.com/(?:track|album|prerelease)/[A-Za-z0-9]+", RegexOptions.IgnoreCase | RegexOptions.Compiled )]
    private static partial Regex SpotifyPattern( );

    [GeneratedRegex( @"https?://spotify\.link/[A-Za-z0-9]+", RegexOptions.IgnoreCase | RegexOptions.Compiled )]
    private static partial Regex SpotifyShortLinkPattern( );

    [GeneratedRegex( @"https?://(?:listen\.)?tidal\.com/(?:browse/)?(?:track|album)/\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled )]
    private static partial Regex TidalPattern( );
}
