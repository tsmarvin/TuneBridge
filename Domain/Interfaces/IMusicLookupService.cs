using System.Text.Json;
using System.Text.RegularExpressions;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Domain.Interfaces {

    /// <summary>
    /// The common interface for looking up music from a given provider (e.g., Apple Music, Spotify).
    /// </summary>
    public partial interface IMusicLookupService {

        static SupportedProviders Provider { get; }
        Task<MusicLookupResultDto?> GetInfoAsync( string title, string artist );
        Task<MusicLookupResultDto?> GetInfoByISRCAsync( string isrc );
        Task<MusicLookupResultDto?> GetInfoByUPCAsync( string upc );
        Task<MusicLookupResultDto?> GetInfoAsync( string uri );
        Task<MusicLookupResultDto?> GetInfoAsync( MusicLookupResultDto lookup );

    }
}
