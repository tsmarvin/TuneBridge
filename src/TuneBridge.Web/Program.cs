using TuneBridge.Common;
using TuneBridge.Web;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Configure Web services (Identity, authentication, etc.)
_ = builder.ConfigureTuneBridgeServices( args );

// Add HTTP client for API service communication
builder.Services.AddHttpClient<TuneBridgeApiClient>( client => {
    client.BaseAddress = new( "https+http://apiservice" );
} );

// Configure the web application with middleware and database initialization
WebApplication app = await builder.ConfigureWebApp( );

app.MapDefaultEndpoints( );

app.Run( );
