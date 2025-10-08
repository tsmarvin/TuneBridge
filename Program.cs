using TuneBridge.Configuration;

namespace TuneBridge {
    public class Program {
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
