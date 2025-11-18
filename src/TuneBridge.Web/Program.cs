using TuneBridge.Common;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddCommon( );

// Add services to the container.
builder.Services.AddRazorComponents( )
    .AddInteractiveServerComponents( );

builder.Services.AddOutputCache( );

builder.Services.AddHttpClient<TuneBridgeApiClient>( client => {
    client.BaseAddress = new( "https+http://apiservice" );
} );

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment( )) {
    _ = app.UseExceptionHandler( "/Error", createScopeForErrors: true );
    _ = app.UseHsts( );
}

app.UseHttpsRedirection( );

app.UseAntiforgery( );

app.UseOutputCache( );

app.MapStaticAssets( );

//app.MapRazorComponents<App>( )
//    .AddInteractiveServerRenderMode( );

app.MapDefaultEndpoints( );

app.Run( );
