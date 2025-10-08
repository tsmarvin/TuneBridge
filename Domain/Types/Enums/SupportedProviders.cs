using System.ComponentModel;

namespace TuneBridge.Domain.Types.Enums {
    /// <summary>
    /// Represents the supported music streaming providers.
    /// </summary>
    public enum SupportedProviders {
        /// <summary>
        /// Apple Music streaming service.
        /// </summary>
        [Description("Apple Music")]
        AppleMusic = 1,

        /// <summary>
        /// Spotify streaming service.
        /// </summary>
        Spotify    = 2,
    }
}
