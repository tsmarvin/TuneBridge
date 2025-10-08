using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using NetCord;
using NetCord.Rest;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Domain.Implementations.Extensions {

    internal static class AppExtensions {
        public static IEnumerable<string> GetGroupValues( this Regex regex, string input, string groupName ) {
            foreach (Match match in regex.Matches( input )) {
                if (match.Groups.ContainsKey( groupName )) {
                    yield return match.Groups[groupName].Value;
                }
            }
        }

        #region ToDiscordMessageProperties

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

        private static Color GetPrimaryProviderColor( SupportedProviders provider )
            => provider switch {
                SupportedProviders.AppleMusic => new( 0, 0, 255 ),
                SupportedProviders.Spotify => new( 0, 153, 0 ),
                _ => new( 100, 100, 100 ),
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

        #endregion ToDiscordMessageProperties

    }

}
