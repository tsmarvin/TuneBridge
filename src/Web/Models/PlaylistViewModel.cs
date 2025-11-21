using TuneBridge.Domain.Contracts.DTOs;

namespace TuneBridge.Web.Models {

    /// <summary>
    /// View model for displaying a playlist.
    /// </summary>
    public class PlaylistViewModel {

        /// <summary>
        /// The unique identifier for the playlist.
        /// </summary>
        public string PlaylistId { get; set; } = string.Empty;

        /// <summary>
        /// Title of the playlist.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Optional description of the playlist.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// List of items in the playlist.
        /// </summary>
        public List<PlaylistItemViewModel> Items { get; set; } = [];

        /// <summary>
        /// The base URL for the application.
        /// </summary>
        public string BaseUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// View model for a single item in a playlist.
    /// </summary>
    public class PlaylistItemViewModel {

        /// <summary>
        /// The card ID (rkey) for this item.
        /// </summary>
        public string CardId { get; set; } = string.Empty;

        /// <summary>
        /// URL to the individual card.
        /// </summary>
        public string CardUrl { get; set; } = string.Empty;

        /// <summary>
        /// The full MediaLinkResult for this item.
        /// </summary>
        public MediaLinkResult Result { get; set; } = null!;

        /// <summary>
        /// Title of the track/album.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Artist name.
        /// </summary>
        public string Artist { get; set; } = string.Empty;

        /// <summary>
        /// URL to the album/track artwork.
        /// </summary>
        public string? ArtUrl { get; set; }

        /// <summary>
        /// Whether this is an album (true) or track (false).
        /// </summary>
        public bool IsAlbum { get; set; }
    }
}
