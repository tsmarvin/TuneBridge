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
// Use external Redis container (start manually: docker run -d -p 6379:6379 redis)
// Connection string configured in user secrets or appsettings: ConnectionStrings:redis
IResourceBuilder<IResourceWithConnectionString> redis = builder.AddConnectionString( "redis" );

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

IResourceBuilder<ExecutableResource> AddProductionExecutable( string name, string projectName )
{
    string dllPath = $"/src/{projectName}/{projectName}.dll";
    return builder.AddExecutable( name, "dotnet", "/src", dllPath );
}

// ============================================================================
// Worker tracking for service discovery (development only)
// ============================================================================
// In development mode, we need to keep references for WithReference() calls
IResourceBuilder<ProjectResource>? spotifyWorkerProject = null;
IResourceBuilder<ProjectResource>? appleMusicWorkerProject = null;
IResourceBuilder<ProjectResource>? tidalWorkerProject = null;
IResourceBuilder<ProjectResource>? discordWorkerProject = null;

// Track which workers are enabled for web app configuration
bool spotifyWorkerEnabled = HasSpotifyCredentials();
bool appleMusicWorkerEnabled = HasAppleMusicCredentials();
bool tidalWorkerEnabled = HasTidalCredentials();

// ============================================================================
// Spotify Worker
// ============================================================================
if (spotifyWorkerEnabled) {
    IResourceBuilder<IResourceWithEnvironment> spotifyWorker = isProduction
        ? AddProductionExecutable( "spotify-worker", "BridgeBeats.Worker.Spotify" )
        : spotifyWorkerProject = builder.AddProject<Projects.BridgeBeats_Worker_Spotify>( "spotify-worker" );

    _ = spotifyWorker
        .WithReference( redis )
        .WithEnvironment( "BridgeBeats__LogDirPath", logDirPath )
        .WithEnvironment( "BridgeBeats__SpotifyClientId", spotifyClientId )
        .WithEnvironment( "BridgeBeats__SpotifyClientSecret", spotifyClientSecret )
        .WithEnvironment( "BridgeBeats__Resilience__MaxRetryAfterSeconds", resilienceMaxRetryAfterSeconds )
        .WithEnvironment( "BridgeBeats__Resilience__MaxRetryAttempts", resilienceMaxRetryAttempts )
        .WithEnvironment( "BridgeBeats__Resilience__TotalTimeoutMinutes", resilienceTotalTimeoutMinutes )
        .WithEnvironment( "BridgeBeats__Resilience__AttemptTimeoutSeconds", resilienceAttemptTimeoutSeconds );
}

// ============================================================================
// Apple Music Worker
// ============================================================================
if (appleMusicWorkerEnabled) {
    IResourceBuilder<IResourceWithEnvironment> appleMusicWorker = isProduction
        ? AddProductionExecutable( "applemusic-worker", "BridgeBeats.Worker.AppleMusic" )
        : appleMusicWorkerProject = builder.AddProject<Projects.BridgeBeats_Worker_AppleMusic>( "applemusic-worker" );

    _ = appleMusicWorker
        .WithReference( redis )
        .WithEnvironment( "BridgeBeats__LogDirPath", logDirPath )
        .WithEnvironment( "BridgeBeats__AppleTeamId", appleTeamId )
        .WithEnvironment( "BridgeBeats__AppleKeyId", appleKeyId )
        .WithEnvironment( "BridgeBeats__AppleKeyPath", appleKeyPath )
        .WithEnvironment( "BridgeBeats__Resilience__MaxRetryAfterSeconds", resilienceMaxRetryAfterSeconds )
        .WithEnvironment( "BridgeBeats__Resilience__MaxRetryAttempts", resilienceMaxRetryAttempts )
        .WithEnvironment( "BridgeBeats__Resilience__TotalTimeoutMinutes", resilienceTotalTimeoutMinutes )
        .WithEnvironment( "BridgeBeats__Resilience__AttemptTimeoutSeconds", resilienceAttemptTimeoutSeconds );
}

// ============================================================================
// Tidal Worker
// ============================================================================
if (tidalWorkerEnabled) {
    IResourceBuilder<IResourceWithEnvironment> tidalWorker = isProduction
        ? AddProductionExecutable( "tidal-worker", "BridgeBeats.Worker.Tidal" )
        : tidalWorkerProject = builder.AddProject<Projects.BridgeBeats_Worker_Tidal>( "tidal-worker" );

    _ = tidalWorker
        .WithReference( redis )
        .WithEnvironment( "BridgeBeats__LogDirPath", logDirPath )
        .WithEnvironment( "BridgeBeats__TidalClientId", tidalClientId )
        .WithEnvironment( "BridgeBeats__TidalClientSecret", tidalClientSecret )
        .WithEnvironment( "BridgeBeats__Resilience__MaxRetryAfterSeconds", resilienceMaxRetryAfterSeconds )
        .WithEnvironment( "BridgeBeats__Resilience__MaxRetryAttempts", resilienceMaxRetryAttempts )
        .WithEnvironment( "BridgeBeats__Resilience__TotalTimeoutMinutes", resilienceTotalTimeoutMinutes )
        .WithEnvironment( "BridgeBeats__Resilience__AttemptTimeoutSeconds", resilienceAttemptTimeoutSeconds );
}

// ============================================================================
// Discord Worker
// ============================================================================
// Handles Discord gateway events, detecting music links and calling the
// BridgeBeats Web API for lookups and card generation.
if (HasDiscordCredentials()) {
    IResourceBuilder<IResourceWithEnvironment> discordWorker = isProduction
        ? AddProductionExecutable( "discord-worker", "BridgeBeats.Worker.Discord" )
        : discordWorkerProject = builder.AddProject<Projects.BridgeBeats_Worker_Discord>( "discord-worker" );

    _ = discordWorker
        .WithEnvironment( "BridgeBeats__LogDirPath", logDirPath )
        .WithEnvironment( "BridgeBeats__DiscordToken", discordToken )
        .WithEnvironment( "BridgeBeats__NodeNumber", nodeNumber )
        .WithEnvironment( "BridgeBeats__BaseUrl", baseUrl );
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

IResourceBuilder<IResourceWithEnvironment> sagaCoordinator = isProduction
    ? AddProductionExecutable( "saga-coordinator", "BridgeBeats.Worker.SagaCoordinator" )
    : builder.AddProject<Projects.BridgeBeats_Worker_SagaCoordinator>( "saga-coordinator" );

_ = sagaCoordinator
    .WithReference( redis )
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
IResourceBuilder<IResourceWithEnvironment> jetstreamWatcher = isProduction
    ? AddProductionExecutable( "jetstream-watcher", "BridgeBeats.Worker.JetStreamWatcher" )
    : builder.AddProject<Projects.BridgeBeats_Worker_JetStreamWatcher>( "jetstream-watcher" );

_ = jetstreamWatcher
    .WithReference( redis )
    .WithEnvironment( "BridgeBeats__LogDirPath", logDirPath );

// ============================================================================
// Cache Bootstrap Worker
// ============================================================================
// Bootstraps Redis cache from ATProto public records on startup and periodically
// (default: every 6 hours). Uses unauthenticated access for public records.
IResourceBuilder<IResourceWithEnvironment> cacheBootstrap = isProduction
    ? AddProductionExecutable( "cache-bootstrap", "BridgeBeats.Worker.CacheBootstrap" )
    : builder.AddProject<Projects.BridgeBeats_Worker_CacheBootstrap>( "cache-bootstrap" );

_ = cacheBootstrap
    .WithReference( redis )
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
IResourceBuilder<IResourceWithEnvironment> bridgebeatsWeb;

if (isProduction) {
    bridgebeatsWeb = AddProductionExecutable( "bridgebeats", "BridgeBeats.Web" )
        .WithHttpEndpoint( port: 10000, name: "bridgebeats-http" );
} else {
    bridgebeatsWebProject = builder.AddProject<Projects.BridgeBeats_Web>( "bridgebeats" )
        .WithHttpEndpoint( port: 10000, name: "bridgebeats-http" );
    bridgebeatsWeb = bridgebeatsWebProject;
}

_ = bridgebeatsWeb
    .WithReference( redis )
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

// Development mode: Add service discovery references between workers and web app
if (!isProduction && bridgebeatsWebProject is not null) {
    if (spotifyWorkerProject is not null) {
        _ = bridgebeatsWebProject.WithReference( spotifyWorkerProject );
    }
    if (appleMusicWorkerProject is not null) {
        _ = bridgebeatsWebProject.WithReference( appleMusicWorkerProject );
    }
    if (tidalWorkerProject is not null) {
        _ = bridgebeatsWebProject.WithReference( tidalWorkerProject );
    }
    if (discordWorkerProject is not null) {
        _ = discordWorkerProject.WithReference( bridgebeatsWebProject );
    }
}

builder.Build().Run();
