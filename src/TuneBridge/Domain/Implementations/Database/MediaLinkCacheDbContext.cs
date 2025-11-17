using Microsoft.EntityFrameworkCore;
using TuneBridge.Domain.Contracts.Entities;

namespace TuneBridge.Domain.Implementations.Database {

    /// <summary>
    /// Database context for storing MediaLinkResult cache entries and lookup entries.
    /// </summary>
    public class MediaLinkCacheDbContext : DbContext {

        /// <summary>
        /// Initializes a new instance of the <see cref="MediaLinkCacheDbContext"/> class.
        /// </summary>
        /// <param name="options">The database context configuration options.</param>
        public MediaLinkCacheDbContext( DbContextOptions<MediaLinkCacheDbContext> options )
            : base( options ) {
        }

        /// <summary>
        /// Cache entries representing MediaLinkResults stored on ATProto PDS.
        /// </summary>
        public DbSet<MediaLinkCacheEntry> CacheEntries { get; set; }

        /// <summary>
        /// Lookup entries that map to cache entries (links, IDs, metadata).
        /// </summary>
        public DbSet<MediaLookupEntry> LookupEntries { get; set; }

        /// <inheritdoc/>
        protected override void OnModelCreating( ModelBuilder modelBuilder ) {
            base.OnModelCreating( modelBuilder );

            // Configure MediaLinkCacheEntry
            _ = modelBuilder.Entity<MediaLinkCacheEntry>( entity => {
                _ = entity.HasKey( e => e.Rkey );
                _ = entity.Property( e => e.Rkey )
                    .IsRequired( )
                    .HasMaxLength( 200 );
                _ = entity.Property( e => e.RecordUri )
                    .IsRequired( )
                    .HasMaxLength( 500 );
                _ = entity.Property( e => e.CreatedAt )
                    .IsRequired( );
                _ = entity.Property( e => e.LastLookedUpAt )
                    .IsRequired( );

                _ = entity.HasIndex( e => e.RecordUri )
                    .IsUnique( );
                _ = entity.HasIndex( e => e.LastLookedUpAt );
            } );

            // Configure MediaLookupEntry
            _ = modelBuilder.Entity<MediaLookupEntry>( entity => {
                _ = entity.HasKey( e => e.Id );
                _ = entity.Property( e => e.LookupValue )
                    .IsRequired( )
                    .HasMaxLength( 1000 );
                _ = entity.Property( e => e.LookupType )
                    .IsRequired( );
                _ = entity.Property( e => e.IsAlbum )
                    .IsRequired( );
                _ = entity.Property( e => e.MediaLinkCacheEntryRkey )
                    .IsRequired( )
                    .HasMaxLength( 200 );
                _ = entity.Property( e => e.CreatedAt )
                    .IsRequired( );

                // Composite unique index on LookupValue, LookupType, and IsAlbum
                // This allows the same URL/ID/metadata to exist for both tracks and albums
                _ = entity.HasIndex( e => new { e.LookupValue, e.LookupType, e.IsAlbum } )
                    .IsUnique( );

                _ = entity.HasOne( e => e.MediaLinkCacheEntry )
                    .WithMany( c => c.LookupEntries )
                    .HasForeignKey( e => e.MediaLinkCacheEntryRkey )
                    .OnDelete( DeleteBehavior.Cascade );
            } );
        }
    }
}
