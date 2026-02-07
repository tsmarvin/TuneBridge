using BridgeBeats.Core.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BridgeBeats.Core.Infrastructure.Extensions {

    /// <summary>
    /// Extension methods for registering ASP.NET Core Identity services.
    /// </summary>
    public static class IdentityServiceExtensions {

        /// <summary>
        /// Adds ASP.NET Core Identity services with secure password requirements, Data Protection
        /// with persistent key storage, and personal data encryption for <c>[ProtectedPersonalData]</c> fields.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <param name="dataProtectionKeyPath">
        /// Directory path for persisting Data Protection keys. Defaults to <c>"./keys"</c>.
        /// In Docker, this should be a mounted volume (e.g., <c>/app/keys</c>).
        /// </param>
        /// <returns>The configured service collection.</returns>
        /// <remarks>
        /// Data Protection keys are persisted to the file system at <paramref name="dataProtectionKeyPath"/>.
        /// The <c>AddPersonalDataProtection</c> call registers <see cref="DataProtectionLookupProtector"/>
        /// and <see cref="DataProtectionKeyRing"/>, enabling automatic encryption/decryption for all
        /// Identity fields marked with <c>[ProtectedPersonalData]</c>.
        /// </remarks>
        public static IServiceCollection AddBridgeBeatsIdentity(
            this IServiceCollection services,
            string dataProtectionKeyPath = "./keys"
        ) {
            // Configure Data Protection with persistent key storage.
            // Keys must survive container restarts to decrypt existing data.
            DirectoryInfo keyDirectory = new( dataProtectionKeyPath );
            _ = services.AddDataProtection( )
                .SetApplicationName( "BridgeBeats" )
                .PersistKeysToFileSystem( keyDirectory );

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
            .AddDefaultTokenProviders( )
            .AddPersonalDataProtection<DataProtectionLookupProtector, DataProtectionKeyRing>( );

            // Register scoped ApplicationDbContext for Identity framework using the factory
            _ = services.AddScoped( sp => {
                IDbContextFactory<ApplicationDbContext> factory = sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>( );
                return factory.CreateDbContext( );
            } );

            return services;
        }
    }
}
