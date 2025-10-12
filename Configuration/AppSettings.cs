namespace TuneBridge.Configuration {
    /// <summary>
    /// Represents application settings for external service integrations.<para/>
    ///
    /// Apple Music and Spotify API credentials are required for the application to function properly.
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
    /// YouTube API credentials can be obtained from the Google Cloud Console.
    /// See <seealso href="https://developers.google.com/youtube/v3/getting-started">this link</seealso>
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
        /// The YouTube Data API v3 API key.
        /// </summary>
        public string YouTubeApiKey { get; set; } = string.Empty;

        /// <summary>
        /// The Discord bot token.
        /// </summary>
        public string DiscordToken { get; set; } = string.Empty;

    }
}
