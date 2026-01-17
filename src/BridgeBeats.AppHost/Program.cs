IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// Music Provider Parameters - Aspire reads from environment variables prefixed with "Parameters__"
// At least one music provider is required for the application to function
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

// Security Parameters
IResourceBuilder<ParameterResource> apiKeySalt = builder.AddParameter( "ApiKeySalt", secret: true );

// Additional configuration parameters
IResourceBuilder<ParameterResource> nodeNumber = builder.AddParameter( "NodeNumber" );
IResourceBuilder<ParameterResource> baseUrl = builder.AddParameter( "BaseUrl" );
IResourceBuilder<ParameterResource> rateLimitRequestsPerHour = builder.AddParameter( "RateLimitRequestsPerHour" );
IResourceBuilder<ParameterResource> cacheDays = builder.AddParameter( "CacheDays" );
IResourceBuilder<ParameterResource> linkCacheConnectionString = builder.AddParameter( "LinkCacheConnectionString" );
IResourceBuilder<ParameterResource> identityConnectionString = builder.AddParameter( "IdentityConnectionString" );
IResourceBuilder<ParameterResource> logFilePath = builder.AddParameter( "LogFilePath" );
IResourceBuilder<ParameterResource> cardCacheExpirationHours = builder.AddParameter( "CardCacheExpirationHours" );
IResourceBuilder<ParameterResource> cardCacheCleanupInterval = builder.AddParameter( "CardCacheCleanupInterval" );

_ = builder.AddProject<Projects.BridgeBeats_Web>( "bridgebeats" )
    .WithHttpEndpoint( port: 10000, name: "http" )
    // Spotify
    .WithEnvironment( "BridgeBeats__SpotifyClientId", spotifyClientId )
    .WithEnvironment( "BridgeBeats__SpotifyClientSecret", spotifyClientSecret )
    // Apple Music
    .WithEnvironment( "BridgeBeats__AppleTeamId", appleTeamId )
    .WithEnvironment( "BridgeBeats__AppleKeyId", appleKeyId )
    .WithEnvironment( "BridgeBeats__AppleKeyPath", appleKeyPath )
    // Tidal
    .WithEnvironment( "BridgeBeats__TidalClientId", tidalClientId )
    .WithEnvironment( "BridgeBeats__TidalClientSecret", tidalClientSecret )
    // Discord
    .WithEnvironment( "BridgeBeats__DiscordToken", discordToken )
    .WithEnvironment( "BridgeBeats__NodeNumber", nodeNumber )
    // ATProto (Bluesky)
    .WithEnvironment( "BridgeBeats__ATProtoIdentifier", atProtoIdentifier )
    .WithEnvironment( "BridgeBeats__ATProtoPassword", atProtoPassword )
    .WithEnvironment( "BridgeBeats__ATProtoUserDID", atProtoUserDID )
    // Security
    .WithEnvironment( "BridgeBeats__ApiKeySalt", apiKeySalt )
    // Application Configuration
    .WithEnvironment( "BridgeBeats__BaseUrl", baseUrl )
    .WithEnvironment( "BridgeBeats__RateLimitRequestsPerHour", rateLimitRequestsPerHour )
    .WithEnvironment( "BridgeBeats__CacheDays", cacheDays )
    .WithEnvironment( "BridgeBeats__LinkCacheConnectionString", linkCacheConnectionString )
    .WithEnvironment( "BridgeBeats__IdentityConnectionString", identityConnectionString )
    .WithEnvironment( "BridgeBeats__LogFilePath", logFilePath )
    .WithEnvironment( "BridgeBeats__CardCacheExpirationHours", cardCacheExpirationHours )
    .WithEnvironment( "BridgeBeats__CardCacheCleanupInterval", cardCacheCleanupInterval );

builder.Build( ).Run( );
