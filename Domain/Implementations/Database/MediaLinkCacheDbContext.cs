using Microsoft.EntityFrameworkCore;
using TuneBridge.Domain.Contracts.Entities;

namespace TuneBridge.Domain.Implementations.Database {

    /// <summary>
    /// Database context for storing MediaLinkResult cache entries and input links.
    /// </summary>
    public class MediaLinkCacheDbContext : DbContext {

        public MediaLinkCacheDbContext( DbContextOptions<MediaLinkCacheDbContext> options )
            : base( options ) {
        }

        /// <summary>
        /// Cache entries representing MediaLinkResults stored on Bluesky PDS.
        /// </summary>
        public DbSet<MediaLinkCacheEntry> CacheEntries { get; set; }

        /// <summary>
        /// Input links that map to cache entries.
        /// </summary>
        public DbSet<InputLinkEntry> InputLinks { get; set; }

        protected override void OnModelCreating( ModelBuilder modelBuilder ) {
            base.OnModelCreating( modelBuilder );

            // Configure MediaLinkCacheEntry
            _ = modelBuilder.Entity<MediaLinkCacheEntry>( entity => {
                _ = entity.HasKey( e => e.Id );
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

            // Configure InputLinkEntry
            _ = modelBuilder.Entity<InputLinkEntry>( entity => {
                _ = entity.HasKey( e => e.Id );
                _ = entity.Property( e => e.Link )
                    .IsRequired( )
                    .HasMaxLength( 1000 );
                _ = entity.Property( e => e.CreatedAt )
                    .IsRequired( );

                _ = entity.HasIndex( e => e.Link )
                    .IsUnique( );

                _ = entity.HasOne( e => e.MediaLinkCacheEntry )
                    .WithMany( c => c.InputLinks )
                    .HasForeignKey( e => e.MediaLinkCacheEntryId )
                    .OnDelete( DeleteBehavior.Cascade );
            } );
        }
    }
}
