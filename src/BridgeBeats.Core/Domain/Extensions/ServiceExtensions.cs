using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Domain.Providers.Spotify;
using BridgeBeats.Core.Domain.Services;
using BridgeBeats.Core.Domain.Services.Cards;
using BridgeBeats.Core.Domain.Services.LinkResolver;
using BridgeBeats.Core.Domain.Services.Queue;
using BridgeBeats.Core.Infrastructure.Extensions;
using BridgeBeats.Core.Infrastructure.Identity;
using BridgeBeats.Core.Infrastructure.Queue;
using BridgeBeats.Services.LinkResolver;
using Microsoft.AspNetCore.Html;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BridgeBeats.Core.Domain.Extensions;

/// <summary>
/// Extension methods for registering queue processor services in worker applications.
/// </summary>
public static class ServiceExtensions {

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

    #region Web Extensions

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


    /// <summary>
    /// Returns the HTML content for the provider logo, with support for light and dark themes.
    /// </summary>
    /// <param name="provider">The music provider to operate on.</param>
    /// <returns>The HTML content for the provider logo.</returns>
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

    /// <summary>
    /// Returns the HTML content for the provider icon, with support for light and dark themes.
    /// </summary>
    /// <param name="provider">The music provider to operate on.</param>
    /// <returns>The HTML content for the provider icon.</returns>
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

    #endregion Web Extensions


    #region Queue Processor Registration

    /// <summary>
    /// Adds the queue processor background service and related infrastructure to a provider worker.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method registers:
    /// <list type="bullet">
    ///   <item>Queue settings configuration</item>
    ///   <item>Provider-specific request queue</item>
    ///   <item>Shared queue infrastructure (deduplicator, rate limit tracker, saga manager)</item>
    ///   <item>Queue processor background service</item>
    /// </list>
    /// </para>
    /// <para>
    /// The lookup service must be registered separately before calling this method.
    /// </para>
    /// </remarks>
    /// <typeparam name="TLookupService">The concrete lookup service type for the provider.</typeparam>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="provider">The music provider this worker serves.</param>
    /// <param name="configureSettings">Optional action to configure queue settings.</param>
    /// <returns>The configured service collection.</returns>
    public static IServiceCollection AddQueueProcessor<TLookupService>(
        this IServiceCollection services,
        SupportedProviders provider,
        Action<QueueSettings>? configureSettings = null
    ) where TLookupService : class, IMusicLookupService {
        // Configure queue settings with defaults from the record definition
        // The QueueSettings record already has sensible defaults via init properties
        if (configureSettings is not null) {
            _ = services.Configure( configureSettings );
        }

        // Register shared queue infrastructure
        _ = services.AddQueueInfrastructure( );

        // Register the provider-specific queue via the single factory that owns the
        // "wrap with SpotifyBulkQueueDecorator if Spotify" conditional (AA-F1).
        _ = services.AddSingleton<IRequestQueue<QueuedLookupRequest>>(
            sp => QueueServiceExtensions.CreateProviderQueue( sp, provider ) );

        // Register the background service
        _ = services.AddHostedService( sp => new QueueProcessorBackgroundService(
            sp.GetRequiredService<IConnectionMultiplexer>( ),
            sp.GetRequiredService<IRequestQueue<QueuedLookupRequest>>( ),
            sp.GetRequiredService<IRateLimitTracker>( ),
            sp.GetRequiredService<ISagaStateManager>( ),
            sp.GetRequiredService<TLookupService>( ),
            provider,
            sp.GetRequiredService<ILogger<QueueProcessorBackgroundService>>( )
        ) );

        return services;
    }

    /// <summary>
    /// Adds the queue processor background service using a pre-resolved lookup service instance.
    /// </summary>
    /// <remarks>
    /// Use this overload when the lookup service is resolved via a factory or needs special handling.
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="provider">The music provider this worker serves.</param>
    /// <param name="lookupServiceFactory">Factory to create the lookup service instance.</param>
    /// <param name="configureSettings">Optional action to configure queue settings.</param>
    /// <returns>The configured service collection.</returns>
    public static IServiceCollection AddQueueProcessor(
        this IServiceCollection services,
        SupportedProviders provider,
        Func<IServiceProvider, IMusicLookupService> lookupServiceFactory,
        Action<QueueSettings>? configureSettings = null
    ) {
        // Configure queue settings with defaults from the record definition
        // The QueueSettings record already has sensible defaults via init properties
        if (configureSettings is not null) {
            _ = services.Configure( configureSettings );
        }

        // Register shared queue infrastructure
        _ = services.AddQueueInfrastructure( );

        // Register the provider-specific queue via the single factory that owns the
        // "wrap with SpotifyBulkQueueDecorator if Spotify" conditional (AA-F1).
        _ = services.AddSingleton<IRequestQueue<QueuedLookupRequest>>(
            sp => QueueServiceExtensions.CreateProviderQueue( sp, provider ) );

        // Register the background service using the factory
        _ = services.AddHostedService( sp => new QueueProcessorBackgroundService(
            sp.GetRequiredService<IConnectionMultiplexer>( ),
            sp.GetRequiredService<IRequestQueue<QueuedLookupRequest>>( ),
            sp.GetRequiredService<IRateLimitTracker>( ),
            sp.GetRequiredService<ISagaStateManager>( ),
            lookupServiceFactory( sp ),
            provider,
            sp.GetRequiredService<ILogger<QueueProcessorBackgroundService>>( )
        ) );

        return services;
    }

    #endregion Queue Processor Registration


    #region Media Link Resolver Registration

    /// <summary>
    /// Registers the media link resolver service (with optional caching).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="enabledProviders">Set of enabled music providers.</param>
    /// <param name="useCaching">Whether to use caching (requires ATProto configuration).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMediaLinkResolver(
        this IServiceCollection services,
        HashSet<SupportedProviders> enabledProviders,
        bool useCaching
    ) {
        if (useCaching) {
            // Register the lookup orchestrator
            _ = services.AddSingleton<ILookupOrchestrator>( s =>
                new LookupOrchestrator(
                    s.GetRequiredService<IMediaLinkCacheRepository>( ),
                    s.GetRequiredService<IRequestDeduplicator>( ),
                    s.GetRequiredService<ISagaStateManager>( ),
                    s.GetRequiredService<IProviderQueueResolver<QueuedLookupRequest>>( ),
                    s.GetRequiredService<IATProtoStorageService>( ),
                    enabledProviders,
                    s.GetRequiredService<ILogger<LookupOrchestrator>>( ),
                    s.GetService<IOptions<QueueSettings>>( )?.Value
                )
            );

            // Register CachingMediaLinkService that delegates to the orchestrator
            _ = services.AddTransient<IMediaLinkService>( s =>
                new CachingMediaLinkService(
                    s.GetRequiredService<ILookupOrchestrator>( ),
                    s.GetRequiredService<ILogger<CachingMediaLinkService>>( )
                )
            );
        } else {
            // Use default (non-caching) service
            _ = services.AddTransient<IMediaLinkService>( s => {
                Dictionary<SupportedProviders, IMusicLookupService> providerServices = GetEnabledProviderServices( enabledProviders, s );

                return new DefaultMediaLinkService(
                    providerServices,
                    s.GetRequiredService<ILogger<DefaultMediaLinkService>>( ),
                    s.GetRequiredService<JsonSerializerOptions>( )
                );
            } );
        }

        return services;
    }

    /// <summary>
    /// Creates a dictionary that maps enabled providers (by their enums designation) to their corresponding
    /// <see cref="IMusicLookupService"/> implementations.
    /// This is a helper method used during service registration to enable adding the
    /// <see cref="IMusicLookupService"/> implementations to the <see cref="DefaultMediaLinkService"/>.
    /// </summary>
    /// <param name="enabledProviders">The set of providers that have been enabled based on configuration.</param>
    /// <param name="serviceProvider">The service provider used to resolve service instances.</param>
    /// <returns>A dictionary of provider to service instances.</returns>
    private static Dictionary<SupportedProviders, IMusicLookupService> GetEnabledProviderServices(
        HashSet<SupportedProviders> enabledProviders,
        IServiceProvider serviceProvider
    ) {
        Dictionary<SupportedProviders, IMusicLookupService> results = [];
        foreach (SupportedProviders provider in enabledProviders) {
            switch (provider) {
                case SupportedProviders.AppleMusic:
                    results.Add( SupportedProviders.AppleMusic, serviceProvider.GetRequiredService<Providers.AppleMusic.AppleMusicLookupService>( ) );
                    break;
                case SupportedProviders.Spotify:
                    results.Add( SupportedProviders.Spotify, serviceProvider.GetRequiredService<Providers.Spotify.SpotifyLookupService>( ) );
                    break;
                case SupportedProviders.Tidal:
                    results.Add( SupportedProviders.Tidal, serviceProvider.GetRequiredService<Providers.Tidal.TidalLookupService>( ) );
                    break;
            }
        }
        return results;
    }

    #endregion Media Link Resolver Registration


    #region Card Services Registration

    /// <summary>
    /// Registers the OpenGraph card service and playlist service.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="domain">The base URL for card and playlist URLs.</param>
    /// <param name="cardCacheExpirationHours">Card cache expiration in hours.</param>
    /// <param name="cardCacheCleanupInterval">Card cache cleanup interval.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCardServices(
        this IServiceCollection services,
        string domain,
        int cardCacheExpirationHours,
        int cardCacheCleanupInterval
    ) {
        // Validate card cache settings
        if (cardCacheExpirationHours <= 0) {
            throw new InvalidOperationException( $"CardCacheExpirationHours must be greater than zero. Current value: {cardCacheExpirationHours}" );
        }
        if (cardCacheCleanupInterval <= 0) {
            throw new InvalidOperationException( $"CardCacheCleanupInterval must be greater than zero. Current value: {cardCacheCleanupInterval}" );
        }

        // OpenGraph card service
        _ = services.AddSingleton<IOpenGraphCardService, OpenGraphCardService>(
            _ => new OpenGraphCardService(
                domain,
                cardCacheExpirationHours,
                cardCacheCleanupInterval
            )
        );

        // Playlist service (singleton with DbContextFactory for thread-safe database access)
        _ = services.AddSingleton<IPlaylistService, PlaylistService>(
            p => new PlaylistService( domain, p.GetRequiredService<IDbContextFactory<ApplicationDbContext>>( ) )
        );

        // Register playlist cleanup background service
        _ = services.AddHostedService<PlaylistCleanupService>( );

        return services;
    }

    #endregion Card Services Registration


    #region QR Code Service Registration

    /// <summary>
    /// Registers the QR code generation service.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddQrCodeService( this IServiceCollection services ) {
        _ = services.AddSingleton<IQrCodeService, QrCodeService>( );

        return services;
    }

    #endregion QR Code Service Registration


    #region OpenGraph Extensions

    /// <summary>
    /// Converts a media link result to OpenGraph metadata properties.
    /// </summary>
    /// <param name="result">The media link result to convert.</param>
    /// <returns>A dictionary of OpenGraph meta tag properties.</returns>
    public static Dictionary<string, string> ToOpenGraphMetadata( this MediaLinkResult result ) {
        Dictionary<string, string> metadata = [];

        string title = string.Empty;
        string description = string.Empty;
        string image = string.Empty;
        bool isAlbum = false;
        string artist = string.Empty;
        string externalId = string.Empty;
        SupportedProviders? primaryProvider = null;

        // Extract information from results, prioritizing the primary result
        foreach ((SupportedProviders provider, MusicLookupResult dto) in result.Results.OrderBy( kv => kv.Key )) {

            if (string.IsNullOrWhiteSpace( image ) && !string.IsNullOrWhiteSpace( dto.ArtUrl )) {
                image = dto.ArtUrl;
            }

            if (string.IsNullOrWhiteSpace( externalId ) && !string.IsNullOrWhiteSpace( dto.ExternalId )) {
                externalId = dto.ExternalId;
            }

            if (string.IsNullOrWhiteSpace( title )) {
                title = dto.Title;
                isAlbum = dto.IsAlbum ?? false;
                artist = dto.Artist;
            }

            if (dto.IsPrimary) {
                title = dto.Title;
                isAlbum = dto.IsAlbum ?? false;
                artist = dto.Artist;
                primaryProvider = provider;
                if (string.IsNullOrWhiteSpace( dto.ArtUrl ) == false) {
                    image = dto.ArtUrl;
                }
                if (string.IsNullOrWhiteSpace( dto.ExternalId ) == false) {
                    externalId = dto.ExternalId;
                }
            }
        }

        // Build description with artist and ISRC/UPC (similar to old Discord embed format)
        description = $"Artist: {artist}";
        if (!string.IsNullOrWhiteSpace( externalId )) {
            string externalIdPrefix = isAlbum ? "UPC" : "ISRC";
            description += $"\n{externalIdPrefix}: {externalId}";
        }

        // Set OpenGraph properties
        metadata["og:type"] = "music." + (isAlbum ? "album" : "song");
        metadata["og:title"] = title;
        metadata["og:description"] = description;

        if (!string.IsNullOrWhiteSpace( image )) {
            metadata["og:image"] = image;
            metadata["og:image:alt"] = $"{title} artwork";
        }

        // Add music-specific metadata
        metadata["music:musician"] = artist;

        // Add theme color based on primary provider (for Discord embed color)
        if (primaryProvider.HasValue) {
            string themeColor = GetPrimaryProviderColorHex( primaryProvider.Value );
            metadata["theme-color"] = themeColor;
        }

        return metadata;
    }

    /// <summary>
    /// Gets the hex color code for a provider's theme color.
    /// </summary>
    /// <param name="provider">The music provider.</param>
    /// <returns>Hex color code string.</returns>
    private static string GetPrimaryProviderColorHex( SupportedProviders provider )
        => provider switch {
            SupportedProviders.AppleMusic => "#D60017",
            SupportedProviders.Spotify => "#1ED760",
            SupportedProviders.Tidal => "#FFFFFF",
            _ => "#6366F1"  // Default purple
        };

    #endregion OpenGraph Extensions


    /// <summary>
    /// Registers all BridgeBeats services including link resolver and card services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="enabledProviders">Set of enabled music providers.</param>
    /// <param name="useCaching">Whether to use caching for media link service.</param>
    /// <param name="domain">The base URL for card and playlist URLs.</param>
    /// <param name="cardCacheExpirationHours">Card cache expiration in hours.</param>
    /// <param name="cardCacheCleanupInterval">Card cache cleanup interval.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBridgeBeatsServices(
        this IServiceCollection services,
        HashSet<SupportedProviders> enabledProviders,
        bool useCaching,
        string domain,
        int cardCacheExpirationHours,
        int cardCacheCleanupInterval
    ) {
        _ = services.AddMediaLinkResolver( enabledProviders, useCaching );
        _ = services.AddCardServices( domain, cardCacheExpirationHours, cardCacheCleanupInterval );
        _ = services.AddQrCodeService( );

        return services;
    }

}
