using System.ComponentModel;
using System.Reflection;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using NetCord;
using NetCord.Rest;

namespace BridgeBeats.Worker.Discord.Extensions {

    /// <summary>
    /// Formatting helpers that turn a cross-provider <see cref="MediaLinkResult"/> into the Discord
    /// message and embed shape the bot posts back to a channel, plus a small helper for reading
    /// <see cref="DescriptionAttribute"/> values off enum members.
    /// </summary>
    internal static class DiscordMessageExtensions {

        /// <summary>
        /// Builds a Discord message whose embed links to a shareable card URL. The embed lists each
        /// provider's link as a field, uses the first available cover art and the first provider's
        /// accent color, and appends the item's external id (UPC for albums, ISRC for tracks) when
        /// present.
        /// </summary>
        /// <param name="result">The cross-provider lookup result to render.</param>
        /// <param name="userId">The id of the user to credit in the message content.</param>
        /// <param name="cardUrl">The shareable card URL to attach to the embed title.</param>
        /// <returns>The composed Discord message properties, with user mentions suppressed.</returns>
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
        /// Builds a Discord message embed for a cross-provider result without a shareable card URL.
        /// Behaves like <see cref="ToDiscordMessagePropertiesWithCardUrl"/> but omits the title link;
        /// used when the card service is disabled or storing the card failed.
        /// </summary>
        /// <param name="result">The cross-provider lookup result to render.</param>
        /// <param name="userId">The id of the user to credit in the message content.</param>
        /// <returns>The composed Discord message properties, with user mentions suppressed.</returns>
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
        /// Returns the <see cref="DescriptionAttribute"/> text applied to an enum member, falling
        /// back to the member name when no description is present. Used to render provider names in
        /// embeds.
        /// </summary>
        /// <typeparam name="T">The enum type whose member is described. Must be a value type.</typeparam>
        /// <param name="enumValue">The enum value to describe.</param>
        /// <returns>
        /// The member's description text; otherwise its name, or an empty string when the name cannot
        /// be resolved.
        /// </returns>
        /// <exception cref="ArgumentException">Thrown when <typeparamref name="T"/> is not an enum type.</exception>
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

        /// <summary>The description-line label prefixing an album's UPC.</summary>
        private const string AlbumExternalMediaPrefix = "UPC: ";

        /// <summary>The description-line label prefixing a track's ISRC.</summary>
        private const string SongExternalMediaPrefix = "ISRC: ";

        /// <summary>The title label used when the item type is unknown.</summary>
        private const string TitlePrefix = "Title: ";

        /// <summary>The title label used when the item is an album.</summary>
        private const string AlbumPrefix = "Album: ";

        /// <summary>The title label used when the item is a track.</summary>
        private const string SongPrefix = "Song: ";

        /// <summary>
        /// Prefixes the title with the label that matches the item type: album, song, or a generic
        /// title label when the type is unknown.
        /// </summary>
        /// <param name="isAlbum"><see langword="true"/> for an album, <see langword="false"/> for a track, <see langword="null"/> when unknown.</param>
        /// <param name="title">The raw title text.</param>
        /// <returns>The labeled title string.</returns>
        private static string GetTitle( bool? isAlbum, string title ) {
            return (isAlbum == null
                ? TitlePrefix
                : (bool)isAlbum
                    ? AlbumPrefix
                    : SongPrefix
            ) + title;
        }

        /// <summary>
        /// Returns the brand color used for a provider's embed accent, falling back to a default
        /// color for providers without a dedicated brand color.
        /// </summary>
        /// <param name="provider">The provider whose accent color is requested.</param>
        /// <returns>The embed accent <see cref="Color"/> for the provider.</returns>
        private static Color GetPrimaryProviderColor( SupportedProviders provider )
            => provider switch {
                SupportedProviders.AppleMusic => new( 214, 0, 23 ), // #D60017
                SupportedProviders.Spotify => new( 30, 215, 96 ),   // #1ED760
                SupportedProviders.Tidal => new( 255, 255, 255 ),   // #FFFFFF
                _ => new( 99, 102, 241 ),                           // #6366F1 (purple)
            };

        /// <summary>
        /// Assembles the final Discord message and embed (without a title link) from the prepared
        /// fields, crediting the sharing user and suppressing mentions.
        /// </summary>
        /// <param name="title">The embed title.</param>
        /// <param name="image">The embed image URL.</param>
        /// <param name="desc">The embed description text.</param>
        /// <param name="embedColor">The embed accent color.</param>
        /// <param name="fieldProps">The per-provider link fields.</param>
        /// <param name="userId">The id of the user credited in the message content.</param>
        /// <returns>The composed message properties.</returns>
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

        /// <summary>
        /// Assembles the final Discord message and embed with a title link (the shareable card URL)
        /// from the prepared fields, crediting the sharing user and suppressing mentions.
        /// </summary>
        /// <param name="title">The embed title.</param>
        /// <param name="image">The embed image URL.</param>
        /// <param name="desc">The embed description text.</param>
        /// <param name="embedColor">The embed accent color.</param>
        /// <param name="fieldProps">The per-provider link fields.</param>
        /// <param name="userId">The id of the user credited in the message content.</param>
        /// <param name="url">The card URL applied to the embed title link.</param>
        /// <returns>The composed message properties.</returns>
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
