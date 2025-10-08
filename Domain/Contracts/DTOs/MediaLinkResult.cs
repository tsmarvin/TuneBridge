using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Domain.Contracts.DTOs {

    /// <summary>
    /// Represents the result of parsing and looking up media links for supported providers.
    /// </summary>
    public sealed class MediaLinkResult {

        /// <summary>
        /// The list of input media links used for the initial lookup (if applicable).
        /// </summary>
        internal List<string> _inputLinks = [];

        /// <summary>
        /// The dictionary of results from supported providers with matching entries.
        /// </summary>
        public Dictionary<SupportedProviders, MusicLookupResultDto> Results { get; set; } = [];

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
