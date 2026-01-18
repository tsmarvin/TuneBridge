using BridgeBeats.Contracts.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BridgeBeats.Infrastructure.Cache {

    /// <summary>
    /// Extension methods for registering cache and database services.
    /// </summary>
    public static class CacheServiceExtensions {

        /// <summary>
        /// Adds the media link cache repository as a singleton using DbContextFactory for thread-safe database access.
        /// This method should only be called when ATProto storage is configured.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <param name="cacheDays">Number of days to cache media link results.</param>
        /// <param name="atProtoUserDID">ATProto user DID for storage (required).</param>
        /// <returns>The configured service collection.</returns>
        public static IServiceCollection AddMediaLinkCache(
            this IServiceCollection services,
            int cacheDays,
            string atProtoUserDID
        ) {
            _ = services.AddSingleton<IMediaLinkCacheRepository>( s => new MediaLinkCacheRepository(
                s.GetRequiredService<IDbContextFactory<MediaLinkCacheDbContext>>( ),
                s.GetRequiredService<IATProtoStorageService>( ),
                s.GetRequiredService<ILogger<MediaLinkCacheRepository>>( ),
                cacheDays,
                atProtoUserDID
            ) );

            return services;
        }
    }
}
