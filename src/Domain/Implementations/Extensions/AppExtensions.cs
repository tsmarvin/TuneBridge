using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;
using NetCord;
using NetCord.Rest;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Domain.Implementations.Extensions {

    /// <summary>
    /// Extension methods for application-specific functionality.
    /// </summary>
    internal static class AppExtensions {
        /// <summary>
        /// Extracts all values of a named group from regex matches.
        /// </summary>
        /// <param name="regex">The regex to match against.</param>
        /// <param name="input">The input string to search.</param>
        /// <param name="groupName">The name of the group to extract.</param>
        /// <returns>An enumerable of all group values found.</returns>
        public static IEnumerable<string> GetGroupValues( this Regex regex, string input, string groupName ) {
            foreach (Match match in regex.Matches( input )) {
                if (match.Groups.ContainsKey( groupName )) {
                    yield return match.Groups[groupName].Value;
                }
            }
        }

        /// <summary>
        /// Gets the hex color code associated with a supported provider.
        /// </summary>
        /// <param name="provider">The provider to get the color for.</param>
        /// <param name="darkmode">Whether to get the dark mode color.</param>
        /// <returns>The hex color code for the provider.</returns>
        public static string GetProviderHexColor( this SupportedProviders provider, bool darkmode = false )
            => provider switch {
                SupportedProviders.AppleMusic => "#D60017",
                SupportedProviders.Spotify => "#1ED760",
                SupportedProviders.Tidal => darkmode ? "#FFFFFF" : "#000000",
                _ => "#6366F1"
            };

        private static Color GetPrimaryProviderColor( SupportedProviders provider )
            => provider switch {
                SupportedProviders.AppleMusic => new( 214, 0, 23 ), // #D60017
                SupportedProviders.Spotify => new( 30, 215, 96 ),   // #1ED760
                SupportedProviders.Tidal => new( 255, 255, 255 ),   // #FFFFFF
                _ => new( 99, 102, 241 ),                           // #6366F1 (purple)
            };

        public static IHtmlContent GetProviderLogo( this SupportedProviders provider ) {
            // Dark backgrounds need LIGHT logos, light backgrounds need DARK logos
            string darkThemeLogo = provider switch {
                SupportedProviders.AppleMusic => "/public/providerIcons/US-UK_Apple_Music_Listen_on_Lockup_RGB_wht_072720.svg",
                SupportedProviders.Spotify => "/public/providerIcons/Spotify_Full_Logo_Green_RGB.svg",
                SupportedProviders.Tidal => "/public/providerIcons/tidal-horizontal-white-cmyk.png",
                _ => "🎧"
            };

            string lightThemeLogo = provider switch {
                SupportedProviders.AppleMusic => "/public/providerIcons/US-UK_Apple_Music_Listen_on_Lockup_RGB_blk_072720.svg",
                SupportedProviders.Spotify => "/public/providerIcons/Spotify_Full_Logo_Green_CMYK.svg",
                SupportedProviders.Tidal => "/public/providerIcons/tidal-horizontal-black-cmyk.png",
                _ => "🎧"
            };
            string alt = provider.GetDescription() + " logo";

            // Return both images with classes for CSS-based theme switching
            return new HtmlString(
                $@"<img src=""{lightThemeLogo}"" alt=""{alt}"" class=""provider-logo provider-logo-light"" />" +
                $@"<img src=""{darkThemeLogo}"" alt=""{alt}"" class=""provider-logo provider-logo-dark"" />"
            );
        }

        public static IHtmlContent GetProviderIcon( this SupportedProviders provider ) {
            // Dark backgrounds need LIGHT logos, light backgrounds need DARK logos
            string darkThemeIcon = provider switch {
                SupportedProviders.AppleMusic => "/public/providerIcons/Apple_Music_Icon_RGB_sm_073120.svg",
                SupportedProviders.Spotify => "/public/providerIcons/Spotify_Primary_Logo_Green_RGB.svg",
                SupportedProviders.Tidal => "/public/providerIcons/tidal-icon-white-cmyk.png",
                _ => "🎧"
            };

            string lightThemeIcon = provider switch {
                SupportedProviders.AppleMusic => "/public/providerIcons/Apple_Music_Icon_RGB_sm_073120.svg",
                SupportedProviders.Spotify => "/public/providerIcons/Spotify_Primary_Logo_Green_CMYK.svg",
                SupportedProviders.Tidal => "/public/providerIcons/tidal-icon-black-cmyk.png",
                _ => "🎧"
            };
            string alt = provider.GetDescription() + " icon";

            // Return both images with classes for CSS-based theme switching
            return new HtmlString(
                $@"<img src=""{lightThemeIcon}"" alt=""{alt}"" class=""provider-icon provider-icon-light"" />" +
                $@"<img src=""{darkThemeIcon}"" alt=""{alt}"" class=""provider-icon provider-icon-dark"" />"
            );
        }

        #region ToDiscordMessageProperties

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

        /// <summary>
        /// Gets the description attribute value from an enum, or returns the enum's string representation if no description exists.
        /// </summary>
        /// <typeparam name="T">The enum type.</typeparam>
        /// <param name="enumValue">The enum value.</param>
        /// <returns>The description attribute value or the enum's string representation.</returns>
        /// <exception cref="ArgumentException">Thrown if T is not an enum type.</exception>
        public static string GetDescription<T>( this T enumValue )
            where T : struct {
            Type type = enumValue.GetType();
            if (type.IsEnum == false) {
                throw new ArgumentException( "Must be an Enum!", nameof( enumValue ) );
            }

            //Tries to find a DescriptionAttribute for a potential friendly name
            //for the enum
            string? value = enumValue.ToString( );
            if (value != null) {
                MemberInfo[] memberInfo = type.GetMember(value);
                if (memberInfo.Length > 0) {
                    object[] attrs = memberInfo[0].GetCustomAttributes(typeof(DescriptionAttribute), false);

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
