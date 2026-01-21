namespace BridgeBeats.Web.Configuration {
    /// <summary>
    /// Represents application settings for external service integrations.<para/>
    ///
    /// At least one music provider (Apple Music, Spotify, or Tidal) API credentials are required for the application to function properly.
    /// A Discord bot token is required for Discord integrations.<para/>
    ///
    /// Apple Music API credentials can be obtained from the Apple Developer portal.
    /// See <seealso href="https://developer.apple.com/help/account/configure-app-capabilities/create-a-media-identifier-and-private-key/">this link</seealso>
    /// for more details. <para/>
    ///
    /// Spotify API credentials can be obtained by creating an app in the Spotify Developer Dashboard.
    /// See <seealso href="https://developer.spotify.com/documentation/general/guides/app-settings/#register-your-app">this link</seealso>
    /// for more details. <para/>
    ///
    /// Tidal API credentials can be obtained by creating an app in the Tidal Developer Portal.
    /// See <seealso href="https://developer.tidal.com/">this link</seealso>
    /// for more details. <para/>
    ///
    /// A Discord bot token can be obtained by creating an app in the Discord Developer Portal.
    /// See <seealso href="https://discord.com/developers/docs/quick-start/getting-started">this link</seealso>
    /// for more details.
    /// </summary>
    internal class AppSettings {
        /// <summary>
        /// The node number for this instance (used for discord shard identification).
        /// </summary>
        public int NodeNumber { get; set; }
        /// <summary>
        /// The Apple Developer Team ID for Apple Music API authentication.
        /// </summary>
        public string AppleTeamId { get; set; } = string.Empty;

        /// <summary>
        /// The private key ID for Apple Music API authentication.
        /// </summary>
        public string AppleKeyId { get; set; } = string.Empty;

        /// <summary>
        /// The file path to the Apple Music private key (.p8).
        /// </summary>
        public string AppleKeyPath { get; set; } = string.Empty;

        /// <summary>
        /// The Spotify API client ID.
        /// </summary>
        public string SpotifyClientId { get; set; } = string.Empty;

        /// <summary>
        /// The Spotify API client secret.
        /// </summary>
        public string SpotifyClientSecret { get; set; } = string.Empty;

        /// <summary>
        /// The Tidal API client ID.
        /// </summary>
        public string TidalClientId { get; set; } = string.Empty;

        /// <summary>
        /// The Tidal API client secret.
        /// </summary>
        public string TidalClientSecret { get; set; } = string.Empty;

        /// <summary>
        /// The Discord bot token.
        /// </summary>
        public string DiscordToken { get; set; } = string.Empty;

        /// <summary>
        /// The database connection string for the identity database (SQLite).
        /// </summary>
        public string IdentityConnectionString { get; set; } = "Data Source=bridgebeats.db";

        /// <summary>
        /// Salt value for hashing API keys.
        /// </summary>
        public string ApiKeySalt { get; set; } = string.Empty;

        /// <summary>
        /// Maximum number of requests per hour per user for rate limiting.
        /// </summary>
        public int RateLimitRequestsPerHour { get; set; } = 20;

        /// <summary>
        /// The ATProto account identifier (handle or DID).
        /// </summary>
        public string ATProtoIdentifier { get; set; } = string.Empty;

        /// <summary>
        /// The ATProto account password or app password.
        /// </summary>
        public string ATProtoPassword { get; set; } = string.Empty;

        /// <summary>
        /// The ATProto account did (Decentralized Identifier).
        /// </summary>
        public string ATProtoUserDID { get; set; } = string.Empty;

        /// <summary>
        /// The ATProto PDS URI for public record access. Default is https://pds.bridgebeats.link.
        /// </summary>
        public string? ATProtoPdsUri { get; set; } = "https://pds.bridgebeats.link";

        /// <summary>
        /// The number of days to cache MediaLinkResult lookups. Default is 7 days.
        /// </summary>
        public int CacheDays { get; set; } = 7;

        /// <summary>
        /// The database connection string for the link cache database (SQLite).
        /// </summary>
        public string LinkCacheConnectionString { get; set; } = "Data Source=bridgebeats.db";

        /// <summary>
        /// The base URL for the application (e.g., https://bridgebeats.link). Used for generating OpenGraph card URLs.
        /// </summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>
        /// The log file path for the service.
        /// </summary>
        public string LogFilePath { get; set; } = string.Empty;

        /// <summary>
        /// The number of hours to cache OpenGraph cards in memory before expiration. Default is 1 hour.
        /// </summary>
        public int CardCacheExpirationHours { get; set; } = 1;

        /// <summary>
        /// The number of operations between cleanup cycles for expired OpenGraph cards. Default is 500.
        /// </summary>
        public int CardCacheCleanupInterval { get; set; } = 500;

        /// <summary>
        /// HTTP resilience configuration settings for music provider API requests.
        /// </summary>
        public ResilienceSettings Resilience { get; set; } = new( );

        /// <summary>
        /// Worker service configuration settings.
        /// When UseWorkerServices is true, the web app communicates with provider workers via HTTP
        /// instead of making direct API calls.
        /// </summary>
        public WorkerSettings Workers { get; set; } = new( );

    }

    /// <summary>
    /// Configuration settings for worker services.
    /// Controls whether the web app uses HTTP-based worker services for music provider lookups.
    /// </summary>
    public class WorkerSettings {
        /// <summary>
        /// Whether to use worker services for music provider lookups.
        /// When true, the web app communicates with provider workers via HTTP.
        /// When false, the web app makes direct API calls to music providers.
        /// </summary>
        public bool UseWorkerServices { get; set; }

        /// <summary>
        /// Whether the Spotify worker is enabled. Only used when UseWorkerServices is true.
        /// </summary>
        public bool SpotifyWorkerEnabled { get; set; }

        /// <summary>
        /// Whether the Apple Music worker is enabled. Only used when UseWorkerServices is true.
        /// </summary>
        public bool AppleMusicWorkerEnabled { get; set; }

        /// <summary>
        /// Whether the Tidal worker is enabled. Only used when UseWorkerServices is true.
        /// </summary>
        public bool TidalWorkerEnabled { get; set; }
    }

    /// <summary>
    /// Configuration settings for HTTP resilience policies applied to music provider API requests.
    /// These settings control retry behavior, timeouts, and rate limit handling.
    /// </summary>
    public class ResilienceSettings {
        /// <summary>
        /// Maximum Retry-After header value (in seconds) to honor before failing fast.
        /// When a music provider returns HTTP 429 with a Retry-After value exceeding this threshold,
        /// the request will fail immediately instead of waiting. Default is 120 seconds (2 minutes).
        /// </summary>
        public int MaxRetryAfterSeconds { get; set; } = 120;

        /// <summary>
        /// Maximum number of retry attempts for transient failures. Default is 5.
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 5;

        /// <summary>
        /// Total timeout for all retry attempts combined, in minutes. Default is 10 minutes.
        /// </summary>
        public int TotalTimeoutMinutes { get; set; } = 10;

        /// <summary>
        /// Timeout for each individual request attempt, in seconds. Default is 10 seconds.
        /// </summary>
        public int AttemptTimeoutSeconds { get; set; } = 10;
    }
}
