using TuneBridge.Common;
using TuneBridge.Core.Domain.Implementations.Extensions;

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

// Configure the HTTP request pipeline.
app.UseExceptionHandler( );

if (app.Environment.IsDevelopment( )) {
    _ = app.MapOpenApi( );
}

app.UseRouting( );
app.UseAuthentication( );
app.UseAuthorization( );

app.MapControllers( );
app.MapDefaultEndpoints( );

app.Run( );
