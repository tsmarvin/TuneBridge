using System.Text.RegularExpressions;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using TuneBridge.Configuration;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Interfaces;

namespace TuneBridge.Domain.Implementations.DiscordGatewayHandlers {

    /// <summary>
    /// Handles Discord message creation events to detect and respond to music links.
    /// </summary>
    /// <param name="discordConfig">Configuration containing the media link service and node identifier.</param>
    public partial class MessageCreateGatewayHandler(
        DiscordNodeConfig discordConfig
    ) : IMessageCreateShardedGatewayHandler {

        private readonly int _nodeNumber = discordConfig.NodeNumber;
        private readonly IMediaLinkService _linkLookupService = discordConfig.LinkLookupService;
        private readonly IMediaCardService _cardService = discordConfig.CardService;
        private readonly string _baseUrl = discordConfig.BaseUrl;

        /// <summary>
        /// Sends a message with a link to the OpenGraph card for the media lookup result.
        /// </summary>
        /// <param name="client">The Discord gateway client.</param>
        /// <param name="channelId">The channel ID to send the message to.</param>
        /// <param name="result">The media link result to create a card for.</param>
        /// <param name="userId">The user ID who shared the link.</param>
        /// <param name="baseUrl">The base URL for the card service.</param>
        /// <returns>True if the message was sent successfully.</returns>
        internal static async Task<bool> SendLinkMessage( GatewayClient client, ulong channelId, MediaLinkResult result, ulong userId, IMediaCardService cardService, string baseUrl ) {
            string cardId = cardService.StoreResult( result );
            string cardUrl = $"{baseUrl.TrimEnd( '/' )}/music/card/{cardId}";
            
            _ = await client.Rest.SendMessageAsync(
                channelId,
                new NetCord.Rest.MessageProperties {
                    Content = $"<@{userId}> Shared: {cardUrl}",
                    AllowedMentions = NetCord.Rest.AllowedMentionsProperties.None
                }
            );
            return true;
        }

        /// <summary>
        /// The primary handler method that is called when a new message is created in a channel the bot has access to.
        /// </summary>
        /// <param name="message">The input event message.</param>
        public async ValueTask HandleAsync( GatewayClient client, Message message ) {
            if (client.Shard.HasValue && client.Shard.Value.Id != _nodeNumber) { return; }
            if (message.Author.IsBot) { return; }

            string content = message.Content.Trim();

            bool messageSent = false;
            List<string> inputLinks = [];
            await foreach (MediaLinkResult result in _linkLookupService.GetInfoAsync( content )) {
                inputLinks.AddRange( result._inputLinks );
                messageSent = await SendLinkMessage( client, message.ChannelId, result, message.Author.Id, _cardService, _baseUrl );
            }

            // If we sent an embed message and the input message only contained valid links, delete the input message
            if (messageSent && Regex.IsMatch( content, $"^{CombinedInputLinksRegexEscaped( inputLinks )}$" )) {
                await message.DeleteAsync( );
            }

            return;
        }

        private static string CombinedInputLinksRegexEscaped( List<string> inputLinks ) {
            return string.Join( @"\s*", inputLinks.Select( link => Regex.Escape( link ) ) );
        }
    }
}
