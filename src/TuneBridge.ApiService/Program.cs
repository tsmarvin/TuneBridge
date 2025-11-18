using TuneBridge.Common;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddCommon( );

// Add services to the container.
builder.Services.AddProblemDetails( );

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi( );

WebApplication app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler( );

if (app.Environment.IsDevelopment( )) {
    _ = app.MapOpenApi( );
}

app.MapDefaultEndpoints( );

app.Run( );
