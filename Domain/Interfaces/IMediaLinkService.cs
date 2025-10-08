using TuneBridge.Domain.Contracts.DTOs;

namespace TuneBridge.Domain.Interfaces {
    public interface IMediaLinkService {
        Task<MediaLinkResult?> GetInfoAsync( string title, string artist );
        Task<MediaLinkResult?> GetInfoByISRCAsync( string isrc );
        Task<MediaLinkResult?> GetInfoByUPCAsync( string upc );
        IAsyncEnumerable<MediaLinkResult> GetInfoAsync( string content );
    }
}
