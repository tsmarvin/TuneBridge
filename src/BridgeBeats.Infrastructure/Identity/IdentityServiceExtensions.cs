using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BridgeBeats.Infrastructure.Identity {

    /// <summary>
    /// Extension methods for registering ASP.NET Core Identity services.
    /// </summary>
    public static class IdentityServiceExtensions {

        /// <summary>
        /// Adds ASP.NET Core Identity services with secure password requirements and Data Protection.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <returns>The configured service collection.</returns>
        public static IServiceCollection AddBridgeBeatsIdentity( this IServiceCollection services ) {
            // Configure Data Protection for encrypting personal data
            // Keys are stored in application data directory by default
            // Note: [ProtectedPersonalData] attribute requires additional setup not included here.
            // Properties marked with this attribute will NOT be automatically encrypted without
            // implementing and registering IPersonalDataProtector or using AddIdentity() instead of AddIdentityCore().
            _ = services.AddDataProtection( );

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
