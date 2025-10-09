using TuneBridge.Configuration;

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
