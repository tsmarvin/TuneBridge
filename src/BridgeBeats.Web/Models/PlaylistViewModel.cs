using BridgeBeats.Contracts.DTOs;

namespace BridgeBeats.Web.Models {
    /// <summary>
    /// View model for a single playlist detail page, holding the playlist metadata, its items, the serving
    /// domain, and an optional QR code for sharing.
    /// </summary>
    public class PlaylistViewModel {
        /// <summary>
        /// The playlist identifier.
        /// </summary>
        public string PlaylistId { get; set; } = string.Empty;

        /// <summary>
        /// The playlist title.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// An optional playlist description.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// The items contained in the playlist.
        /// </summary>
        public List<PlaylistItemViewModel> Items { get; set; } = [];

        /// <summary>
        /// The domain the playlist is served from, used to build absolute links.
        /// </summary>
        public string Domain { get; set; } = string.Empty;

        /// <summary>
        /// A data URI for a QR code linking to the playlist, when one has been generated.
        /// </summary>
        public string? QrCodeDataUri { get; set; }
    }

    /// <summary>
    /// View model for a single item within a playlist, pairing the cross-provider media link with its
    /// card and display metadata.
    /// </summary>
    public class PlaylistItemViewModel {
        /// <summary>
        /// The identifier of the item's shareable card.
        /// </summary>
        public string CardId { get; set; } = string.Empty;

        /// <summary>
        /// The URL of the item's shareable card.
        /// </summary>
        public string CardUrl { get; set; } = string.Empty;

        /// <summary>
        /// The cross-provider media link result for this item.
        /// </summary>
        public MediaLinkResult Result { get; set; } = null!;

        /// <summary>
        /// The display title of the track or album.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// The display artist of the track or album.
        /// </summary>
        public string Artist { get; set; } = string.Empty;

        /// <summary>
        /// The URL of the item's artwork, when available.
        /// </summary>
        public string? ArtUrl { get; set; }

        /// <summary>
        /// A value indicating whether this item represents an album rather than a single track.
        /// </summary>
        public bool IsAlbum { get; set; }
    }
}
