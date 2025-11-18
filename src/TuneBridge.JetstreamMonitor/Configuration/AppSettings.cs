namespace TuneBridge.JetStreamMonitor.Configuration {
    public class AppSettings {
        /// <summary>
        /// The base url to the API Hosting the TuneBridge URL search.
        /// </summary>
        public string ApiHostUrlBase { get; set; } = string.Empty;
        /// <summary>
        /// The database connection string for the database housing the Jetstream monitor data.
        /// </summary>
        public string DBConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// The endpoint URL for connecting to the Bluesky Jetstream WebSocket.
        /// </summary>
        public string JetstreamEndpoint { get; set; } = string.Empty;

    }
}
