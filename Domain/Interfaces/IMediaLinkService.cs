using TuneBridge.Domain.Contracts.DTOs;

namespace TuneBridge.Domain.Interfaces {
    /// <summary>
    /// Service for looking up and combining music information across multiple providers.
    /// </summary>
    public interface IMediaLinkService {
        /// <summary>
        /// Looks up track or album information by title and artist across all enabled providers.
        /// </summary>
        /// <param name="title">The title of the track or album.</param>
        /// <param name="artist">The artist name.</param>
        /// <returns>A combined result from all providers that found a match, or null if no matches were found.</returns>
        Task<MediaLinkResult?> GetInfoAsync( string title, string artist );

        /// <summary>
        /// Looks up track information by ISRC (International Standard Recording Code) across all enabled providers.
        /// </summary>
        /// <param name="isrc">The ISRC code of the track.</param>
        /// <returns>A combined result from all providers that found a match, or null if no matches were found.</returns>
        Task<MediaLinkResult?> GetInfoByISRCAsync( string isrc );

        /// <summary>
        /// Looks up album information by UPC (Universal Product Code) across all enabled providers.
        /// </summary>
        /// <param name="upc">The UPC code of the album.</param>
        /// <returns>A combined result from all providers that found a match, or null if no matches were found.</returns>
        Task<MediaLinkResult?> GetInfoByUPCAsync( string upc );

        /// <summary>
        /// Extracts music links from text content and looks up information for each link across all enabled providers.
        /// </summary>
        /// <param name="content">The text content containing music service URLs.</param>
        /// <returns>An async enumerable of combined results, one for each unique track or album found.</returns>
        IAsyncEnumerable<MediaLinkResult> GetInfoAsync( string content );
    }
}
