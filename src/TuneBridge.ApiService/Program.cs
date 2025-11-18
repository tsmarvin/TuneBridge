using Microsoft.EntityFrameworkCore;
using TuneBridge.Common;
using TuneBridge.Core.Domain.Implementations.Extensions;
using TuneBridge.Core.Infrastructure.Context;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Configure TuneBridge Core services (business logic, music providers, etc.)
_ = builder.ConfigureTuneBridgeServices( args );

// Add API controllers
builder.Services.AddControllers( );

// Add problem details for API error handling
builder.Services.AddProblemDetails( );

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi( );

WebApplication app = builder.Build();

// Initialize database
using (var scope = app.Services.CreateScope()) {
    var context = scope.ServiceProvider.GetRequiredService<MediaLinkCacheDbContext>();
    await context.Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
app.UseExceptionHandler( );

if (app.Environment.IsDevelopment( )) {
    _ = app.MapOpenApi( );
}

app.UseRouting( );
// Note: ApiService is internal-only, accessed via service discovery from Web
// Authentication is handled by the Web layer before calling this service

app.MapControllers( );
app.MapDefaultEndpoints( );

app.Run( );
