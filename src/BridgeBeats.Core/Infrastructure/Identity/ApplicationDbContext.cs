using BridgeBeats.Core.Infrastructure.Playlists;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BridgeBeats.Core.Infrastructure.Identity;

/// <summary>
/// Entity Framework Core database context for the application, extending the ASP.NET Core
/// Identity context with playlist and ATProto OAuth-state entities.
/// </summary>
/// <param name="options">The options used to configure the context.</param>
public class ApplicationDbContext( DbContextOptions<ApplicationDbContext> options )
    : IdentityDbContext<ApplicationUser>( options ) {

    /// <summary>
    /// Gets or sets the set of stored playlist entries, created by users or anonymously.
    /// </summary>
    public DbSet<PlaylistEntry> Playlists { get; set; }

    /// <summary>
    /// Gets or sets the set of transient ATProto OAuth state rows held during the authentication flow.
    /// </summary>
    public DbSet<AtProtoOAuthState> AtProtoOAuthStates { get; set; }

    /// <summary>
    /// Configures the entity model, including indexes and column constraints.
    /// </summary>
    /// <remarks>
    /// Notable index nuances:
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="ApplicationUser.ApiKeyHash"/> has a unique index that is not filtered for NULL,
    /// which differs from the DID index below.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="ApplicationUser.AtProtoDid"/> has a unique index filtered to non-null values,
    /// allowing many users without a DID but at most one per DID.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <c>NormalizedEmail</c> has a unique index named <c>EmailIndex</c>. This database constraint
    /// enforces email uniqueness even though Identity's own <c>RequireUniqueEmail</c> option is false.
    /// </description>
    /// </item>
    /// </list>
    /// </remarks>
    /// <param name="builder">The model builder used to construct the schema.</param>
    protected override void OnModelCreating( ModelBuilder builder ) {
        base.OnModelCreating( builder );

        // Add indexes for performance
        _ = builder.Entity<ApplicationUser>( )
            .HasIndex( u => u.ApiKeyHash )
            .IsUnique( );

        // Add unique index for ATProto DID
        _ = builder.Entity<ApplicationUser>( )
            .HasIndex( u => u.AtProtoDid )
            .IsUnique( )
            .HasFilter( "[AtProtoDid] IS NOT NULL" );

        // Upgrade Identity's default EmailIndex to unique: closes the Register TOCTOU race by
        // converting the window between FindByEmailAsync and CreateAsync into a DB constraint
        // violation caught in AccountController.Register. SQLite allows multiple NULLs in a
        // unique index, so email-less ATProto users are unaffected.
        _ = builder.Entity<ApplicationUser>( )
            .HasIndex( u => u.NormalizedEmail )
            .IsUnique( )
            .HasDatabaseName( "EmailIndex" );

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

        // Configure AtProtoOAuthState for temporary OAuth state storage
        _ = builder.Entity<AtProtoOAuthState>( entity => {
            _ = entity.HasKey( e => e.State );
            _ = entity.Property( e => e.State )
                .IsRequired( )
                .HasMaxLength( 128 );
            _ = entity.Property( e => e.CodeVerifier )
                .IsRequired( )
                .HasMaxLength( 512 );
            _ = entity.Property( e => e.Handle )
                .IsRequired( )
                .HasMaxLength( 256 );
            _ = entity.Property( e => e.Did )
                .HasMaxLength( 128 );
            _ = entity.Property( e => e.PdsUri )
                .HasMaxLength( 512 );
            _ = entity.Property( e => e.AuthorizationServerUri )
                .HasMaxLength( 512 );
            _ = entity.Property( e => e.DPoPKeyJwk )
                .IsRequired( );
            _ = entity.Property( e => e.CreatedAt )
                .IsRequired( );
            _ = entity.Property( e => e.ExpiresAt )
                .IsRequired( );

            _ = entity.HasIndex( e => e.ExpiresAt );
        } );
    }
}
