using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BridgeBeats.Infrastructure.Identity {

    /// <summary>
    /// Extension methods for registering ASP.NET Core Identity services.
    /// </summary>
    public static class IdentityServiceExtensions {

        /// <summary>
        /// Adds ASP.NET Core Identity services with secure password requirements.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <returns>The configured service collection.</returns>
        public static IServiceCollection AddBridgeBeatsIdentity( this IServiceCollection services ) {
            _ = services.AddIdentityCore<ApplicationUser>( options => {
                // Password settings
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequiredLength = 14;

                // User settings
                options.User.RequireUniqueEmail = true;
            } )
            .AddRoles<IdentityRole>( )
            .AddEntityFrameworkStores<ApplicationDbContext>( )
            .AddSignInManager( )
            .AddDefaultTokenProviders( );

            // Register scoped ApplicationDbContext for Identity framework using the factory
            _ = services.AddScoped( sp => {
                IDbContextFactory<ApplicationDbContext> factory = sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>( );
                return factory.CreateDbContext( );
            } );

            return services;
        }
    }
}
