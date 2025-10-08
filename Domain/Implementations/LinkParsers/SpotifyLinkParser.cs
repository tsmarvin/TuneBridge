using System.Text.RegularExpressions;
using TuneBridge.Domain.Implementations.Extensions;
using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Domain.Implementations.LinkParsers {

    internal static partial class SpotifyLinkParser {

        public static bool TryParseUri(
            string link,
            out SpotifyEntity kind,
            out string id
        ) {
            kind = SpotifyEntity.Unknown;
            id = string.Empty;

            if (SpotifyLink.IsMatch( link )) {

                string? uri = SpotifyLink.GetGroupValues(link, "type").FirstOrDefault();
                if (uri != null) {
                    Match match = SpotifyLink.Match(link);

                    id = match.Groups["id"].Value;
                    kind = match.Groups["type"].Value.ToLowerInvariant( ) switch {
                        "track" => SpotifyEntity.Track,
                        "album" => SpotifyEntity.Album,
                        _ => SpotifyEntity.Unknown
                    };
                }
            }

            return kind != SpotifyEntity.Unknown && !string.IsNullOrEmpty( id );
        }

        private static readonly Regex SpotifyLink = SpotifyMusicLink();
        [GeneratedRegex( @"(?:open\.spotify\.com/)(?<type>track|album)/(?<id>[A-Za-z0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled )]
        private static partial Regex SpotifyMusicLink( );

        public static string GetArtistSearchUri( string artist )
            => ArtistsSearchURI.Replace( "{artist}", Uri.EscapeDataString( artist ) );

        public static string GetTracksIsrcURI( string isrc )
            => TracksIsrcURI.Replace( "{isrc}", isrc );

        public static string GetAlbumUpcURI( string upc )
            => AlbumsUpcURI.Replace( "{upc}", upc );

        public static string GetArtistAlbumsURI( string artistId )
            => ArtistAlbumsURI
                .Replace( "{id}", artistId );
        public static string GetAlbumTracksURI( string albumId )
            => AlbumTracksURI
                .Replace( "{id}", albumId );
        public static string GetAlbumIdURI( string albumId )
            => AlbumsURI
                .Replace( "{id}", albumId );
        public static string GetTrackIdURI( string trackId )
            => TracksURI
                .Replace( "{id}", trackId );


        private const string TracksIsrcURI = "search?q=isrc:{isrc}&type=track";
        private const string AlbumsUpcURI = "search?q=upc:{upc}&type=album";
        private const string ArtistsSearchURI = "search?q={artist}&type=artist";
        private const string ArtistAlbumsURI = "artists/{id}/albums";
        private const string AlbumTracksURI = "albums/{id}/tracks";
        private const string AlbumsURI = "albums/{id}";
        private const string TracksURI = "tracks/{id}";

    }
}
