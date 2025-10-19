namespace TuneBridge.Configuration {
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
        /// The Bluesky PDS instance URL (e.g., https://bsky.social).
        /// </summary>
        public string BlueskyPdsUrl { get; set; } = string.Empty;

        /// <summary>
        /// The Bluesky account identifier (handle or DID).
        /// </summary>
        public string BlueskyIdentifier { get; set; } = string.Empty;

        /// <summary>
        /// The Bluesky account password or app password.
        /// </summary>
        public string BlueskyPassword { get; set; } = string.Empty;

        /// <summary>
        /// The number of days to cache MediaLinkResult lookups. Default is 7 days.
        /// </summary>
        public int CacheDays { get; set; } = 7;

        /// <summary>
        /// The SQLite database file path for the cache. Default is "medialinkscache.db".
        /// </summary>
        public string CacheDbPath { get; set; } = "medialinkscache.db";

    }
}
