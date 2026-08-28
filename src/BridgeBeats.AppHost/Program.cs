using System.Reflection;
using System.Text.Json;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

// BridgeBeats Aspire AppHost — the composition root and topology map for the whole system.
//
// This .NET Aspire orchestration host declares every BridgeBeats process and the dependencies
// between them. It is the single best file for understanding the runtime topology, so the wiring
// is described here in prose rather than only in cref links (the AppHost references the other
// projects as Aspire resources, not as code types).
//
// Resources provisioned and wired:
//   - redis: added as a CONNECTION-STRING resource only (AddConnectionString). Aspire does not
//     provision Redis here; an external Redis must exist and ConnectionStrings:redis must be set.
//     Redis is the shared backbone for every component below (work queue, saga state, Pub/Sub).
//   - Provider workers (spotify-worker :5100, applemusic-worker :5101, tidal-worker :5102): each
//     added only when its credentials are present. They expose HTTP for the synchronous WorkerApi
//     and consume the Redis work queue. Spotify additionally runs bulk and genre services.
//   - discord-worker: gateway consumer; no HTTP endpoint. References redis and the Web service.
//   - saga-coordinator: orchestrator/finalizer; no HTTP endpoint. References redis; receives the
//     ATProto credentials and the EnabledProviders CSV that drives secondary fan-out.
//   - jetstream-watcher: ATProto firehose consumer; no HTTP endpoint. References redis.
//   - maintenance: rebuilds the Redis lookup index from the PDS; no HTTP endpoint. References
//     redis and the ATProto credentials/PDS URI.
//   - bridgebeats (Web :10000, unproxied): public API and UI. References redis and every enabled
//     provider worker so it can fan interactive lookups out over the WorkerApi.
//
// Two run modes, branched on builder.Environment.IsProduction():
//   - Development: each component is an Aspire project resource (AddProject) with WithReference
//     wiring, so Aspire injects services__* service-discovery environment variables automatically.
//   - Production: each component is an executable resource (AddExecutable via the local
//     AddProductionExecutable helper) running a published DLL, and service-discovery variables are
//     hand-wired to localhost ports. Production therefore assumes all processes share one host.
//
// AppHost reads one protected database settings revision and projects it to child processes through
// secret-aware Aspire parameters and environment variables. The set of enabled provider workers is
// joined into BridgeBeats__EnabledProviders (CSV) and passed to the saga coordinator and Web.

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder( args );

// Make the standard dotnet user-secrets store an explicit optional first-run source in every
// environment. Environment variables and command-line arguments are re-added afterward so CI and
// deployment-time injection retain their normal precedence.
_ = builder.Configuration
    .AddUserSecrets( Assembly.GetExecutingAssembly( ), optional: true )
    .AddEnvironmentVariables( )
    .AddCommandLine( args );

// Aspire Store Path Override.
// Aspire 13.4+ persists local state (IAspireStore) under the AppHost project's build-time
// intermediate output path (AppHostProjectBaseIntermediateOutputPath assembly metadata), which
// resolves to /build in the published container and does not exist at runtime. The builder seeds
// "Aspire:Store:Path" via an in-memory source added after the environment variable providers, so a
// plain environment variable cannot override it. Set it explicitly when provided.
string? aspireStorePath = Environment.GetEnvironmentVariable( "ASPIRE_STORE_PATH" );
if (!string.IsNullOrWhiteSpace( aspireStorePath )) {
    builder.Configuration["Aspire:Store:Path"] = aspireStorePath;
}

// Environment detection. In production (Docker) we use AddExecutable() with pre-published DLLs;
// in development we use AddProject<T>() for hot reload and debugging.
bool isProduction = builder.Environment.IsProduction();

// Application and provider settings are loaded exclusively from the encrypted database aggregate.
IConfiguration config = builder.Configuration;

// These bootstrap-only values are required before the authoritative settings database can be read.
string identityConnectionStringValue = config["BridgeBeats:IdentityConnectionString"]
    ?? (isProduction
        ? throw new InvalidOperationException( "BridgeBeats:IdentityConnectionString is required in Production." )
        : new SqliteConnectionStringBuilder {
            DataSource = Path.GetFullPath( Path.Combine( Environment.CurrentDirectory, "bridgebeats.db" ) )
        }.ToString( ));

string dataProtectionKeyPathValue = config["BridgeBeats:DataProtectionKeyPath"]
    ?? (isProduction
        ? throw new InvalidOperationException( "BridgeBeats:DataProtectionKeyPath is required in Production." )
        : Path.GetFullPath( Path.Combine( Environment.CurrentDirectory, "keys" ) ));
if (isProduction && !Directory.Exists( dataProtectionKeyPathValue )) {
    throw new InvalidOperationException(
        $"The configured persistent Data Protection key-ring directory does not exist: {dataProtectionKeyPathValue}"
    );
}

string logDirPathValue = config["BridgeBeats:LogDirPath"]
    ?? (isProduction ? "/app/data/logs" : "./logs");
string nodeNumberValue = config["BridgeBeats:NodeNumber"] ?? "0";
string defaultLogLevelValue = config["Logging:LogLevel:Default"] ?? "Information";
string hostingLogLevelValue = config["Logging:LogLevel:Microsoft.Hosting.Lifetime"] ?? "Information";
string allowedHostsValue = config["AllowedHosts"] ?? "localhost;127.0.0.1";
string openTelemetryEndpointValue = config["OpenTelemetry:OtlpEndpoint"] ?? string.Empty;
string openTelemetryTracingValue = config.GetValue( "OpenTelemetry:EnableTracing", true ) ? "true" : "false";
string openTelemetryMetricsValue = config.GetValue( "OpenTelemetry:EnableMetrics", true ) ? "true" : "false";

ApplicationSettingsSnapshot? runtimeSettings = await ApplicationSettingsBootstrapper.MigrateSeedAndLoadAsync(
    identityConnectionStringValue,
    dataProtectionKeyPathValue,
    config
);
ApplicationSettingsValues values = runtimeSettings?.Values ?? new ApplicationSettingsValues( );
ApplicationSettingsSecrets secrets = runtimeSettings?.Secrets ?? new ApplicationSettingsSecrets( );

bool spotifyConfigured = ApplicationSettingsActivation.IsSpotifyConfigured( values, secrets );
bool appleMusicConfigured = ApplicationSettingsActivation.IsAppleMusicConfigured( values, secrets );
bool tidalConfigured = ApplicationSettingsActivation.IsTidalConfigured( values, secrets );
bool setupRequired = runtimeSettings is null ||
    !ApplicationSettingsActivation.CanStartWeb( values, secrets );

IResourceBuilder<ParameterResource> spotifyClientId = builder.AddParameter(
    "RuntimeSpotifyClientId",
    ( ) => values.SpotifyClientId,
    secret: true
);
IResourceBuilder<ParameterResource> spotifyClientSecret = builder.AddParameter(
    "RuntimeSpotifyClientSecret",
    ( ) => secrets.SpotifyClientSecret.Reveal( ) ?? string.Empty,
    secret: true
);
IResourceBuilder<ParameterResource> appleTeamId = builder.AddParameter(
    "RuntimeAppleTeamId",
    ( ) => values.AppleTeamId,
    secret: true
);
IResourceBuilder<ParameterResource> appleKeyId = builder.AddParameter(
    "RuntimeAppleKeyId",
    ( ) => values.AppleKeyId,
    secret: true
);
IResourceBuilder<ParameterResource> applePrivateKey = builder.AddParameter(
    "RuntimeApplePrivateKey",
    ( ) => secrets.ApplePrivateKey.Reveal( ) ?? string.Empty,
    secret: true
);
IResourceBuilder<ParameterResource> tidalClientId = builder.AddParameter(
    "RuntimeTidalClientId",
    ( ) => values.TidalClientId,
    secret: true
);
IResourceBuilder<ParameterResource> tidalClientSecret = builder.AddParameter(
    "RuntimeTidalClientSecret",
    ( ) => secrets.TidalClientSecret.Reveal( ) ?? string.Empty,
    secret: true
);
IResourceBuilder<ParameterResource> discordToken = builder.AddParameter(
    "RuntimeDiscordToken",
    ( ) => secrets.DiscordToken.Reveal( ) ?? string.Empty,
    secret: true
);
IResourceBuilder<ParameterResource> atProtoIdentifier = builder.AddParameter(
    "RuntimeATProtoIdentifier",
    ( ) => values.ATProtoIdentifier,
    secret: true
);
IResourceBuilder<ParameterResource> atProtoPassword = builder.AddParameter(
    "RuntimeATProtoPassword",
    ( ) => secrets.ATProtoPassword.Reveal( ) ?? string.Empty,
    secret: true
);
IResourceBuilder<ParameterResource> atProtoUserDID = builder.AddParameter(
    "RuntimeATProtoUserDID",
    ( ) => values.ATProtoUserDid,
    secret: true
);
IResourceBuilder<ParameterResource> atProtoPdsUri = builder.AddParameter(
    "RuntimeATProtoPdsUri",
    ( ) => values.ATProtoPdsUri
);
IResourceBuilder<ParameterResource> atProtoOAuthSigningKey = builder.AddParameter(
    "RuntimeATProtoOAuthSigningKey",
    ( ) => secrets.ATProtoOAuthSigningKey.Reveal( ) ?? string.Empty,
    secret: true
);
IResourceBuilder<ParameterResource> apiKeySalt = builder.AddParameter(
    "RuntimeApiKeySalt",
    ( ) => secrets.ApiKeySalt.Reveal( ) ?? string.Empty,
    secret: true
);
IResourceBuilder<ParameterResource> internalServiceKey = builder.AddParameter(
    "RuntimeInternalServiceKey",
    ( ) => secrets.InternalServiceKey.Reveal( ) ?? string.Empty,
    secret: true
);
IResourceBuilder<ParameterResource> openTelemetryHeaders = builder.AddParameter(
    "RuntimeOpenTelemetryHeaders",
    ( ) => config["OpenTelemetry:OtlpHeaders"] ?? string.Empty,
    secret: true
);

// The public host is deployment topology shared with Caddy. It is deliberately supplied through
// AppHost bootstrap configuration rather than changed dynamically with the database aggregate.
string domain = config["BridgeBeats:Domain"] ?? (isProduction
    ? throw new InvalidOperationException( "BridgeBeats:Domain is required in Production." )
    : "localhost");
string rateLimitRequestsPerHour = values.RateLimitRequestsPerHour.ToString( System.Globalization.CultureInfo.InvariantCulture );
string cacheDays = values.CacheDays.ToString( System.Globalization.CultureInfo.InvariantCulture );
string atProtoSessionTtlDays = values.ATProtoSessionTtlDays.ToString( System.Globalization.CultureInfo.InvariantCulture );
string cardCacheExpirationHours = values.CardCacheExpirationHours.ToString( System.Globalization.CultureInfo.InvariantCulture );
string cardCacheCleanupInterval = values.CardCacheCleanupInterval.ToString( System.Globalization.CultureInfo.InvariantCulture );
string cardCacheMaxEntries = values.CardCacheMaxEntries.ToString( System.Globalization.CultureInfo.InvariantCulture );
string resilienceMaxRetryAfterSeconds = values.Resilience.MaxRetryAfterSeconds.ToString( System.Globalization.CultureInfo.InvariantCulture );
string resilienceMaxRetryAttempts = values.Resilience.MaxRetryAttempts.ToString( System.Globalization.CultureInfo.InvariantCulture );
string resilienceTotalTimeoutMinutes = values.Resilience.TotalTimeoutMinutes.ToString( System.Globalization.CultureInfo.InvariantCulture );
string resilienceAttemptTimeoutSeconds = values.Resilience.AttemptTimeoutSeconds.ToString( System.Globalization.CultureInfo.InvariantCulture );
string bootstrapIntervalHours = values.Maintenance.BootstrapIntervalHours.ToString( System.Globalization.CultureInfo.InvariantCulture );
string refreshIntervalHours = values.Maintenance.RefreshIntervalHours.ToString( System.Globalization.CultureInfo.InvariantCulture );
string maxRecordsPerRun = values.Maintenance.MaxRecordsPerRun.ToString( System.Globalization.CultureInfo.InvariantCulture );
string refreshRetryMinutes = values.Maintenance.RefreshRetryMinutes.ToString( System.Globalization.CultureInfo.InvariantCulture );
string queueSettingsSnapshot = JsonSerializer.Serialize( values.Queue );
string spotifyBatchSettingsSnapshot = JsonSerializer.Serialize( values.Spotify.Batch );
string identityConnectionString = identityConnectionStringValue;
string dataProtectionKeyPath = dataProtectionKeyPathValue;
string logDirPath = logDirPathValue;
string nodeNumber = nodeNumberValue;

// Redis is added as a connection-string resource only: Aspire does NOT provision it here. An external
// Redis must already exist and ConnectionStrings:redis must be set. Every component references this
// resource. If the connection string is missing the host writes an error to stderr but does not abort.
// In Docker, Redis availability is ensured by Docker Compose depends_on; in development it must be running.
IResourceBuilder<IResourceWithConnectionString> redis = builder.AddConnectionString( "redis" );

// Validate Redis connection string is configured.
if (string.IsNullOrWhiteSpace( config["ConnectionStrings:redis"] )) {
    Console.Error.WriteLine( "ERROR: Redis connection string is required but not configured." );
    Console.Error.WriteLine( "       Set the ConnectionStrings__redis bootstrap environment variable." );
}

// Credential-presence checks use only the database snapshot. External configuration cannot enable a
// worker or override a stored credential.
bool HasSpotifyCredentials( ) =>
    values.Workers.SpotifyWorkerEnabled &&
    spotifyConfigured;
bool HasAppleMusicCredentials( ) =>
    values.Workers.AppleMusicWorkerEnabled &&
    appleMusicConfigured;
bool HasTidalCredentials( ) =>
    values.Workers.TidalWorkerEnabled &&
    tidalConfigured;
bool HasDiscordCredentials( ) => ApplicationSettingsActivation.CanStartDiscordWorker( secrets );

// Adds a production component as an Aspire executable resource that runs a published DLL, referencing
// the shared Redis resource and optionally exposing an HTTP endpoint. Used in the production run mode
// in place of the project resources used during development; the DLL is located under "/src".
//   name             - the Aspire resource name (for example "spotify-worker").
//   projectName      - the project/assembly name, used to build the DLL path under "/src".
//   httpPort         - the HTTP target port to expose, or null for a headless background host.
//   workingDirectory - the working directory for the executable; defaults to "/app/data".
// Returns the executable resource builder for further configuration.
IResourceBuilder<ExecutableResource> AddProductionExecutable(
    string name,
    string projectName,
    int? httpPort = null,
    string workingDirectory = "/app/data"
) {
    string dllPath = $"/src/{projectName}/{projectName}.dll";
    IResourceBuilder<ExecutableResource> resource = builder.AddExecutable( name, "dotnet", workingDirectory, dllPath )
        .WithReference(redis)
        .WithOtlpExporter( );

    if (httpPort.HasValue) {
        resource = resource.WithHttpEndpoint( targetPort: httpPort.Value, name: "http" );
    }

    return resource;
}

bool spotifyWorkerEnabled = HasSpotifyCredentials();
bool appleMusicWorkerEnabled = HasAppleMusicCredentials();
bool tidalWorkerEnabled = HasTidalCredentials();

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
bool queueTopologyEnabled = ApplicationSettingsActivation.CanStartQueueTopology( values, secrets );
bool atProtoWorkerTopologyEnabled = ApplicationSettingsActivation.CanStartAtProtoWorkerTopology(
    values,
    secrets
);

// Applies the settings every child process shares. Keeping this contract in one place prevents
// development and production topology branches from drifting and guarantees that every queue
// participant and HTTP client observes the same database revision.
IResourceBuilder<T> WithSharedRuntime<T>( IResourceBuilder<T> resource, bool includeQueueSnapshot )
    where T : IResourceWithEnvironment {
    IResourceBuilder<T> configured = resource
        .WithEnvironment( "BridgeBeats__SettingsRevision", runtimeSettings?.Revision ?? string.Empty )
        .WithEnvironment( "BridgeBeats__LogDirPath", logDirPath )
        .WithEnvironment( "Logging__LogLevel__Default", defaultLogLevelValue )
        .WithEnvironment( "Logging__LogLevel__Microsoft.Hosting.Lifetime", hostingLogLevelValue )
        .WithEnvironment( "Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics", "Warning" )
        .WithEnvironment( "Logging__LogLevel__Microsoft.AspNetCore.Routing.EndpointMiddleware", "Warning" )
        .WithEnvironment( "Serilog__MinimumLevel__Default", defaultLogLevelValue )
        .WithEnvironment( "Serilog__MinimumLevel__Override__Microsoft.AspNetCore.Hosting.Diagnostics", "Warning" )
        .WithEnvironment( "Serilog__MinimumLevel__Override__Microsoft.AspNetCore.Routing.EndpointMiddleware", "Warning" )
        .WithEnvironment( "OpenTelemetry__OtlpEndpoint", openTelemetryEndpointValue )
        .WithEnvironment( "OpenTelemetry__OtlpHeaders", openTelemetryHeaders )
        .WithEnvironment( "OpenTelemetry__EnableTracing", openTelemetryTracingValue )
        .WithEnvironment( "OpenTelemetry__EnableMetrics", openTelemetryMetricsValue )
        .WithEnvironment( "BridgeBeats__Resilience__MaxRetryAfterSeconds", resilienceMaxRetryAfterSeconds )
        .WithEnvironment( "BridgeBeats__Resilience__MaxRetryAttempts", resilienceMaxRetryAttempts )
        .WithEnvironment( "BridgeBeats__Resilience__TotalTimeoutMinutes", resilienceTotalTimeoutMinutes )
        .WithEnvironment( "BridgeBeats__Resilience__AttemptTimeoutSeconds", resilienceAttemptTimeoutSeconds );

    return includeQueueSnapshot
        ? configured.WithEnvironment( "BridgeBeats__QueueSnapshot", queueSettingsSnapshot )
        : configured;
}

IResourceBuilder<T> WithSpotifyRuntime<T>( IResourceBuilder<T> resource )
    where T : IResourceWithEnvironment => WithSharedRuntime( resource, includeQueueSnapshot: true )
        .WithEnvironment( "ASPNETCORE_HTTP_PORTS", "5100" )
        .WithEnvironment( "BridgeBeats__SpotifyBatchSnapshot", spotifyBatchSettingsSnapshot )
        .WithEnvironment( "BridgeBeats__SpotifyClientId", spotifyClientId )
        .WithEnvironment( "BridgeBeats__SpotifyClientSecret", spotifyClientSecret );

IResourceBuilder<T> WithAppleMusicRuntime<T>( IResourceBuilder<T> resource )
    where T : IResourceWithEnvironment => WithSharedRuntime( resource, includeQueueSnapshot: true )
        .WithEnvironment( "ASPNETCORE_HTTP_PORTS", "5101" )
        .WithEnvironment( "BridgeBeats__AppleTeamId", appleTeamId )
        .WithEnvironment( "BridgeBeats__AppleKeyId", appleKeyId )
        .WithEnvironment( "BridgeBeats__ApplePrivateKey", applePrivateKey );

IResourceBuilder<T> WithTidalRuntime<T>( IResourceBuilder<T> resource )
    where T : IResourceWithEnvironment => WithSharedRuntime( resource, includeQueueSnapshot: true )
        .WithEnvironment( "ASPNETCORE_HTTP_PORTS", "5102" )
        .WithEnvironment( "BridgeBeats__TidalClientId", tidalClientId )
        .WithEnvironment( "BridgeBeats__TidalClientSecret", tidalClientSecret );

IResourceBuilder<T> WithDiscordRuntime<T>( IResourceBuilder<T> resource )
    where T : IResourceWithEnvironment => WithSharedRuntime( resource, includeQueueSnapshot: false )
        .WithEnvironment( "BridgeBeats__DiscordToken", discordToken )
        .WithEnvironment( "BridgeBeats__NodeNumber", nodeNumber )
        .WithEnvironment( "BridgeBeats__Domain", domain )
        .WithEnvironment( "BridgeBeats__InternalServiceKey", internalServiceKey );

IResourceBuilder<T> WithSagaRuntime<T>( IResourceBuilder<T> resource )
    where T : IResourceWithEnvironment => WithSharedRuntime( resource, includeQueueSnapshot: true )
        .WithEnvironment( "BridgeBeats__ATProtoIdentifier", atProtoIdentifier )
        .WithEnvironment( "BridgeBeats__ATProtoPassword", atProtoPassword )
        .WithEnvironment( "BridgeBeats__ATProtoUserDID", atProtoUserDID )
        .WithEnvironment( "BridgeBeats__CacheDays", cacheDays )
        .WithEnvironment( "BridgeBeats__ATProtoSessionTtlDays", atProtoSessionTtlDays )
        .WithEnvironment( "BridgeBeats__EnabledProviders", enabledProvidersValue )
        .WithEnvironment( "BridgeBeats__DataProtectionKeyPath", dataProtectionKeyPath );

IResourceBuilder<T> WithMaintenanceRuntime<T>( IResourceBuilder<T> resource )
    where T : IResourceWithEnvironment => WithSharedRuntime( resource, includeQueueSnapshot: true )
        .WithEnvironment( "BridgeBeats__ATProtoIdentifier", atProtoIdentifier )
        .WithEnvironment( "BridgeBeats__ATProtoPassword", atProtoPassword )
        .WithEnvironment( "BridgeBeats__ATProtoUserDID", atProtoUserDID )
        .WithEnvironment( "BridgeBeats__ATProtoPdsUri", atProtoPdsUri )
        .WithEnvironment( "BridgeBeats__CacheDays", cacheDays )
        .WithEnvironment( "BridgeBeats__ATProtoSessionTtlDays", atProtoSessionTtlDays )
        .WithEnvironment( "BridgeBeats__DataProtectionKeyPath", dataProtectionKeyPath )
        .WithEnvironment( "BridgeBeats__EnabledProviders", enabledProvidersValue )
        .WithEnvironment( "BridgeBeats__BootstrapIntervalHours", bootstrapIntervalHours )
        .WithEnvironment( "BridgeBeats__RefreshIntervalHours", refreshIntervalHours )
        .WithEnvironment( "BridgeBeats__MaxRecordsPerRun", maxRecordsPerRun )
        .WithEnvironment( "BridgeBeats__RefreshRetryMinutes", refreshRetryMinutes );

IResourceBuilder<T> WithWebRuntime<T>( IResourceBuilder<T> resource )
    where T : IResourceWithEnvironment => WithSharedRuntime( resource, includeQueueSnapshot: true )
        .WithEnvironment( "ASPNETCORE_HTTP_PORTS", "10000" )
        .WithEnvironment( "BridgeBeats__SetupRequired", setupRequired ? "true" : "false" )
        .WithEnvironment( "BridgeBeats__SettingsRevision", runtimeSettings?.Revision ?? string.Empty )
        .WithEnvironment( "BridgeBeats__Workers__UseWorkerServices", values.Workers.UseWorkerServices ? "true" : "false" )
        .WithEnvironment( "BridgeBeats__Workers__SpotifyWorkerEnabled", spotifyWorkerEnabled ? "true" : "false" )
        .WithEnvironment( "BridgeBeats__Workers__AppleMusicWorkerEnabled", appleMusicWorkerEnabled ? "true" : "false" )
        .WithEnvironment( "BridgeBeats__Workers__TidalWorkerEnabled", tidalWorkerEnabled ? "true" : "false" )
        .WithEnvironment( "BridgeBeats__SpotifyClientId", spotifyClientId )
        .WithEnvironment( "BridgeBeats__SpotifyClientSecret", spotifyClientSecret )
        .WithEnvironment( "BridgeBeats__AppleTeamId", appleTeamId )
        .WithEnvironment( "BridgeBeats__AppleKeyId", appleKeyId )
        .WithEnvironment( "BridgeBeats__ApplePrivateKey", applePrivateKey )
        .WithEnvironment( "BridgeBeats__TidalClientId", tidalClientId )
        .WithEnvironment( "BridgeBeats__TidalClientSecret", tidalClientSecret )
        .WithEnvironment( "BridgeBeats__ATProtoIdentifier", atProtoIdentifier )
        .WithEnvironment( "BridgeBeats__ATProtoPassword", atProtoPassword )
        .WithEnvironment( "BridgeBeats__ATProtoUserDID", atProtoUserDID )
        .WithEnvironment( "BridgeBeats__ATProtoPdsUri", atProtoPdsUri )
        .WithEnvironment( "BridgeBeats__ATProtoOAuthSigningKey", atProtoOAuthSigningKey )
        .WithEnvironment( "BridgeBeats__ApiKeySalt", apiKeySalt )
        .WithEnvironment( "BridgeBeats__InternalServiceKey", internalServiceKey )
        .WithEnvironment( "BridgeBeats__Domain", domain )
        .WithEnvironment( "AllowedHosts", allowedHostsValue )
        .WithEnvironment( "BridgeBeats__RateLimitRequestsPerHour", rateLimitRequestsPerHour )
        .WithEnvironment( "BridgeBeats__CacheDays", cacheDays )
        .WithEnvironment( "BridgeBeats__ATProtoSessionTtlDays", atProtoSessionTtlDays )
        .WithEnvironment( "BridgeBeats__IdentityConnectionString", identityConnectionString )
        .WithEnvironment( "BridgeBeats__DataProtectionKeyPath", dataProtectionKeyPath )
        .WithEnvironment( "BridgeBeats__CardCacheExpirationHours", cardCacheExpirationHours )
        .WithEnvironment( "BridgeBeats__CardCacheCleanupInterval", cardCacheCleanupInterval )
        .WithEnvironment( "BridgeBeats__CardCacheMaxEntries", cardCacheMaxEntries );

// Worker tracking for service discovery. Track references for WithReference() calls in both
// development and production.
IResourceBuilder<ProjectResource>? spotifyWorkerProject = null;
IResourceBuilder<ProjectResource>? appleMusicWorkerProject = null;
IResourceBuilder<ProjectResource>? tidalWorkerProject = null;
IResourceBuilder<ProjectResource>? discordWorkerProject = null;

IResourceBuilder<ExecutableResource>? discordWorkerExe = null;
IResourceBuilder<ExecutableResource>? bridgebeatsWebExe = null;

// Spotify Worker.
if (!setupRequired && spotifyWorkerEnabled) {
    if (isProduction) {
        _ = WithSpotifyRuntime(
            AddProductionExecutable( "spotify-worker", "BridgeBeats.Worker.Spotify", httpPort: 5100 )
        );
    } else {
        spotifyWorkerProject = builder.AddProject<Projects.BridgeBeats_Worker_Spotify>( "spotify-worker" )
            .WithHttpEndpoint( targetPort: 5100, name: "http" );
        _ = WithSpotifyRuntime( spotifyWorkerProject.WithReference( redis ) );
    }
}

// Apple Music Worker.
if (!setupRequired && appleMusicWorkerEnabled) {
    if (isProduction) {
        _ = WithAppleMusicRuntime(
            AddProductionExecutable( "applemusic-worker", "BridgeBeats.Worker.AppleMusic", httpPort: 5101 )
        );
    } else {
        appleMusicWorkerProject = builder.AddProject<Projects.BridgeBeats_Worker_AppleMusic>( "applemusic-worker" )
            .WithHttpEndpoint( targetPort: 5101, name: "http" );
        _ = WithAppleMusicRuntime( appleMusicWorkerProject.WithReference( redis ) );
    }
}

// Tidal Worker.
if (!setupRequired && tidalWorkerEnabled) {
    if (isProduction) {
        _ = WithTidalRuntime(
            AddProductionExecutable( "tidal-worker", "BridgeBeats.Worker.Tidal", httpPort: 5102 )
        );
    } else {
        tidalWorkerProject = builder.AddProject<Projects.BridgeBeats_Worker_Tidal>( "tidal-worker" )
            .WithHttpEndpoint( targetPort: 5102, name: "http" );
        _ = WithTidalRuntime( tidalWorkerProject.WithReference( redis ) );
    }
}

// Discord Worker. Handles Discord gateway events, detecting music links and calling the
// BridgeBeats Web API for lookups and card generation.
if (!setupRequired && HasDiscordCredentials( )) {
    if (isProduction) {
        discordWorkerExe = AddProductionExecutable( "discord-worker", "BridgeBeats.Worker.Discord" );
        _ = WithDiscordRuntime( discordWorkerExe );
    } else {
        discordWorkerProject = builder.AddProject<Projects.BridgeBeats_Worker_Discord>( "discord-worker" );
        _ = WithDiscordRuntime( discordWorkerProject.WithReference( redis ) );
    }
}

// Saga Coordinator Worker. The saga coordinator monitors for completed multi-provider lookups and
// writes final results to ATProto. It uses Redis Pub/Sub for event-driven coordination and polling
// as a fallback.
//
if (!setupRequired && atProtoWorkerTopologyEnabled && isProduction) {
    _ = WithSagaRuntime( AddProductionExecutable( "saga-coordinator", "BridgeBeats.Worker.SagaCoordinator" ) );
} else if (!setupRequired && atProtoWorkerTopologyEnabled) {
    _ = WithSagaRuntime(
        builder.AddProject<Projects.BridgeBeats_Worker_SagaCoordinator>( "saga-coordinator" ).WithReference( redis )
    );
}

// JetStream Watcher Worker. Monitors Bluesky Jetstream for music links and submits them to provider
// queues at bulk priority. Fire-and-forget: no credentials needed.
if (!setupRequired && queueTopologyEnabled && isProduction) {
    _ = WithSharedRuntime(
        AddProductionExecutable( "jetstream-watcher", "BridgeBeats.Worker.JetStreamWatcher" ),
        includeQueueSnapshot: true
    );
} else if (!setupRequired && queueTopologyEnabled) {
    _ = WithSharedRuntime(
        builder.AddProject<Projects.BridgeBeats_Worker_JetStreamWatcher>( "jetstream-watcher" ).WithReference( redis ),
        includeQueueSnapshot: true
    );
}

// Cache Bootstrap Worker. Bootstraps Redis cache from ATProto public records on startup and
// periodically (default: every 6 hours). Uses unauthenticated access for public records.
if (!setupRequired && atProtoWorkerTopologyEnabled && isProduction) {
    _ = WithMaintenanceRuntime( AddProductionExecutable( "maintenance", "BridgeBeats.Worker.Maintenance" ) );
} else if (!setupRequired && atProtoWorkerTopologyEnabled) {
    _ = WithMaintenanceRuntime(
        builder.AddProject<Projects.BridgeBeats_Worker_Maintenance>( "maintenance" ).WithReference( redis )
    );
}

// Main Web Application. In production, service discovery uses resource names for endpoint resolution.
// Worker references are registered via WithReference() for service discovery.
IResourceBuilder<ProjectResource>? bridgebeatsWebProject = null;

if (isProduction) {
    bridgebeatsWebExe = AddProductionExecutable( "bridgebeats", "BridgeBeats.Web", workingDirectory: "/src/BridgeBeats.Web" );
    _ = WithWebRuntime(
        bridgebeatsWebExe.WithHttpEndpoint( port: 10000, targetPort: 10000, name: "bridgebeats-http", isProxied: false )
    );
} else {
    bridgebeatsWebProject = builder.AddProject<Projects.BridgeBeats_Web>( "bridgebeats" )
        .WithHttpEndpoint( port: 10000, targetPort: 10000, name: "bridgebeats-http", isProxied: false );
    _ = WithWebRuntime( bridgebeatsWebProject.WithReference( redis ) );
}

// Service discovery references. Wire up service discovery between web app and workers.
//   Development: uses WithReference() to inject services__<name>__http__0 environment variables.
//   Production: uses WithEnvironment() to directly set localhost URLs with known ports.
// Either way: Web references the enabled provider workers (it fans interactive lookups out to their
// WorkerApi endpoints) and the Discord worker references Web (it calls Web over internal HTTP).
if (isProduction) {
    // Production: hand-wire service discovery to fixed localhost ports (all processes share a host).
    // Web discovers the provider workers; the Discord worker discovers Web.
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
    // Development: Aspire injects service-discovery variables from WithReference wiring.
    if (bridgebeatsWebProject is not null) {
        if (spotifyWorkerProject is not null) {
            _ = bridgebeatsWebProject.WithReference( spotifyWorkerProject );
        }
        if (appleMusicWorkerProject is not null) {
            _ = bridgebeatsWebProject.WithReference( appleMusicWorkerProject );
        }
        if (tidalWorkerProject is not null) {
            _ = bridgebeatsWebProject.WithReference( tidalWorkerProject );
        }
    }
    if (discordWorkerProject is not null && bridgebeatsWebProject is not null) {
        _ = discordWorkerProject.WithReference( bridgebeatsWebProject );
    }
}

builder.Build( ).Run( );
