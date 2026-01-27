using BridgeBeats.Contracts.Enums;
using BridgeBeats.Providers.Common;
using Microsoft.Extensions.DependencyInjection;

namespace BridgeBeats.Providers.AppleMusic {

    /// <summary>
    /// Extension methods for registering Apple Music provider services.
    /// </summary>
    public static class AppleMusicServiceExtensions {

        /// <summary>
        /// Registers Apple Music services if valid credentials are provided.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <param name="teamId">The Apple Developer Team ID.</param>
        /// <param name="keyId">The Apple Music API Key ID.</param>
        /// <param name="keyPath">The path to the .p8 private key file.</param>
        /// <param name="enabledProviders">The set of enabled providers to update.</param>
        /// <param name="maxRetryAfterSeconds">Maximum Retry-After value in seconds before failing fast.</param>
        /// <returns>True if Apple Music services were registered; otherwise false.</returns>
        public static bool AddAppleMusicServices(
            this IServiceCollection services,
            string? teamId,
            string? keyId,
            string? keyPath,
            HashSet<SupportedProviders> enabledProviders,
            int maxRetryAfterSeconds = 120
        ) {
            if (!services.AddAppleMusicJwtHandler( teamId, keyId, keyPath, maxRetryAfterSeconds )) {
                return false;
            }

            _ = services.AddTransient<AppleMusicLookupService>( );

            _ = enabledProviders.Add( SupportedProviders.AppleMusic );
            return true;
        }

        /// <summary>
        /// Registers the AppleJwtHandler and musickit-api HTTP client for MusicKit JS authentication.
        /// This is needed for the AppleMusicController to generate developer tokens, regardless of
        /// whether worker services are used for backend lookups.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <param name="teamId">The Apple Developer Team ID.</param>
        /// <param name="keyId">The Apple Music API Key ID.</param>
        /// <param name="keyPath">The path to the .p8 private key file.</param>
        /// <param name="maxRetryAfterSeconds">Maximum Retry-After value in seconds before failing fast.</param>
        /// <returns>True if the JWT handler was registered; otherwise false.</returns>
        public static bool AddAppleMusicJwtHandler(
            this IServiceCollection services,
            string? teamId,
            string? keyId,
            string? keyPath,
            int maxRetryAfterSeconds = 120
        ) {
            // Check if Apple Music credentials are provided
            if (string.IsNullOrWhiteSpace( teamId ) ||
                string.IsNullOrWhiteSpace( keyId )) {
                return false;
            }

            // If Team ID and Key ID are provided, the key path must also be provided and valid
            if (string.IsNullOrWhiteSpace( keyPath )) {
                return false;
            }

            // Fail fast if missing required apple key file.
            FileInfo keyFile = new( keyPath );
            if (!keyFile.Exists) {
                throw new FileNotFoundException( $"Missing .p8 file at: {keyFile.FullName}" );
            }

            // Fail fast if apple key file is empty.
            string keyContents = File.ReadAllText( keyFile.FullName );
            if (string.IsNullOrWhiteSpace( keyContents )) {
                throw new InvalidDataException( $".p8 file missing contents at: {keyFile.FullName}" );
            }

            _ = services.AddHttpClient( "musickit-api", c => {
                c.BaseAddress = new Uri( "https://api.music.apple.com/v1/catalog/" );
            } )
            .AddHttpMessageHandler( ProviderServiceExtensions.CreateRetryAfterLimitHandlerFactory( maxRetryAfterSeconds ) )
            .AddHttpMessageHandler( ( ) => new ProviderMetricsHandler( "applemusic" ) );

            _ = services.AddSingleton( new AppleJwtHandler( teamId, keyId, keyContents ) );

            return true;
        }

    }

}
