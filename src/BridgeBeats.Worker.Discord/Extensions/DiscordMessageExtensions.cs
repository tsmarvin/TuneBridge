using System.ComponentModel;
using System.Reflection;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using NetCord;
using NetCord.Rest;

namespace BridgeBeats.Worker.Discord.Extensions {

    /// <summary>
    /// Extension methods for converting media link results to Discord message properties.
    /// </summary>
    internal static class DiscordMessageExtensions {

        /// <summary>
        /// Converts a media link result to Discord message properties with an OpenGraph card URL.
        /// </summary>
        /// <param name="result">The media link result to convert.</param>
        /// <param name="userId">The Discord user ID who shared the link.</param>
        /// <param name="cardUrl">The OpenGraph card URL to link the title and image to.</param>
        /// <returns>Discord message properties with formatted embeds and card URL.</returns>
        public static MessageProperties ToDiscordMessagePropertiesWithCardUrl( this MediaLinkResult result, ulong userId, string cardUrl ) {
            string title = string.Empty;
            string image = string.Empty;
            string externalId = string.Empty;
            bool isAlbum = false;
            string desc = "Artist: ";
            Color embedColor = new( 100, 100, 100 );
            List<EmbedFieldProperties> fieldProps = [];

            // Use the first result as primary (order is preserved from API)
            bool isFirst = true;
            foreach ((SupportedProviders provider, MusicLookupResult dto) in result.Results.OrderBy( kv => kv.Key )) {
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

                // Treat the first result as primary for display purposes
                if (isFirst) {
                    title = GetTitle( dto.IsAlbum, dto.Title );
                    isAlbum = dto.IsAlbum ?? false;
                    desc += dto.Artist;
                    embedColor = GetPrimaryProviderColor( provider );
                    isFirst = false;
                }
            }

            if (string.IsNullOrWhiteSpace( externalId ) == false) {
                desc += "\n" + (isAlbum ? AlbumExternalMediaPrefix : SongExternalMediaPrefix) + externalId;
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

            // Use the first result as primary (order is preserved from API)
            bool isFirst = true;
            foreach ((SupportedProviders provider, MusicLookupResult dto) in result.Results.OrderBy( kv => kv.Key )) {
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

                // Treat the first result as primary for display purposes
                if (isFirst) {
                    title = GetTitle( dto.IsAlbum, dto.Title );
                    isAlbum = dto.IsAlbum ?? false;
                    desc += dto.Artist;
                    embedColor = GetPrimaryProviderColor( provider );
                    isFirst = false;
                }
            }

            if (string.IsNullOrWhiteSpace( externalId ) == false) {
                desc += "\n" + (isAlbum ? AlbumExternalMediaPrefix : SongExternalMediaPrefix) + externalId;
            }

            return NewMessageProperties( title, image, desc, embedColor, fieldProps, userId );
        }

        /// <summary>
        /// Gets the description attribute value from an enum, or returns the enum's string representation if no description exists.
        /// </summary>
        /// <typeparam name="T">The enum type.</typeparam>
        /// <param name="enumValue">The enum value.</param>
        /// <returns>The description attribute value or the enum's string representation.</returns>
        /// <exception cref="ArgumentException">Thrown if T is not an enum type.</exception>
        public static string GetDescription<T>( this T enumValue )
            where T : struct {
            Type type = enumValue.GetType( );
            if (type.IsEnum == false) {
                throw new ArgumentException( "Must be an Enum!", nameof( enumValue ) );
            }

            //Tries to find a DescriptionAttribute for a potential friendly name
            //for the enum
            string? value = enumValue.ToString( );
            if (value != null) {
                MemberInfo[] memberInfo = type.GetMember( value );
                if (memberInfo.Length > 0) {
                    object[] attrs = memberInfo[0].GetCustomAttributes( typeof( DescriptionAttribute ), false );

                    if (attrs != null && attrs.Length > 0) {
                        //Pull out the description value
                        return ((DescriptionAttribute)attrs[0]).Description;
                    }
                }
                return value;
            }
            //If we have no description attribute, just return the ToString of the enum
            return string.Empty;
        }

        private const string AlbumExternalMediaPrefix = "UPC: ";
        private const string SongExternalMediaPrefix = "ISRC: ";
        private const string TitlePrefix = "Title: ";
        private const string AlbumPrefix = "Album: ";
        private const string SongPrefix = "Song: ";

        private static string GetTitle( bool? isAlbum, string title ) {
            return (isAlbum == null
                ? TitlePrefix
                : (bool)isAlbum
                    ? AlbumPrefix
                    : SongPrefix
            ) + title;
        }

        private static Color GetPrimaryProviderColor( SupportedProviders provider )
            => provider switch {
                SupportedProviders.AppleMusic => new( 214, 0, 23 ), // #D60017
                SupportedProviders.Spotify => new( 30, 215, 96 ),   // #1ED760
                SupportedProviders.Tidal => new( 255, 255, 255 ),   // #FFFFFF
                _ => new( 99, 102, 241 ),                           // #6366F1 (purple)
            };

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
    }

}
