using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace TuneBridge.Domain.Models;

/// <summary>
/// Database context for ASP.NET Identity with custom ApplicationUser.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser> {
    public ApplicationDbContext( DbContextOptions<ApplicationDbContext> options )
        : base( options ) {
    }

    protected override void OnModelCreating( ModelBuilder builder ) {
        base.OnModelCreating( builder );

        // Add indexes for performance
        _ = builder.Entity<ApplicationUser>( )
            .HasIndex( u => u.ApiKeyHash )
            .IsUnique( );
    }
}
