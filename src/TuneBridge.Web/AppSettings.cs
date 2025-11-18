namespace TuneBridge.Web {
    /// <summary>
    /// The application settings schema for the TuneBridge Web Project.
    /// </summary>
    public class AppSettings {

        /// <summary>
        /// The database connection string for the identity database (SQLite).
        /// </summary>
        public string IdentityConnectionString { get; set; } = "Data Source=tunebridge.db";

        /// <summary>
        /// Salt value for hashing API keys.
        /// </summary>
        public string ApiKeySalt { get; set; } = string.Empty;

        /// <summary>
        /// Maximum number of requests per hour per user for rate limiting.
        /// </summary>
        public int RateLimitRequestsPerHour { get; set; } = 20;

        /// <summary>
        /// The base URL for the application (e.g., https://dev.tunebridge.media). Used for generating OpenGraph card URLs.
        /// </summary>
        public string BaseUrl { get; set; } = string.Empty;
    }
}
