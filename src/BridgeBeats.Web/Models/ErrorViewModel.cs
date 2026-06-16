namespace BridgeBeats.Web.Models {
    /// <summary>
    /// View model for the error page, carrying the failed request's identifier and an optional message.
    /// </summary>
    public class ErrorViewModel {
        /// <summary>
        /// The identifier of the request that produced the error, used for correlation. May be <c>null</c>.
        /// </summary>
        public string? RequestId { get; set; }

        /// <summary>
        /// An optional, human-readable message describing the error.
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// Gets a value indicating whether the request identifier should be displayed, which is true when
        /// <see cref="RequestId"/> is non-empty.
        /// </summary>
        public bool ShowRequestId => !string.IsNullOrEmpty( RequestId );
    }
}
