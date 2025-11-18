using System.Text.RegularExpressions;
using TuneBridge.Common;

namespace TuneBridge.JetStreamMonitor.Domain;

/// <summary>
/// Service for detecting music links from supported providers (Spotify, Apple Music, Tidal) in text.
/// Uses shared regex patterns from TuneBridge.Shared to ensure consistency across all projects.
/// </summary>
public partial class MusicLinkDetector {

    /// <summary>
    /// Detects music links in the provided text and returns all matching URLs.
    /// </summary>
    /// <param name="text">The text to scan for music links.</param>
    /// <returns>A list of detected music link URLs with their provider type.</returns>
    internal static List<(string provider, string url)> DetectMusicLinks( string text ) {
        List<(string provider, string url)> detectedLinks = [];
        // Detect Apple Music links
        MatchCollection appleMusicMatches = MusicLinkPatterns.AppleMusic.Matches( text );
        foreach (Match match in appleMusicMatches) {
            detectedLinks.Add( ("Apple Music", match.Value) );
        }

        // Detect Spotify links (both regular and short links)
        MatchCollection spotifyMatches = MusicLinkPatterns.Spotify.Matches( text );
        foreach (Match match in spotifyMatches) {
            detectedLinks.Add( ("Spotify", match.Value) );
        }

        MatchCollection spotifyShortMatches = MusicLinkPatterns.SpotifyShortLink.Matches( text );
        foreach (Match match in spotifyShortMatches) {
            detectedLinks.Add( ("Spotify", match.Value) );
        }

        // Detect Tidal links
        MatchCollection tidalMatches = MusicLinkPatterns.Tidal.Matches( text );
        foreach (Match match in tidalMatches) {
            detectedLinks.Add( ("Tidal", match.Value) );
        }

        return detectedLinks;
    }

    /// <summary>
    /// Checks if the text contains any music links from supported providers.
    /// </summary>
    /// <param name="text">The text to check.</param>
    /// <returns>True if at least one music link is detected, false otherwise.</returns>
    public static bool ContainsMusicLink( string text )
        => !string.IsNullOrWhiteSpace( text ) && MusicLinkPatterns.ValidLink.IsMatch( text ) && (
            MusicLinkPatterns.AppleMusic.IsMatch( text ) ||
            MusicLinkPatterns.Spotify.IsMatch( text ) ||
            MusicLinkPatterns.SpotifyShortLink.IsMatch( text ) ||
            MusicLinkPatterns.Tidal.IsMatch( text )
        );
}
