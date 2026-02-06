using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder( args );

// ============================================================================
// Environment Detection
// ============================================================================
// In production (Docker), we use AddExecutable() with pre-published DLLs.
// In development, we use AddProject<T>() for hot reload and debugging.
bool isProduction = builder.Environment.IsProduction();

// ============================================================================
// Configuration Source
// ============================================================================
// Aspire parameters can be provided via:
// 1. Environment variables prefixed with "Parameters__" (e.g., Parameters__SpotifyClientId)
// 2. Configuration section "Parameters:" in appsettings.json or user secrets
// 3. Command line arguments (e.g., --Parameters:SpotifyClientId=value)
// The builder.Configuration object merges all sources, enabling flexible testing.
IConfiguration config = builder.Configuration;

// Music Provider Parameters - At least one music provider is required for the application to function
IResourceBuilder<ParameterResource> spotifyClientId = builder.AddParameter( "SpotifyClientId", secret: true );
IResourceBuilder<ParameterResource> spotifyClientSecret = builder.AddParameter( "SpotifyClientSecret", secret: true );
IResourceBuilder<ParameterResource> appleTeamId = builder.AddParameter( "AppleTeamId", secret: true );
IResourceBuilder<ParameterResource> appleKeyId = builder.AddParameter( "AppleKeyId", secret: true );
IResourceBuilder<ParameterResource> appleKeyPath = builder.AddParameter( "AppleKeyPath" );
IResourceBuilder<ParameterResource> tidalClientId = builder.AddParameter( "TidalClientId", secret: true );
IResourceBuilder<ParameterResource> tidalClientSecret = builder.AddParameter( "TidalClientSecret", secret: true );

// Discord Bot Parameter
IResourceBuilder<ParameterResource> discordToken = builder.AddParameter( "DiscordToken", secret: true );

// ATProto (Bluesky) Parameters
IResourceBuilder<ParameterResource> atProtoIdentifier = builder.AddParameter( "ATProtoIdentifier", secret: true );
IResourceBuilder<ParameterResource> atProtoPassword = builder.AddParameter( "ATProtoPassword", secret: true );
IResourceBuilder<ParameterResource> atProtoUserDID = builder.AddParameter( "ATProtoUserDID", secret: true );
IResourceBuilder<ParameterResource> atProtoPdsUri = builder.AddParameter( "ATProtoPdsUri" );

// Security Parameters
IResourceBuilder<ParameterResource> apiKeySalt = builder.AddParameter( "ApiKeySalt", secret: true );

// Additional configuration parameters
IResourceBuilder<ParameterResource> nodeNumber = builder.AddParameter( "NodeNumber" );
IResourceBuilder<ParameterResource> baseUrl = builder.AddParameter( "BaseUrl" );
IResourceBuilder<ParameterResource> rateLimitRequestsPerHour = builder.AddParameter( "RateLimitRequestsPerHour" );
IResourceBuilder<ParameterResource> cacheDays = builder.AddParameter( "CacheDays" );
IResourceBuilder<ParameterResource> identityConnectionString = builder.AddParameter( "IdentityConnectionString" );
IResourceBuilder<ParameterResource> logDirPath = builder.AddParameter( "LogDirPath" );
IResourceBuilder<ParameterResource> cardCacheExpirationHours = builder.AddParameter( "CardCacheExpirationHours" );
IResourceBuilder<ParameterResource> cardCacheCleanupInterval = builder.AddParameter( "CardCacheCleanupInterval" );

// Resilience configuration parameters
IResourceBuilder<ParameterResource> resilienceMaxRetryAfterSeconds = builder.AddParameter( "ResilienceMaxRetryAfterSeconds" );
IResourceBuilder<ParameterResource> resilienceMaxRetryAttempts = builder.AddParameter( "ResilienceMaxRetryAttempts" );
IResourceBuilder<ParameterResource> resilienceTotalTimeoutMinutes = builder.AddParameter( "ResilienceTotalTimeoutMinutes" );
IResourceBuilder<ParameterResource> resilienceAttemptTimeoutSeconds = builder.AddParameter( "ResilienceAttemptTimeoutSeconds" );

// ============================================================================
// Redis Cache
// ============================================================================
// Redis connection string is read from ConnectionStrings:redis in configuration.
// In Docker: set via entrypoint.sh in appsettings.json and ConnectionStrings__redis env var
// In development: set in user secrets as ConnectionStrings:redis
// Using AddConnectionString enables WaitFor() to ensure workers don't start until Redis is available.
IResourceBuilder<IResourceWithConnectionString> redis = builder.AddConnectionString( "redis" );

// Validate Redis connection string is configured
if (string.IsNullOrWhiteSpace( config["ConnectionStrings:redis"] )) {
    Console.Error.WriteLine( "ERROR: Redis connection string is required but not configured." );
    Console.Error.WriteLine( "       Set ConnectionStrings:redis in appsettings.json or ConnectionStrings__redis environment variable." );
}

// Helper to check if a provider is enabled (has credentials)
// Uses configuration which merges environment variables, appsettings, user secrets, and command line args
bool HasSpotifyCredentials( ) =>
    !string.IsNullOrWhiteSpace( config["Parameters:SpotifyClientId"] ) &&
    !string.IsNullOrWhiteSpace( config["Parameters:SpotifyClientSecret"] );
bool HasAppleMusicCredentials( ) =>
    !string.IsNullOrWhiteSpace( config["Parameters:AppleTeamId"] ) &&
    !string.IsNullOrWhiteSpace( config["Parameters:AppleKeyId"] ) &&
    !string.IsNullOrWhiteSpace( config["Parameters:AppleKeyPath"] );
bool HasTidalCredentials( ) =>
    !string.IsNullOrWhiteSpace( config["Parameters:TidalClientId"] ) &&
    !string.IsNullOrWhiteSpace( config["Parameters:TidalClientSecret"] );
bool HasDiscordCredentials( ) => !string.IsNullOrWhiteSpace( config["Parameters:DiscordToken"] );

// ============================================================================
// Resource Addition Helpers
// ============================================================================
// In production, we use AddExecutable() with pre-published DLLs at /src/{ProjectName}/
// In development, we use AddProject<T>() for hot reload and debugging.

IResourceBuilder<ExecutableResource> AddProductionExecutable(
    string name,
    string projectName,
    int? httpPort = null,
    string workingDirectory = "/app/data"
) {
    string dllPath = $"/src/{projectName}/{projectName}.dll";
    IResourceBuilder<ExecutableResource> resource = builder.AddExecutable( name, "dotnet", workingDirectory, dllPath )
        .WithReference( redis )
        .WaitFor( redis );

    if (httpPort.HasValue) {
        resource = resource.WithHttpEndpoint( targetPort: httpPort.Value, name: "http" );
    }

    return resource;
}

// ============================================================================
// Worker tracking for service discovery
// ============================================================================
// Track references for WithReference() calls in both development and production
IResourceBuilder<ProjectResource>? spotifyWorkerProject = null;
IResourceBuilder<ProjectResource>? appleMusicWorkerProject = null;
IResourceBuilder<ProjectResource>? tidalWorkerProject = null;
IResourceBuilder<ProjectResource>? discordWorkerProject = null;

IResourceBuilder<ExecutableResource>? _ = null;

IResourceBuilder<ExecutableResource>? discordWorkerExe = null;
IResourceBuilder<ExecutableResource>? bridgebeatsWebExe = null;

// Track which workers are enabled for web app configuration
bool spotifyWorkerEnabled = HasSpotifyCredentials();
bool appleMusicWorkerEnabled = HasAppleMusicCredentials();
bool tidalWorkerEnabled = HasTidalCredentials();

// ============================================================================
// Spotify Worker
// ============================================================================
if (spotifyWorkerEnabled) {
    if (isProduction) {
        _ = AddProductionExecutable( "spotify-worker", "BridgeBeats.Worker.Spotify", httpPort: 5100 )
            .WithEnvironment( "ASPNETCORE_HTTP_PORTS", "5100" )
            .WithEnvironment( "BridgeBeats__LogDirPath", logDirPath )
            .WithEnvironment( "BridgeBeats__SpotifyClientId", spotifyClientId )
            .WithEnvironment( "BridgeBeats__SpotifyClientSecret", spotifyClientSecret )
            .WithEnvironment( "BridgeBeats__Resilience__MaxRetryAfterSeconds", resilienceMaxRetryAfterSeconds )
            .WithEnvironment( "BridgeBeats__Resilience__MaxRetryAttempts", resilienceMaxRetryAttempts )
            .WithEnvironment( "BridgeBeats__Resilience__TotalTimeoutMinutes", resilienceTotalTimeoutMinutes )
            .WithEnvironment( "BridgeBeats__Resilience__AttemptTimeoutSeconds", resilienceAttemptTimeoutSeconds );
    } else {
        spotifyWorkerProject = builder.AddProject<Projects.BridgeBeats_Worker_Spotify>( "spotify-worker" )
            .WithHttpEndpoint( targetPort: 5100, name: "http" );
        _ = (IResourceBuilder<ExecutableResource>?)spotifyWorkerProject
            .WithReference( redis )
            .WaitFor( redis )
            .WithEnvironment( "ASPNETCORE_HTTP_PORTS", "5100" )
            .WithEnvironment( "BridgeBeats__LogDirPath", logDirPath )
            .WithEnvironment( "BridgeBeats__SpotifyClientId", spotifyClientId )
            .WithEnvironment( "BridgeBeats__SpotifyClientSecret", spotifyClientSecret )
            .WithEnvironment( "BridgeBeats__Resilience__MaxRetryAfterSeconds", resilienceMaxRetryAfterSeconds )
            .WithEnvironment( "BridgeBeats__Resilience__MaxRetryAttempts", resilienceMaxRetryAttempts )
            .WithEnvironment( "BridgeBeats__Resilience__TotalTimeoutMinutes", resilienceTotalTimeoutMinutes )
            .WithEnvironment( "BridgeBeats__Resilience__AttemptTimeoutSeconds", resilienceAttemptTimeoutSeconds );
    }
}

// ============================================================================
// Apple Music Worker
// ============================================================================
if (appleMusicWorkerEnabled) {
    if (isProduction) {
        _ = AddProductionExecutable( "applemusic-worker", "BridgeBeats.Worker.AppleMusic", httpPort: 5101 )
            .WithEnvironment( "ASPNETCORE_HTTP_PORTS", "5101" )
            .WithEnvironment( "BridgeBeats__LogDirPath", logDirPath )
            .WithEnvironment( "BridgeBeats__AppleTeamId", appleTeamId )
            .WithEnvironment( "BridgeBeats__AppleKeyId", appleKeyId )
            .WithEnvironment( "BridgeBeats__AppleKeyPath", appleKeyPath )
            .WithEnvironment( "BridgeBeats__Resilience__MaxRetryAfterSeconds", resilienceMaxRetryAfterSeconds )
            .WithEnvironment( "BridgeBeats__Resilience__MaxRetryAttempts", resilienceMaxRetryAttempts )
            .WithEnvironment( "BridgeBeats__Resilience__TotalTimeoutMinutes", resilienceTotalTimeoutMinutes )
            .WithEnvironment( "BridgeBeats__Resilience__AttemptTimeoutSeconds", resilienceAttemptTimeoutSeconds );
    } else {
        appleMusicWorkerProject = builder.AddProject<Projects.BridgeBeats_Worker_AppleMusic>( "applemusic-worker" )
            .WithHttpEndpoint( targetPort: 5101, name: "http" );
        _ = (IResourceBuilder<ExecutableResource>?)appleMusicWorkerProject
            .WithReference( redis )
            .WaitFor( redis )
            .WithEnvironment( "ASPNETCORE_HTTP_PORTS", "5101" )
            .WithEnvironment( "BridgeBeats__LogDirPath", logDirPath )
            .WithEnvironment( "BridgeBeats__AppleTeamId", appleTeamId )
            .WithEnvironment( "BridgeBeats__AppleKeyId", appleKeyId )
            .WithEnvironment( "BridgeBeats__AppleKeyPath", appleKeyPath )
            .WithEnvironment( "BridgeBeats__Resilience__MaxRetryAfterSeconds", resilienceMaxRetryAfterSeconds )
            .WithEnvironment( "BridgeBeats__Resilience__MaxRetryAttempts", resilienceMaxRetryAttempts )
            .WithEnvironment( "BridgeBeats__Resilience__TotalTimeoutMinutes", resilienceTotalTimeoutMinutes )
            .WithEnvironment( "BridgeBeats__Resilience__AttemptTimeoutSeconds", resilienceAttemptTimeoutSeconds );
    }
}

// ============================================================================
// Tidal Worker
// ============================================================================
if (tidalWorkerEnabled) {
    if (isProduction) {
        _ = AddProductionExecutable( "tidal-worker", "BridgeBeats.Worker.Tidal", httpPort: 5102 )
            .WithEnvironment( "ASPNETCORE_HTTP_PORTS", "5102" )
            .WithEnvironment( "BridgeBeats__LogDirPath", logDirPath )
            .WithEnvironment( "BridgeBeats__TidalClientId", tidalClientId )
            .WithEnvironment( "BridgeBeats__TidalClientSecret", tidalClientSecret )
            .WithEnvironment( "BridgeBeats__Resilience__MaxRetryAfterSeconds", resilienceMaxRetryAfterSeconds )
            .WithEnvironment( "BridgeBeats__Resilience__MaxRetryAttempts", resilienceMaxRetryAttempts )
            .WithEnvironment( "BridgeBeats__Resilience__TotalTimeoutMinutes", resilienceTotalTimeoutMinutes )
            .WithEnvironment( "BridgeBeats__Resilience__AttemptTimeoutSeconds", resilienceAttemptTimeoutSeconds );
    } else {
        tidalWorkerProject = builder.AddProject<Projects.BridgeBeats_Worker_Tidal>( "tidal-worker" )
            .WithHttpEndpoint( targetPort: 5102, name: "http" );
        _ = (IResourceBuilder<ExecutableResource>?)tidalWorkerProject
            .WithReference( redis )
            .WaitFor( redis )
            .WithEnvironment( "ASPNETCORE_HTTP_PORTS", "5102" )
            .WithEnvironment( "BridgeBeats__LogDirPath", logDirPath )
            .WithEnvironment( "BridgeBeats__TidalClientId", tidalClientId )
            .WithEnvironment( "BridgeBeats__TidalClientSecret", tidalClientSecret )
            .WithEnvironment( "BridgeBeats__Resilience__MaxRetryAfterSeconds", resilienceMaxRetryAfterSeconds )
            .WithEnvironment( "BridgeBeats__Resilience__MaxRetryAttempts", resilienceMaxRetryAttempts )
            .WithEnvironment( "BridgeBeats__Resilience__TotalTimeoutMinutes", resilienceTotalTimeoutMinutes )
            .WithEnvironment( "BridgeBeats__Resilience__AttemptTimeoutSeconds", resilienceAttemptTimeoutSeconds );
    }
}

// ============================================================================
// Discord Worker
// ============================================================================
// Handles Discord gateway events, detecting music links and calling the
// BridgeBeats Web API for lookups and card generation.
if (HasDiscordCredentials( )) {
    if (isProduction) {
        discordWorkerExe = AddProductionExecutable( "discord-worker", "BridgeBeats.Worker.Discord" );
        _ = discordWorkerExe
            .WithEnvironment( "BridgeBeats__LogDirPath", logDirPath )
            .WithEnvironment( "BridgeBeats__DiscordToken", discordToken )
            .WithEnvironment( "BridgeBeats__NodeNumber", nodeNumber )
            .WithEnvironment( "BridgeBeats__BaseUrl", baseUrl );
    } else {
        discordWorkerProject = builder.AddProject<Projects.BridgeBeats_Worker_Discord>( "discord-worker" );
        _ = (IResourceBuilder<ExecutableResource>?)discordWorkerProject
            .WithReference( redis )
            .WaitFor( redis )
            .WithEnvironment( "BridgeBeats__LogDirPath", logDirPath )
            .WithEnvironment( "BridgeBeats__DiscordToken", discordToken )
            .WithEnvironment( "BridgeBeats__NodeNumber", nodeNumber )
            .WithEnvironment( "BridgeBeats__BaseUrl", baseUrl );
    }
}

// ============================================================================
// Saga Coordinator Worker
// ============================================================================
// The saga coordinator monitors for completed multi-provider lookups and
// writes final results to ATProto. It uses Redis Pub/Sub for event-driven
// coordination and polling as a fallback.

// Build list of enabled providers for the saga coordinator
List<string> enabledProvidersList = [];
if (spotifyWorkerEnabled) {
    enabledProvidersList.Add( "Spotify" );
}
if (appleMusicWorkerEnabled) {
    enabledProvidersList.Add( "AppleMusic" );
}
if (tidalWorkerEnabled) {
    enabledProvidersList.Add( "Tidal" );
}
string enabledProvidersValue = string.Join( ",", enabledProvidersList );

_ = isProduction
    ? AddProductionExecutable( "saga-coordinator", "BridgeBeats.Worker.SagaCoordinator" )
        .WithEnvironment( "BridgeBeats__LogDirPath", logDirPath )
        .WithEnvironment( "BridgeBeats__ATProtoIdentifier", atProtoIdentifier )
        .WithEnvironment( "BridgeBeats__ATProtoPassword", atProtoPassword )
        .WithEnvironment( "BridgeBeats__ATProtoUserDID", atProtoUserDID )
        .WithEnvironment( "BridgeBeats__CacheDays", cacheDays )
        .WithEnvironment( "BridgeBeats__EnabledProviders", enabledProvidersValue )
    : (IResourceBuilder<ExecutableResource>)builder.AddProject<Projects.BridgeBeats_Worker_SagaCoordinator>( "saga-coordinator" )
        .WithReference( redis )
        .WaitFor( redis )
        .WithEnvironment( "BridgeBeats__LogDirPath", logDirPath )
        .WithEnvironment( "BridgeBeats__ATProtoIdentifier", atProtoIdentifier )
        .WithEnvironment( "BridgeBeats__ATProtoPassword", atProtoPassword )
        .WithEnvironment( "BridgeBeats__ATProtoUserDID", atProtoUserDID )
        .WithEnvironment( "BridgeBeats__CacheDays", cacheDays )
        .WithEnvironment( "BridgeBeats__EnabledProviders", enabledProvidersValue );

// ============================================================================
// JetStream Watcher Worker
// ============================================================================
// Monitors Bluesky Jetstream for music links and submits them to provider
// queues at bulk priority. Fire-and-forget: no credentials needed.
_ = isProduction
    ? AddProductionExecutable( "jetstream-watcher", "BridgeBeats.Worker.JetStreamWatcher" )
        .WithEnvironment( "BridgeBeats__LogDirPath", logDirPath )
    : (IResourceBuilder<ExecutableResource>)builder.AddProject<Projects.BridgeBeats_Worker_JetStreamWatcher>( "jetstream-watcher" )
        .WithReference( redis )
        .WaitFor( redis )
        .WithEnvironment( "BridgeBeats__LogDirPath", logDirPath );

// ============================================================================
// Cache Bootstrap Worker
// ============================================================================
// Bootstraps Redis cache from ATProto public records on startup and periodically
// (default: every 6 hours). Uses unauthenticated access for public records.
_ = isProduction
    ? AddProductionExecutable( "cache-bootstrap", "BridgeBeats.Worker.CacheBootstrap" )
        .WithEnvironment( "BridgeBeats__LogDirPath", logDirPath )
        .WithEnvironment( "BridgeBeats__ATProtoIdentifier", atProtoIdentifier )
        .WithEnvironment( "BridgeBeats__ATProtoPassword", atProtoPassword )
        .WithEnvironment( "BridgeBeats__ATProtoUserDID", atProtoUserDID )
        .WithEnvironment( "BridgeBeats__ATProtoPdsUri", atProtoPdsUri )
        .WithEnvironment( "BridgeBeats__CacheDays", cacheDays )
    : (IResourceBuilder<ExecutableResource>)builder.AddProject<Projects.BridgeBeats_Worker_CacheBootstrap>( "cache-bootstrap" )
        .WithReference( redis )
        .WaitFor( redis )
        .WithEnvironment( "BridgeBeats__LogDirPath", logDirPath )
        .WithEnvironment( "BridgeBeats__ATProtoIdentifier", atProtoIdentifier )
        .WithEnvironment( "BridgeBeats__ATProtoPassword", atProtoPassword )
        .WithEnvironment( "BridgeBeats__ATProtoUserDID", atProtoUserDID )
        .WithEnvironment( "BridgeBeats__ATProtoPdsUri", atProtoPdsUri )
        .WithEnvironment( "BridgeBeats__CacheDays", cacheDays );

// ============================================================================
// Main Web Application
// ============================================================================
// In production, service discovery uses resource names for endpoint resolution.
// Worker references are registered via WithReference() for service discovery.
IResourceBuilder<ProjectResource>? bridgebeatsWebProject = null;

if (isProduction) {
    bridgebeatsWebExe = AddProductionExecutable( "bridgebeats", "BridgeBeats.Web", httpPort: 10000, workingDirectory: "/src/BridgeBeats.Web" );
    _ = bridgebeatsWebExe
        .WithHttpEndpoint( port: 10000, targetPort: 10000, name: "bridgebeats-http", isProxied: false )
        .WithEnvironment( "ASPNETCORE_HTTP_PORTS", "10000" )
        .WithEnvironment( "BridgeBeats__Workers__UseWorkerServices", "true" )
        .WithEnvironment( "BridgeBeats__Workers__SpotifyWorkerEnabled", spotifyWorkerEnabled ? "true" : "false" )
        .WithEnvironment( "BridgeBeats__Workers__AppleMusicWorkerEnabled", appleMusicWorkerEnabled ? "true" : "false" )
        .WithEnvironment( "BridgeBeats__Workers__TidalWorkerEnabled", tidalWorkerEnabled ? "true" : "false" )
        .WithEnvironment( "BridgeBeats__ATProtoIdentifier", atProtoIdentifier )
        .WithEnvironment( "BridgeBeats__ATProtoPassword", atProtoPassword )
        .WithEnvironment( "BridgeBeats__ATProtoUserDID", atProtoUserDID )
        .WithEnvironment( "BridgeBeats__ATProtoPdsUri", atProtoPdsUri )
        .WithEnvironment( "BridgeBeats__ApiKeySalt", apiKeySalt )
        .WithEnvironment( "BridgeBeats__BaseUrl", baseUrl )
        .WithEnvironment( "BridgeBeats__RateLimitRequestsPerHour", rateLimitRequestsPerHour )
        .WithEnvironment( "BridgeBeats__CacheDays", cacheDays )
        .WithEnvironment( "BridgeBeats__IdentityConnectionString", identityConnectionString )
        .WithEnvironment( "BridgeBeats__LogDirPath", logDirPath )
        .WithEnvironment( "BridgeBeats__CardCacheExpirationHours", cardCacheExpirationHours )
        .WithEnvironment( "BridgeBeats__CardCacheCleanupInterval", cardCacheCleanupInterval )
        .WithEnvironment( "BridgeBeats__Resilience__MaxRetryAfterSeconds", resilienceMaxRetryAfterSeconds )
        .WithEnvironment( "BridgeBeats__Resilience__MaxRetryAttempts", resilienceMaxRetryAttempts )
        .WithEnvironment( "BridgeBeats__Resilience__TotalTimeoutMinutes", resilienceTotalTimeoutMinutes )
        .WithEnvironment( "BridgeBeats__Resilience__AttemptTimeoutSeconds", resilienceAttemptTimeoutSeconds );
} else {
    bridgebeatsWebProject = builder.AddProject<Projects.BridgeBeats_Web>( "bridgebeats" )
        .WithHttpEndpoint( port: 10000, targetPort: 10000, name: "bridgebeats-http" );
    _ = (IResourceBuilder<ExecutableResource>)bridgebeatsWebProject
        .WithReference( redis )
        .WaitFor( redis )
        .WithEnvironment( "ASPNETCORE_HTTP_PORTS", "10000" )
        .WithEnvironment( "BridgeBeats__Workers__UseWorkerServices", "true" )
        .WithEnvironment( "BridgeBeats__Workers__SpotifyWorkerEnabled", spotifyWorkerEnabled ? "true" : "false" )
        .WithEnvironment( "BridgeBeats__Workers__AppleMusicWorkerEnabled", appleMusicWorkerEnabled ? "true" : "false" )
        .WithEnvironment( "BridgeBeats__Workers__TidalWorkerEnabled", tidalWorkerEnabled ? "true" : "false" )
        .WithEnvironment( "BridgeBeats__ATProtoIdentifier", atProtoIdentifier )
        .WithEnvironment( "BridgeBeats__ATProtoPassword", atProtoPassword )
        .WithEnvironment( "BridgeBeats__ATProtoUserDID", atProtoUserDID )
        .WithEnvironment( "BridgeBeats__ATProtoPdsUri", atProtoPdsUri )
        .WithEnvironment( "BridgeBeats__ApiKeySalt", apiKeySalt )
        .WithEnvironment( "BridgeBeats__BaseUrl", baseUrl )
        .WithEnvironment( "BridgeBeats__RateLimitRequestsPerHour", rateLimitRequestsPerHour )
        .WithEnvironment( "BridgeBeats__CacheDays", cacheDays )
        .WithEnvironment( "BridgeBeats__IdentityConnectionString", identityConnectionString )
        .WithEnvironment( "BridgeBeats__LogDirPath", logDirPath )
        .WithEnvironment( "BridgeBeats__CardCacheExpirationHours", cardCacheExpirationHours )
        .WithEnvironment( "BridgeBeats__CardCacheCleanupInterval", cardCacheCleanupInterval )
        .WithEnvironment( "BridgeBeats__Resilience__MaxRetryAfterSeconds", resilienceMaxRetryAfterSeconds )
        .WithEnvironment( "BridgeBeats__Resilience__MaxRetryAttempts", resilienceMaxRetryAttempts )
        .WithEnvironment( "BridgeBeats__Resilience__TotalTimeoutMinutes", resilienceTotalTimeoutMinutes )
        .WithEnvironment( "BridgeBeats__Resilience__AttemptTimeoutSeconds", resilienceAttemptTimeoutSeconds );
}

// ============================================================================
// Service Discovery References
// ============================================================================
// Wire up service discovery between web app and workers.
// Development: Uses WithReference() to inject services__<name>__http__0 environment variables.
// Production: Uses WithEnvironment() to directly set localhost URLs with known ports.

if (isProduction) {
    // Production: Wire up executable resources with explicit localhost URLs
    // In Docker, all services run on localhost with known ports
    if (bridgebeatsWebExe is not null) {
        _ = bridgebeatsWebExe
            .WithEnvironment( "services__spotify-worker__http__0", "http://localhost:5100" )
            .WithEnvironment( "services__applemusic-worker__http__0", "http://localhost:5101" )
            .WithEnvironment( "services__tidal-worker__http__0", "http://localhost:5102" );
    }
    if (discordWorkerExe is not null) {
        _ = discordWorkerExe
            .WithEnvironment( "services__bridgebeats__http__0", "http://localhost:10000" );
    }
} else {
    // Development: Wire up project resources
    if (bridgebeatsWebProject is not null) {
        if (spotifyWorkerProject is not null) {
            _ = (IResourceBuilder<ExecutableResource>)bridgebeatsWebProject.WithReference( spotifyWorkerProject );
        }
        if (appleMusicWorkerProject is not null) {
            _ = (IResourceBuilder<ExecutableResource>)bridgebeatsWebProject.WithReference( appleMusicWorkerProject );
        }
        if (tidalWorkerProject is not null) {
            _ = (IResourceBuilder<ExecutableResource>)bridgebeatsWebProject.WithReference( tidalWorkerProject );
        }
    }
    if (discordWorkerProject is not null && bridgebeatsWebProject is not null) {
        _ = (IResourceBuilder<ExecutableResource>)discordWorkerProject.WithReference( bridgebeatsWebProject );
    }
}

builder.Build( ).Run( );
