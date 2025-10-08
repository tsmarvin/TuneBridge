namespace TuneBridge.Domain.Contracts.DTOs {

    /// <summary>
    /// The music lookup result data transfer object.
    /// Used to transfer music metadata between services and layers.
    /// </summary>
    public sealed class MusicLookupResultDto {

        /// <summary>
        /// The artist name of the track or album.
        /// </summary>
        public string Artist { get; set; } = string.Empty;

        /// <summary>
        /// The title of the track or album.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// The external identifier (ISRC for tracks, UPC for albums) from the music provider.
        /// </summary>
        public string ExternalId { get; set; } = string.Empty;

        /// <summary>
        /// The URL to the track or album on the music provider's platform.
        /// </summary>
        public string URL { get; set; } = string.Empty;

        /// <summary>
        /// The URL to the album or track artwork image.
        /// </summary>
        public string ArtUrl { get; set; } = string.Empty;

        /// <summary>
        /// The market region/storefront for this result (e.g., "us").
        /// </summary>
        public string MarketRegion { get; set; } = "us";

        /// <summary>
        /// Indicates whether this result represents an album (true) or a track (false). Null if unknown.
        /// </summary>
        public bool? IsAlbum { get; set; }

        /// <summary>
        /// Indicates whether this is the primary result (from the service that was queried directly).
        /// Doesn't count for equality as it's arbitrary (based on which service was queried first).
        /// </summary>
        internal bool IsPrimary { get; set; }

        public override bool Equals( object? obj ) {
            if (
                obj is not null &&
                obj.GetType( ) == typeof( MusicLookupResultDto )
            ) {
                MusicLookupResultDto objCast = (MusicLookupResultDto)obj;
                return
                    objCast.ExternalId == ExternalId &&
                    objCast.Artist == Artist &&
                    objCast.Title == Title &&
                    objCast.URL == URL &&
                    objCast.ArtUrl == ArtUrl &&
                    objCast.MarketRegion == MarketRegion &&
                    objCast.IsAlbum == IsAlbum;
            }
            return false;
        }

        public override int GetHashCode( ) {
            return Artist.GetHashCode( ) +
            Title.GetHashCode( ) +
            ExternalId.GetHashCode( ) +
            URL.GetHashCode( ) +
            ArtUrl.GetHashCode( ) +
            MarketRegion.GetHashCode( ) +
            IsAlbum.GetHashCode( );
        }
    }

}
