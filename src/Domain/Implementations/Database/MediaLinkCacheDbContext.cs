using BridgeBeats.Domain.Contracts.Entities;
using Microsoft.EntityFrameworkCore;

namespace BridgeBeats.Domain.Implementations.Database {

    /// <summary>
    /// Database context for storing MediaLinkResult cache entries and lookup entries.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="MediaLinkCacheDbContext"/> class.
    /// </remarks>
    /// <param name="options">The database context configuration options.</param>
    public class MediaLinkCacheDbContext( DbContextOptions<MediaLinkCacheDbContext> options ) : DbContext( options ) {

        /// <summary>
        /// Cache entries representing MediaLinkResults stored on ATProto PDS.
        /// </summary>
        public DbSet<MediaLinkCacheEntry> CacheEntries { get; set; }

        /// <summary>
        /// Lookup entries that map to cache entries (links, IDs, metadata).
        /// </summary>
        public DbSet<MediaLookupEntry> LookupEntries { get; set; }

        /// <summary>
        /// Provider-specific entries that map to cache entries (provider IDs).
        /// </summary>
        public DbSet<MediaProviderEntry> ProviderEntries { get; set; }

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
                _ = entity.Property( e => e.CardId )
                    .HasMaxLength( 64 );

                _ = entity.HasIndex( e => e.RecordUri )
                    .IsUnique( );
                _ = entity.HasIndex( e => e.LastLookedUpAt );
                _ = entity.HasIndex( e => e.CardId );
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

            // Configure MediaProviderEntry
            _ = modelBuilder.Entity<MediaProviderEntry>( entity => {
                _ = entity.HasKey( e => e.Id );
                _ = entity.Property( e => e.Provider )
                    .IsRequired( );
                _ = entity.Property( e => e.ProviderId )
                    .IsRequired( )
                    .HasMaxLength( 200 );
                _ = entity.Property( e => e.MediaLinkCacheEntryRkey )
                    .IsRequired( )
                    .HasMaxLength( 200 );
                _ = entity.Property( e => e.CreatedAt )
                    .IsRequired( );

                // Composite unique index on Provider and ProviderId
                // This ensures each provider ID is only stored once per provider
                _ = entity.HasIndex( e => new { e.Provider, e.ProviderId } )
                    .IsUnique( );

                // Index on ProviderId for fast lookups by provider-specific ID
                _ = entity.HasIndex( e => e.ProviderId );

                _ = entity.HasOne( e => e.MediaLinkCacheEntry )
                    .WithMany( c => c.ProviderEntries )
                    .HasForeignKey( e => e.MediaLinkCacheEntryRkey )
                    .OnDelete( DeleteBehavior.Cascade );
            } );
        }
    }
}
