using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using BridgeBeats.Contracts.Enums;
using Microsoft.AspNetCore.Html;

namespace BridgeBeats.Services.Extensions {

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

    }

}
