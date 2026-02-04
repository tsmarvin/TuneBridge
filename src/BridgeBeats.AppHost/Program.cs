using Microsoft.Extensions.Configuration;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder( args );

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
IResourceBuilder<ParameterResource> logDirPath = builder.AddParameter("LogDirPath");
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
IResourceBuilder<IResourceWithConnectionString> redis = builder.AddConnectionString("redis");

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
// Spotify Worker
// ============================================================================
IResourceBuilder<ProjectResource>? spotifyWorker = null;
if (HasSpotifyCredentials( )) {
    spotifyWorker = builder.AddProject<Projects.BridgeBeats_Worker_Spotify>( "spotify-worker" )
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
IResourceBuilder<ProjectResource>? appleMusicWorker = null;
if (HasAppleMusicCredentials( )) {
    appleMusicWorker = builder.AddProject<Projects.BridgeBeats_Worker_AppleMusic>( "applemusic-worker" )
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
IResourceBuilder<ProjectResource>? tidalWorker = null;
if (HasTidalCredentials( )) {
    tidalWorker = builder.AddProject<Projects.BridgeBeats_Worker_Tidal>( "tidal-worker" )
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
IResourceBuilder<ProjectResource>? discordWorker = null;
if (HasDiscordCredentials( )) {
    discordWorker = builder.AddProject<Projects.BridgeBeats_Worker_Discord>( "discord-worker" )
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
if (HasSpotifyCredentials( )) {
    enabledProvidersList.Add( "Spotify" );
}
if (HasAppleMusicCredentials( )) {
    enabledProvidersList.Add( "AppleMusic" );
}
if (HasTidalCredentials( )) {
    enabledProvidersList.Add( "Tidal" );
}
string enabledProvidersValue = string.Join( ",", enabledProvidersList );

_ = builder.AddProject<Projects.BridgeBeats_Worker_SagaCoordinator>( "saga-coordinator" )
    .WithReference( redis )
    .WithEnvironment( "BridgeBeats__LogDirPath", logDirPath )
    // ATProto (Bluesky) - required for writing final results
    .WithEnvironment( "BridgeBeats__ATProtoIdentifier", atProtoIdentifier )
    .WithEnvironment( "BridgeBeats__ATProtoPassword", atProtoPassword )
    .WithEnvironment( "BridgeBeats__ATProtoUserDID", atProtoUserDID )
    // Cache configuration
    .WithEnvironment( "BridgeBeats__CacheDays", cacheDays )
    // Enabled providers (comma-separated list)
    .WithEnvironment( "BridgeBeats__EnabledProviders", enabledProvidersValue );

// ============================================================================
// JetStream Watcher Worker
// ============================================================================
// Monitors Bluesky Jetstream for music links and submits them to provider
// queues at bulk priority. Fire-and-forget: no credentials needed.
_ = builder.AddProject<Projects.BridgeBeats_Worker_JetStreamWatcher>( "jetstream-watcher" )
    .WithReference( redis )
    .WithEnvironment( "BridgeBeats__LogDirPath", logDirPath );

// ============================================================================
// Cache Bootstrap Worker
// ============================================================================
// Bootstraps Redis cache from ATProto public records on startup and periodically
// (default: every 6 hours). Uses unauthenticated access for public records.
_ = builder.AddProject<Projects.BridgeBeats_Worker_CacheBootstrap>( "cache-bootstrap" )
    .WithReference( redis )
    .WithEnvironment( "BridgeBeats__LogDirPath", logDirPath )
    // ATProto credentials for authenticated storage operations
    .WithEnvironment( "BridgeBeats__ATProtoIdentifier", atProtoIdentifier )
    .WithEnvironment( "BridgeBeats__ATProtoPassword", atProtoPassword )
    .WithEnvironment( "BridgeBeats__ATProtoUserDID", atProtoUserDID )
    .WithEnvironment( "BridgeBeats__ATProtoPdsUri", atProtoPdsUri )
    // Cache configuration
    .WithEnvironment( "BridgeBeats__CacheDays", cacheDays );

// ============================================================================
// Main Web Application
// ============================================================================
IResourceBuilder<ProjectResource> bridgebeatsWeb = builder.AddProject<Projects.BridgeBeats_Web>( "bridgebeats" )
    .WithHttpEndpoint(port: 10000, name: "bridgebeats-http")
    .WithReference( redis )
    // Worker Configuration - Enable worker mode and specify which workers are available
    .WithEnvironment( "BridgeBeats__Workers__UseWorkerServices", "true" )
    .WithEnvironment( "BridgeBeats__Workers__SpotifyWorkerEnabled", spotifyWorker is not null ? "true" : "false" )
    .WithEnvironment( "BridgeBeats__Workers__AppleMusicWorkerEnabled", appleMusicWorker is not null ? "true" : "false" )
    .WithEnvironment( "BridgeBeats__Workers__TidalWorkerEnabled", tidalWorker is not null ? "true" : "false" )
    // ATProto (Bluesky)
    .WithEnvironment( "BridgeBeats__ATProtoIdentifier", atProtoIdentifier )
    .WithEnvironment( "BridgeBeats__ATProtoPassword", atProtoPassword )
    .WithEnvironment( "BridgeBeats__ATProtoUserDID", atProtoUserDID )
    .WithEnvironment( "BridgeBeats__ATProtoPdsUri", atProtoPdsUri )
    // Security
    .WithEnvironment( "BridgeBeats__ApiKeySalt", apiKeySalt )
    // Application Configuration
    .WithEnvironment( "BridgeBeats__BaseUrl", baseUrl )
    .WithEnvironment( "BridgeBeats__RateLimitRequestsPerHour", rateLimitRequestsPerHour )
    .WithEnvironment( "BridgeBeats__CacheDays", cacheDays )
    .WithEnvironment( "BridgeBeats__IdentityConnectionString", identityConnectionString )
    .WithEnvironment("BridgeBeats__LogDirPath", logDirPath)
    .WithEnvironment( "BridgeBeats__CardCacheExpirationHours", cardCacheExpirationHours )
    .WithEnvironment( "BridgeBeats__CardCacheCleanupInterval", cardCacheCleanupInterval )
    // Resilience Configuration
    .WithEnvironment( "BridgeBeats__Resilience__MaxRetryAfterSeconds", resilienceMaxRetryAfterSeconds )
    .WithEnvironment( "BridgeBeats__Resilience__MaxRetryAttempts", resilienceMaxRetryAttempts )
    .WithEnvironment( "BridgeBeats__Resilience__TotalTimeoutMinutes", resilienceTotalTimeoutMinutes )
    .WithEnvironment( "BridgeBeats__Resilience__AttemptTimeoutSeconds", resilienceAttemptTimeoutSeconds );

// Add references from web app to workers for service discovery
if (spotifyWorker is not null) {
    _ = bridgebeatsWeb.WithReference( spotifyWorker );
}
if (appleMusicWorker is not null) {
    _ = bridgebeatsWeb.WithReference( appleMusicWorker );
}
if (tidalWorker is not null) {
    _ = bridgebeatsWeb.WithReference( tidalWorker );
}

// Add reference from Discord worker to web app for API calls
if (discordWorker is not null) {
    _ = discordWorker.WithReference( bridgebeatsWeb );
}

builder.Build( ).Run( );
