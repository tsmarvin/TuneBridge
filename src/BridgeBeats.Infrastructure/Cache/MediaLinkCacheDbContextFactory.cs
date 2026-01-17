using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BridgeBeats.Infrastructure.Cache;

/// <summary>
/// Factory for creating MediaLinkCacheDbContext instances at design time (for migrations).
/// </summary>
public class MediaLinkCacheDbContextFactory : IDesignTimeDbContextFactory<MediaLinkCacheDbContext> {
    /// <summary>
    /// Creates a new instance of <see cref="MediaLinkCacheDbContext"/> for design-time tools.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>A configured MediaLinkCacheDbContext instance.</returns>
    public MediaLinkCacheDbContext CreateDbContext( string[] args ) {
        DbContextOptionsBuilder<MediaLinkCacheDbContext> optionsBuilder = new( );
        _ = optionsBuilder.UseSqlite( "Data Source=bridgebeats.db" );

        return new MediaLinkCacheDbContext( optionsBuilder.Options );
    }
}
