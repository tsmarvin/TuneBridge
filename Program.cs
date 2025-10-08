using TuneBridge.Configuration;

namespace TuneBridge {
    /// <summary>
    /// Main entry point for the TuneBridge web application. Configures ASP.NET Core services, middleware,
    /// and routing for both the web UI and REST API endpoints. Sets up music provider integrations
    /// (Apple Music, Spotify) and optional Discord bot functionality based on configuration.
    /// </summary>
    /// <remarks>
    /// The application supports multiple deployment modes:
    /// - Web-only: Serves the UI and REST API for manual music link translation
    /// - Discord bot: Monitors Discord channels and automatically responds to music links
    /// - Combined: Runs both web interface and Discord bot in a single process
    /// Mode is determined by presence of DiscordToken in configuration.
    /// </remarks>
    public class Program {
        /// <summary>
        /// Application entry point. Initializes the ASP.NET Core host, configures services including
        /// HTTP clients with retry policies, sets up authentication for music provider APIs, and starts
        /// the web server. Also initializes Discord bot if token is configured.
        /// </summary>
        /// <param name="args">
        /// Command-line arguments that can override appsettings.json values. Useful for containerized
        /// deployments where configuration is passed via environment variables or command flags.
        /// </param>
        /// <remarks>
        /// Configuration is loaded from (in order of precedence):
        /// 1. Command-line arguments
        /// 2. Environment variables
        /// 3. appsettings.json file
        /// 
        /// Required configuration:
        /// - TuneBridge:AppleTeamId, AppleKeyId, AppleKeyPath (for Apple Music)
        /// - TuneBridge:SpotifyClientId, SpotifyClientSecret (for Spotify)
        /// Optional:
        /// - TuneBridge:DiscordToken (enables Discord bot functionality)
        /// - TuneBridge:NodeNumber (for Discord sharding in multi-instance deployments)
        /// </remarks>
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

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment( )) {
                _ = app.UseExceptionHandler( "/Home/Error" );
                _ = app.UseHsts( );
            }

            _ = app.UseHttpsRedirection( );
            _ = app.UseRouting( );

            _ = app.MapStaticAssets( );
            _ = app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}" )
                .WithStaticAssets( );

            app.Run( );
        }
    }
}
