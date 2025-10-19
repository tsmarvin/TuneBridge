using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Domain.Interfaces {

    /// <summary>
    /// The common interface for looking up music from a given provider (e.g., Apple Music, Spotify).
    /// </summary>
    public partial interface IMusicLookupService {

        /// <summary>
        /// The music provider that this service looks up information from.
        /// </summary>
        static SupportedProviders Provider { get; }

        /// <summary>
        /// Looks up track or album information by title and artist.
        /// </summary>
        /// <param name="title">The title of the track or album.</param>
        /// <param name="artist">The artist name.</param>
        /// <returns>Music lookup result with metadata, or null if not found.</returns>
        Task<MusicLookupResultDto?> GetInfoAsync( string title, string artist );

        /// <summary>
        /// Looks up track information by ISRC (International Standard Recording Code).
        /// </summary>
        /// <param name="isrc">The ISRC code of the track.</param>
        /// <returns>Music lookup result with metadata, or null if not found.</returns>
        Task<MusicLookupResultDto?> GetInfoByISRCAsync( string isrc );

        /// <summary>
        /// Looks up album information by UPC (Universal Product Code).
        /// </summary>
        /// <param name="upc">The UPC code of the album.</param>
        /// <returns>Music lookup result with metadata, or null if not found.</returns>
        Task<MusicLookupResultDto?> GetInfoByUPCAsync( string upc );

        /// <summary>
        /// Looks up track or album information from a provider-specific URI.
        /// </summary>
        /// <param name="uri">The music provider's URI (e.g., Spotify or Apple Music link).</param>
        /// <returns>Music lookup result with metadata, or null if not found or URI is invalid.</returns>
        Task<MusicLookupResultDto?> GetInfoAsync( string uri );

        /// <summary>
        /// Looks up additional information for a partial music lookup result.
        /// </summary>
        /// <param name="lookup">The partial lookup result to enhance with additional data.</param>
        /// <returns>Enhanced music lookup result with metadata, or null if not found.</returns>
        Task<MusicLookupResultDto?> GetInfoAsync( MusicLookupResultDto lookup );

    }
}
