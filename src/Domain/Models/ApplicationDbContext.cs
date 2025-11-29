using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using BridgeBeats.Domain.Contracts.Entities;

namespace BridgeBeats.Domain.Models;

/// <summary>
/// Database context for ASP.NET Identity with custom ApplicationUser.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ApplicationDbContext"/> class.
/// </remarks>
/// <param name="options">The database context configuration options.</param>
public class ApplicationDbContext( DbContextOptions<ApplicationDbContext> options )
    : IdentityDbContext<ApplicationUser>( options ) {

    /// <summary>
    /// Playlists created by users or anonymously.
    /// </summary>
    public DbSet<PlaylistEntry> Playlists { get; set; }

    /// <inheritdoc/>
    protected override void OnModelCreating( ModelBuilder builder ) {
        base.OnModelCreating( builder );

        // Add indexes for performance
        _ = builder.Entity<ApplicationUser>( )
            .HasIndex( u => u.ApiKeyHash )
            .IsUnique( );

        // Configure PlaylistEntry
        _ = builder.Entity<PlaylistEntry>( entity => {
            _ = entity.HasKey( e => e.PlaylistId );
            _ = entity.Property( e => e.PlaylistId )
                .IsRequired( )
                .HasMaxLength( 64 );
            _ = entity.Property( e => e.Title )
                .HasMaxLength( 200 );
            _ = entity.Property( e => e.Description )
                .HasMaxLength( 1000 );
            _ = entity.Property( e => e.CardIds )
                .IsRequired( )
                .HasMaxLength( 2000 ); // Support up to 20 card IDs
            _ = entity.Property( e => e.CardRkeys )
                .IsRequired( )
                .HasMaxLength( 2000 ); // Support up to 20 rkeys
            _ = entity.Property( e => e.UserId )
                .HasMaxLength( 450 ); // Standard ASP.NET Identity user ID length
            _ = entity.Property( e => e.CreatedAt )
                .IsRequired( );
            _ = entity.Property( e => e.ExpiresAt );

            _ = entity.HasIndex( e => e.UserId );
            _ = entity.HasIndex( e => e.CreatedAt );
            _ = entity.HasIndex( e => e.ExpiresAt );
        } );
    }
}
