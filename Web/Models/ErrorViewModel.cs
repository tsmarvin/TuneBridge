namespace TuneBridge.Web.Models {
    /// <summary>
    /// View model for displaying error information.
    /// </summary>
    public class ErrorViewModel {
        /// <summary>
        /// The unique identifier for the request that resulted in an error.
        /// </summary>
        public string? RequestId { get; set; }

        /// <summary>
        /// Indicates whether the request ID should be displayed.
        /// </summary>
        public bool ShowRequestId => !string.IsNullOrEmpty( RequestId );
    }
}
