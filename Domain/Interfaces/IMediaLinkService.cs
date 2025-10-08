using TuneBridge.Domain.Contracts.DTOs;

namespace TuneBridge.Domain.Interfaces {
    /// <summary>
    /// Aggregates music metadata from multiple streaming providers (Apple Music, Spotify) and returns
    /// unified results. This service handles cross-platform lookups, deduplication, and link extraction
    /// to help users find the same song or album across different music services.
    /// </summary>
    /// <remarks>
    /// The service queries all enabled providers in parallel and combines matching results based on
    /// external IDs (ISRC/UPC) to ensure the same track/album is identified across platforms.
    /// </remarks>
    public interface IMediaLinkService {
        /// <summary>
        /// Searches for a track or album by title and artist name across all configured music providers.
        /// Results are deduplicated when the same content is found on multiple platforms.
        /// </summary>
        /// <param name="title">The track or album title to search for.</param>
        /// <param name="artist">The primary artist name. Should match the main credited artist for best results.</param>
        /// <returns>
        /// A <see cref="MediaLinkResult"/> containing URLs for each provider where the content was found,
        /// or null if no matches were found on any platform. The result includes metadata like artwork,
        /// external IDs (ISRC/UPC), and market region information.
        /// </returns>
        Task<MediaLinkResult?> GetInfoAsync( string title, string artist );

        /// <summary>
        /// Performs an exact lookup of a track using its ISRC (International Standard Recording Code),
        /// a globally unique identifier for sound recordings. This is the most reliable way to match
        /// tracks across platforms as ISRCs are standardized and consistent.
        /// </summary>
        /// <param name="isrc">
        /// The 12-character ISRC code. Hyphens are optional and will be handled automatically.
        /// </param>
        /// <returns>
        /// A <see cref="MediaLinkResult"/> with URLs from providers that have this specific recording,
        /// or null if the ISRC is not found in any provider's catalog.
        /// </returns>
        Task<MediaLinkResult?> GetInfoByISRCAsync( string isrc );

        /// <summary>
        /// Performs an exact lookup of an album using its UPC (Universal Product Code), which uniquely
        /// identifies album releases. UPC lookups are more reliable than title searches for albums
        /// with special editions, deluxe versions, or international releases.
        /// </summary>
        /// <param name="upc">
        /// The UPC barcode number (typically 12-13 digits).
        /// Leading zeros should be preserved for accurate matching.
        /// </param>
        /// <returns>
        /// A <see cref="MediaLinkResult"/> with URLs from providers that carry this album release,
        /// or null if the UPC is not recognized by any configured provider.
        /// </returns>
        Task<MediaLinkResult?> GetInfoByUPCAsync( string upc );

        /// <summary>
        /// Parses text content for Apple Music and Spotify URLs, extracts track/album information from
        /// each link, and returns unified results with cross-platform matches. This is the primary method
        /// used by the Discord bot to process user-shared links and reply with multi-platform URLs.
        /// </summary>
        /// <param name="content">
        /// Free-form text that may contain one or more music service URLs. The method will automatically
        /// detect and extract supported URL patterns (music.apple.com/*, open.spotify.com/*).
        /// </param>
        /// <returns>
        /// An async enumerable yielding one <see cref="MediaLinkResult"/> per unique track/album found.
        /// Results are streamed as they're discovered, allowing for progressive processing. Each result
        /// contains the original URL plus equivalent URLs from other providers when available.
        /// </returns>
        /// <remarks>
        /// The method deduplicates multiple links to the same content and handles regional variations
        /// of URLs (e.g., music.apple.com/us/* vs music.apple.com/gb/*).
        /// </remarks>
        IAsyncEnumerable<MediaLinkResult> GetInfoAsync( string content );
    }
}
