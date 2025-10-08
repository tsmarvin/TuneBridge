namespace TuneBridge.Domain.Contracts.DTOs {

    /// <summary>
    /// The music lookup result data transfer object.
    /// Used to transfer music metadata between services and layers.
    /// </summary>
    public sealed class MusicLookupResultDto {

        public string Artist { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ExternalId { get; set; } = string.Empty;
        public string URL { get; set; } = string.Empty;
        public string ArtUrl { get; set; } = string.Empty;
        public string MarketRegion { get; set; } = "us";
        public bool? IsAlbum { get; set; }

        // Doesn't count for equality as its arbitrary (based on which service was queried first)
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
