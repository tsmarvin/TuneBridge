var builder = DistributedApplication.CreateBuilder( args );

// Add the Aspire Dashboard as a resource
// The dashboard provides observability and telemetry for the distributed application
var dashboard = builder.AddContainer( "aspire-dashboard", "mcr.microsoft.com/dotnet/aspire-dashboard", "13.0" )
    .WithEnvironment( "ASPNETCORE_ENVIRONMENT", "Production" )
    .WithEnvironment( "ASPNETCORE_URLS", "http://+:18888" )
    .WithEnvironment( "DASHBOARD__FRONTEND__AUTHMODE", "Unsecured" )
    .WithEnvironment( "DASHBOARD__OTLP__ENDPOINTURL", "http://+:4317" )
    .WithEnvironment( "DASHBOARD__OTLP__AUTHMODE", "Unsecured" )
    .WithHttpEndpoint( port: 18888, name: "http" )
    .WithEndpoint( port: 4317, name: "otlp", scheme: "http" );

// Add the TuneBridge application as an executable
// TuneBridge is the main web application that provides music link conversion services
string dotnetPath = "dotnet";
string tunebridgeDll = Path.Combine( AppContext.BaseDirectory, "TuneBridge.dll" );

var tunebridge = builder.AddExecutable( "tunebridge", dotnetPath, AppContext.BaseDirectory, tunebridgeDll )
    .WithHttpEndpoint( port: 10000, name: "http" )
    .WithEnvironment( "OTLP_ENDPOINT", $"http://{dashboard.Resource.Name}:4317" );

builder.Build( ).Run( );
