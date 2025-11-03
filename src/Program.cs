using TuneBridge.Configuration;
using TuneBridge.Domain.Implementations.Middleware;

namespace TuneBridge {
    /// <summary>
    /// Main entry point for the TuneBridge web application. Configures ASP.NET Core services, middleware,
    /// and routing for the web UI and REST API endpoints. Sets up music provider integrations
    /// (Apple Music, Spotify) and optional Discord bot functionality.
    /// </summary>
    public class Program {
        /// <summary>
        /// Application entry point. Initializes the ASP.NET Core host, configures services, and starts
        /// the web server. Also initializes Discord bot if token is configured.
        /// </summary>
        /// <param name="args">Command-line arguments for configuration overrides.</param>
        public static void Main( string[] args ) {

            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions() {
                ApplicationName = "TuneBridge",
                Args = args,
                WebRootPath = "Web/wwwroot"
            });

            _ = builder
                .Configuration
                .ConfigureAppSettings( args );

            // Add services to the container.
            _ = builder
                .Services
                .AddControllersWithViews( )
                .AddRazorOptions( o => {
                    o.ViewLocationFormats.Clear( );
                    o.ViewLocationFormats.Add( "/Web/Views/{1}/{0}.cshtml" );
                    o.ViewLocationFormats.Add( "/Web/Views/Shared/{0}.cshtml" );
                } );
            _ = builder.Services.AddTuneBridgeServices( builder.Configuration );

            WebApplication app = builder.Build();

            // Initialize database
            _ = app.InitializeDatabase( );

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment( )) {
                _ = app.UseExceptionHandler( "/Home/Error" );
                _ = app.UseHsts( );
            }

            _ = app.UseHttpsRedirection( );
            _ = app.UseStaticFiles( ); // Serve static files from wwwroot
            _ = app.UseRouting( );

            _ = app.UseAuthentication( );
            _ = app.UseAuthorization( );

            // Enable Swagger middleware
            _ = app.UseSwagger( );
            _ = app.UseSwaggerUI( options => {
                options.SwaggerEndpoint( "/swagger/v1/swagger.json", "TuneBridge API v1" );
                options.RoutePrefix = "swagger";
                options.DocumentTitle = "TuneBridge API Documentation";
            } );

            // Restrict Swagger UI access to authenticated users
            _ = app.UseMiddleware<SwaggerAuthorizationMiddleware>( );

            // Add rate limiting middleware with configured rate limit
            int rateLimitRequestsPerHour = builder.Configuration.GetSection( "TuneBridge" ).GetValue<int>( "RateLimitRequestsPerHour" );
            _ = app.UseMiddleware<RateLimitingMiddleware>( rateLimitRequestsPerHour );

            _ = app.MapStaticAssets( );
            _ = app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}" )
                .WithStaticAssets( );

            app.Run( );
        }
    }
}
