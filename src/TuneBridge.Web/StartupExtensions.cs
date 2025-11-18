using System.Text.Json;
using AspNetCore.Authentication.ApiKey;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using TuneBridge.Common;
using TuneBridge.Common.Contracts.Constants;
using TuneBridge.Common.Contracts.Interfaces;
using TuneBridge.Web.Identity;
using TuneBridge.Web.Middleware;
using TuneBridge.Web.Models;
using TuneBridge.Web.Services;

namespace TuneBridge.Web {
    /// <summary>
    /// The Web application startup extensions.
    /// </summary>
    public static class StartupExtensions {

        #region Service Configuration

        /// <summary>
        /// Registers TuneBridge Web services and authentication handlers.
        /// </summary>
        /// <param name="builder">The builder to configure.</param>
        /// <param name="args">The commandline arguments.</param>
        /// <returns>The configured builder.</returns>
        public static WebApplicationBuilder ConfigureTuneBridgeServices(
            this WebApplicationBuilder builder,
            string[] args
        ) {
            if (builder.Environment.EnvironmentName != "Testing") {
                _ = builder
                    .Configuration
                    .ConfigureAppSettings( args );
                _ = builder.AddCommon( );
            }

            IServiceCollection services = builder.Services;
            IConfiguration config = builder.Configuration;

            _ = builder.AddCommon( );
            ConfigureWebServices( services, config );

            return builder;
        }

        internal static void ConfigureWebServices(
            IServiceCollection services,
            IConfiguration config
        ) {
            // Add services to the container.
            _ = services
                .AddControllersWithViews( )
                .AddRazorOptions( o => {
                    o.ViewLocationFormats.Clear( );
                    o.ViewLocationFormats.Add( "/Web/Views/{1}/{0}.cshtml" );
                    o.ViewLocationFormats.Add( "/Web/Views/Shared/{0}.cshtml" );
                } );
            _ = services.AddSingleton( new JsonSerializerOptions { WriteIndented = true } );

            AppSettings settings = new( );
            config.GetRequiredSection( "Identity" ).Bind( settings );

            _ = services.AddMemoryCache( );

            ConfigureIdentity( services, settings );

            _ = services.AddSingleton<IOpenGraphCardService, OpenGraphCardService>(
                p => new OpenGraphCardService( settings.BaseUrl )
            );
        }

        private static void ConfigureIdentity(
            IServiceCollection services,
            AppSettings settings
        ) {
            _ = services.AddDbContext<IdentityDbContext>( options =>
                options.UseSqlite( settings.IdentityConnectionString )
            );

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
            .AddEntityFrameworkStores<IdentityDbContext>( )
            .AddSignInManager( )
            .AddDefaultTokenProviders( );

            ConfigureApiKeyAuth( services, settings );
        }

        private static void ConfigureApiKeyAuth(
            IServiceCollection services,
            AppSettings settings
        ) {
            // Configure API Key hashing
            if (string.IsNullOrWhiteSpace( settings.ApiKeySalt )) {
                throw new InvalidOperationException( "ApiKeySalt is required in configuration for secure API key storage." );
            }
            _ = services.AddSingleton( new ApiKeyHasher( settings.ApiKeySalt ) );

            // Configure API Key authentication for API endpoints
            _ = services.AddTransient<IApiKeyProvider, ApiKeyProvider>( );
            _ = services.AddAuthentication( options => {
                options.DefaultScheme = "MultiScheme";
                options.DefaultChallengeScheme = "MultiScheme";
            } )
            .AddPolicyScheme( "MultiScheme", "API Key or Cookie", options => {
                options.ForwardDefaultSelector = context => context.Request.Headers.ContainsKey( "X-API-Key" )
                    ? ApiKeyDefaults.AuthenticationScheme
                    : IdentityConstants.ApplicationScheme;
            } )
            .AddApiKeyInHeader<ApiKeyProvider>( options => {
                options.Realm = "TuneBridge API";
                options.KeyName = "X-API-Key";
            } )
            .AddIdentityCookies( ); // Add cookie authentication for web UI

            _ = services.AddAuthorization( );
        }
        #endregion Service Configuration

        #region App Configuration

        /// <summary>
        /// Configures the web application post service registration.
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static async Task<WebApplication> ConfigureWebApp(
            this WebApplicationBuilder builder
        ) {
            WebApplication app = builder.Build();

            IConfiguration config = app.Configuration;
            AppSettings settings = new( );
            config.GetRequiredSection( "Identity" ).Bind( settings );

            // Initialize database and seed roles
            _ = await app.InitializeDatabaseAsync( );

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment( )) {
                _ = app.UseExceptionHandler( "/Home/Error" );
                _ = app.UseHsts( );
            }
            _ = app.UseHttpsRedirection( );

            // Configure static file provider with proper MIME types for AT Protocol lexicon files
            FileExtensionContentTypeProvider provider = new( );
            provider.Mappings[".json"] = "application/json";

            // Serve .well-known directory with proper content types for AT Protocol lexicons
            _ = app.UseStaticFiles( new StaticFileOptions {
                FileProvider = new PhysicalFileProvider( Path.Combine( builder.Environment.WebRootPath, ".well-known" ) ),
                RequestPath = "/.well-known",
                ContentTypeProvider = provider,
                ServeUnknownFileTypes = true, // Allow serving files without extensions (required for ATProto lexicons)
                DefaultContentType = "application/octet-stream", // Default for unknown types
                OnPrepareResponse = ctx => {
                    // Serve lexicon files in atproto-lexicon directory as application/json
                    if (ctx.File.PhysicalPath != null && ctx.File.PhysicalPath.Contains( "atproto-lexicon", StringComparison.OrdinalIgnoreCase )) {
                        ctx.Context.Response.ContentType = "application/json; charset=utf-8";
                    }

                    // Add CORS headers to allow ATProto PDS to fetch lexicon files
                    ctx.Context.Response.Headers.Append( "Access-Control-Allow-Origin", "*" );
                    ctx.Context.Response.Headers.Append( "Access-Control-Allow-Methods", "GET, HEAD, OPTIONS" );
                    ctx.Context.Response.Headers.Append( "Access-Control-Allow-Headers", "Content-Type" );

                    // Cache lexicon files for 1 day
                    ctx.Context.Response.Headers.Append( "Cache-Control", "public, max-age=86400" );
                }
            } );

            _ = app.UseStaticFiles( ); // Serve static files from wwwroot
            _ = app.UseRouting( );

            // Restrict health endpoint access to internal requests only
            // NOTE: This middleware is intentionally placed before authentication because it uses IP-based authorization
            // and does not require authenticated user context. If future changes require authentication, adjust the order accordingly.
            _ = app.UseMiddleware<HealthEndpointAuthorizationMiddleware>( );

            _ = app.UseAuthentication( );
            _ = app.UseAuthorization( );

            // Add rate limiting middleware with configured rate limit
            _ = app.UseMiddleware<RateLimitingMiddleware>( settings.RateLimitRequestsPerHour );

            _ = app.MapStaticAssets( );
            _ = app.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}" )
                .WithStaticAssets( );

            return app;
        }

        /// <summary>
        /// Ensures the database is created and applies any pending migrations.
        /// Seeds required roles if they don't exist.
        /// </summary>
        /// <param name="app">The web application.</param>
        /// <returns>The web application for method chaining.</returns>
        private static async Task<WebApplication> InitializeDatabaseAsync( this WebApplication app ) {
            using IServiceScope scope = app.Services.CreateScope( );
            IdentityDbContext context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>( );
            RoleManager<IdentityRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>( );

            // Apply pending migrations and create the database if it doesn't exist
            await context.Database.MigrateAsync( );

            // Seed the AspireDashboardAccess role if it doesn't exist
            await SeedRolesAsync( roleManager );

            return app;
        }

        /// <summary>
        /// Seeds required application roles.
        /// </summary>
        /// <param name="roleManager">The role manager.</param>
        private static async Task SeedRolesAsync( RoleManager<IdentityRole> roleManager ) {
            // Create AspireDashboardAccess role if it doesn't exist
            if (!await roleManager.RoleExistsAsync( Roles.AspireDashboardAccess )) {
                _ = await roleManager.CreateAsync( new IdentityRole( Roles.AspireDashboardAccess ) );
            }
        }

        #endregion App Configuration
    }
}
