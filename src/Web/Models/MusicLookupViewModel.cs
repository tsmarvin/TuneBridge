using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Web.Models {
    /// <summary>
    /// View model for displaying music lookup results.
    /// </summary>
    public class MusicLookupViewModel {
        /// <summary>
        /// List of music lookup result items with card URLs.
        /// </summary>
        public List<MusicLookupResultItem> Items { get; set; } = [];

        /// <summary>
        /// Message to display when there are no results.
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// Individual result item with card URL and metadata.
        /// </summary>
        public class MusicLookupResultItem {
            /// <summary>
            /// URL to the stored OpenGraph card.
            /// </summary>
            public string? CardUrl { get; set; }

            /// <summary>
            /// ATProto URI for the stored record (at://...).
            /// </summary>
            public string? ATProtoUri { get; set; }

            /// <summary>
            /// The raw result data.
            /// </summary>
            public MediaLinkResult Result { get; set; } = null!;

            /// <summary>
            /// Primary provider for this result (for styling).
            /// </summary>
            public SupportedProviders PrimaryProvider { get; set; }

            /// <summary>
            /// Primary result data (for display).
            /// </summary>
            public MusicLookupResultDto PrimaryResult { get; set; } = null!;
        }
    }
}
