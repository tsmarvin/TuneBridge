using System.Text.Json.Serialization;
using TuneBridge.Common.Contracts.Enums;

namespace TuneBridge.Common.Contracts.DTOs {

    /// <summary>
    /// Represents the result of parsing and looking up media links for supported providers.
    /// </summary>
    public sealed class MediaLinkResult {

        /// <summary>
        /// The list of input media links used for the initial lookup (if applicable).
        /// </summary>
        [JsonIgnore]
        public List<string> _inputLinks = [];

        /// <summary>
        /// The dictionary of results from supported providers with matching entries.
        /// </summary>
        public Dictionary<SupportedProviders, MusicLookupResultDto> Results { get; set; } = [];

        /// <summary>
        /// Optional user-facing messages associated with this result (e.g., guidance for unsupported links).
        /// </summary>
        public List<string>? Messages { get; set; }

        /// <inheritdoc/>
        public override bool Equals( object? obj ) {
            if (
                obj is not null &&
                obj.GetType( ) == typeof( MediaLinkResult )
            ) {
                MediaLinkResult objCast = (MediaLinkResult)obj;
                return objCast._inputLinks == _inputLinks &&
                        objCast.Results == Results;
            }
            return false;
        }

        /// <inheritdoc/>
        public override int GetHashCode( )
            => Results.GetHashCode( );
    }

}
