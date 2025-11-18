using NetCord;
using NetCord.Rest;
using TuneBridge.Common;
using TuneBridge.Common.Contracts.DTOs;
using TuneBridge.Common.Contracts.Enums;

namespace TuneBridge.Core.Domain.Implementations.Extensions {

    /// <summary>
    /// Extension methods for application-specific functionality.
    /// </summary>
    public static class DiscordExtensions {

        #region ToDiscordMessageProperties

        /// <summary>
        /// Converts a media link result to Discord message properties with an OpenGraph card URL.
        /// </summary>
        /// <param name="result">The media link result to convert.</param>
        /// <param name="userId">The Discord user ID who shared the link.</param>
        /// <param name="cardUrl">The OpenGraph card URL to link the title and image to.</param>
        /// <returns>Discord message properties with formatted embeds and card URL.</returns>
        public static MessageProperties ToDiscordMessagePropertiesWithCardUrl(
            this MediaLinkResult result,
            ulong userId,
            string cardUrl
        ) {
            string title = string.Empty;
            string image = string.Empty;
            string externalId = string.Empty;
            bool isAlbum = false;
            string desc = "Artist: ";
            Color embedColor = new( 100, 100, 100 );
            List<EmbedFieldProperties> fieldProps = [];

            int count = result.Results.Count;
            bool hasPrimary = false;
            foreach ((SupportedProviders provider, MusicLookupResultDto dto) in result.Results.OrderBy( kv => kv.Key )) {
                fieldProps.Add( new EmbedFieldProperties( ) {
                    Value = $"[{provider.GetDescription( )}]({dto.URL})",
                    Inline = true
                } );

                if (string.IsNullOrWhiteSpace( dto.ExternalId ) == false) {
                    externalId = dto.ExternalId;
                }

                if (string.IsNullOrWhiteSpace( image ) && string.IsNullOrWhiteSpace( dto.ArtUrl ) == false) {
                    image = dto.ArtUrl;
                }

                if (string.IsNullOrWhiteSpace( title )) {
                    title = GetTitle( dto.IsAlbum, dto.Title );
                }

                if (dto.IsPrimary) {
                    title = GetTitle( dto.IsAlbum, dto.Title );
                    isAlbum = dto.IsAlbum ?? false;
                    desc += dto.Artist;
                    embedColor = GetPrimaryProviderColor( provider );
                    hasPrimary = true;
                }

                count--;
                if (count == 0 && hasPrimary == false) {
                    isAlbum = dto.IsAlbum ?? false;
                    desc += dto.Artist;
                }
            }

            if (string.IsNullOrWhiteSpace( externalId ) == false) {
                desc += "\n" + (isAlbum ? _albumExternalMediaPrefix : _songExternalMediaPrefix) + externalId;
            }

            return NewMessagePropertiesWithUrl( title, image, desc, embedColor, fieldProps, userId, cardUrl );
        }

        /// <summary>
        /// Converts a media link result to Discord message properties for display in Discord.
        /// </summary>
        /// <param name="result">The media link result to convert.</param>
        /// <param name="userId">The Discord user ID who shared the link.</param>
        /// <returns>Discord message properties with formatted embeds.</returns>
        public static MessageProperties ToDiscordMessageProperties( this MediaLinkResult result, ulong userId ) {
            string title = string.Empty;
            string image = string.Empty;
            string externalId = string.Empty;
            bool isAlbum = false;
            string desc = "Artist: ";
            Color embedColor = new( 100, 100, 100 );
            List<EmbedFieldProperties> fieldProps = [];

            int count = result.Results.Count;
            bool hasPrimary = false;
            foreach ((SupportedProviders provider, MusicLookupResultDto dto) in result.Results.OrderBy( kv => kv.Key )) {
                fieldProps.Add( new EmbedFieldProperties( ) {
                    Value = $"[{provider.GetDescription( )}]({dto.URL})",
                    Inline = true
                } );

                if (string.IsNullOrWhiteSpace( dto.ExternalId ) == false) {
                    externalId = dto.ExternalId;
                }

                if (string.IsNullOrWhiteSpace( image ) && string.IsNullOrWhiteSpace( dto.ArtUrl ) == false) {
                    image = dto.ArtUrl;
                }

                if (string.IsNullOrWhiteSpace( title )) {
                    title = GetTitle( dto.IsAlbum, dto.Title );
                }

                if (dto.IsPrimary) {
                    title = GetTitle( dto.IsAlbum, dto.Title );
                    isAlbum = dto.IsAlbum ?? false;
                    desc += dto.Artist;
                    embedColor = GetPrimaryProviderColor( provider );
                    hasPrimary = true;
                }

                count--;
                if (count == 0 && hasPrimary == false) {
                    isAlbum = dto.IsAlbum ?? false;
                    desc += dto.Artist;
                }
            }

            if (string.IsNullOrWhiteSpace( externalId ) == false) {
                desc += "\n" + (isAlbum ? _albumExternalMediaPrefix : _songExternalMediaPrefix) + externalId;
            }

            return NewMessageProperties( title, image, desc, embedColor, fieldProps, userId );
        }

        private static Color GetPrimaryProviderColor( SupportedProviders provider )
            => provider switch {
                SupportedProviders.AppleMusic => new( 214, 0, 23 ),   // #D60017
                SupportedProviders.Spotify => new( 30, 215, 96 ),     // #1ED760
                SupportedProviders.Tidal => new( 255, 255, 255 ),     // #FFFFFF
                _ => new( 99, 102, 241 ),                             // #6366F1 (purple)
            };

        private const string _albumExternalMediaPrefix = "UPC: ";
        private const string _songExternalMediaPrefix = "ISRC: ";
        private const string _titlePrefix = "Title: ";
        private const string _albumPrefix = "Album: ";
        private const string _songPrefix = "Song: ";

        private static string GetTitle( bool? isAlbum, string title ) {
            return (isAlbum == null
                ? _titlePrefix
                : (bool)isAlbum
                    ? _albumPrefix
                    : _songPrefix
            ) + title;
        }

        private static MessageProperties NewMessageProperties(
            string title,
            string image,
            string desc,
            Color embedColor,
            List<EmbedFieldProperties> fieldProps,
            ulong userId
        ) {
            return new( ) {
                Content = $"<@{userId}> Shared:",
                Embeds = [
                new EmbedProperties {
                    Title       = title,
                    Image       = image,
                    Description = desc,
                    Color       = embedColor,
                    Fields      = fieldProps
                }
            ],
                AllowedMentions = AllowedMentionsProperties.None
            };
        }

        private static MessageProperties NewMessagePropertiesWithUrl(
            string title,
            string image,
            string desc,
            Color embedColor,
            List<EmbedFieldProperties> fieldProps,
            ulong userId,
            string url
        ) {
            return new( ) {
                Content = $"<@{userId}> Shared:",
                Embeds = [
                new EmbedProperties {
                    Title       = title,
                    Url         = url,
                    Image       = image,
                    Description = desc,
                    Color       = embedColor,
                    Fields      = fieldProps
                }
            ],
                AllowedMentions = AllowedMentionsProperties.None
            };
        }

        #endregion ToDiscordMessageProperties

    }

}
