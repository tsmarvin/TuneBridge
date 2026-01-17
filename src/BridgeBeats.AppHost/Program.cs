IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// Add Docker Compose environment for deployment artifact generation
// Running 'aspire publish' will generate docker-compose.yml and related files
_ = builder.AddDockerComposeEnvironment( "docker-compose" );

// Music Provider Parameters - Aspire reads from AppHost user secrets under "Parameters:{name}"
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

_ = builder.AddProject<Projects.BridgeBeats_Web>( "bridgebeats" )
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
    // ATProto (Bluesky)
    .WithEnvironment( "BridgeBeats__ATProtoIdentifier", atProtoIdentifier )
    .WithEnvironment( "BridgeBeats__ATProtoPassword", atProtoPassword )
    .WithEnvironment( "BridgeBeats__ATProtoUserDID", atProtoUserDID )
    // Security
    .WithEnvironment( "BridgeBeats__ApiKeySalt", apiKeySalt );

builder.Build( ).Run( );
