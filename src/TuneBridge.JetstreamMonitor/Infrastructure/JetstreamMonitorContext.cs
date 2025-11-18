using Microsoft.EntityFrameworkCore;

namespace TuneBridge.JetStreamMonitor.Infrastructure;

/// <summary>
/// Database context for the Jetstream monitor's SQLite database.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="JetstreamMonitorContext"/> class.
/// </remarks>
/// <param name="options">The database context options.</param>
public class JetstreamMonitorContext(
    DbContextOptions<JetstreamMonitorContext> options
) : DbContext( options ) {

    /// <summary>
    /// Gets or sets the detected music links.
    /// </summary>
    public DbSet<DetectedMusicLink> DetectedMusicLinks { get; set; }

    /// <inheritdoc/>
    protected override void OnModelCreating( ModelBuilder modelBuilder ) {
        base.OnModelCreating( modelBuilder );

        // Create unique index on URL to enforce uniqueness
        _ = modelBuilder.Entity<DetectedMusicLink>( )
            .HasIndex( link => link.Url )
            .IsUnique( );

        // Create index on provider for faster queries
        _ = modelBuilder.Entity<DetectedMusicLink>( )
            .HasIndex( link => link.Provider );

        // Create index on FirstDetectedAt for time-based queries
        _ = modelBuilder.Entity<DetectedMusicLink>( )
            .HasIndex( link => link.FirstDetectedAt );
    }
}
