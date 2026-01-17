using System.Text.RegularExpressions;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Services.Extensions;
using BridgeBeats.Web.Discord;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;

namespace BridgeBeats.Web.Discord {

    /// <summary>
    /// Handles Discord message creation events to detect and respond to music links.
    /// </summary>
    /// <param name="discordConfig">Configuration containing the media link service and node identifier.</param>
    /// <param name="cardService">Service for storing MediaLinkResult objects for OpenGraph card generation.</param>
    public partial class MessageCreateGatewayHandler(
        DiscordNodeConfig discordConfig,
        IOpenGraphCardService cardService
    ) : IMessageCreateShardedGatewayHandler {

        private readonly int _nodeNumber = discordConfig.NodeNumber;
        private readonly IMediaLinkService _linkLookupService = discordConfig.LinkLookupService;
        private readonly IOpenGraphCardService _cardService = cardService;

        /// <summary>
        /// Sends a message with a link to the OpenGraph card for the media link result.
        /// </summary>
        /// <param name="client">The Discord gateway client.</param>
        /// <param name="channelId">The channel ID to send the message to.</param>
        /// <param name="result">The media link result to create a card for.</param>
        /// <param name="userId">The user ID who shared the link.</param>
        /// <param name="cardService">Service for storing the result and generating a card ID.</param>
        /// <returns>True if the message was sent successfully.</returns>
        internal static async Task<bool> SendLinkMessage(
            GatewayClient client,
            ulong channelId,
            MediaLinkResult result,
            ulong userId,
            IOpenGraphCardService cardService
        ) {
            // When OpenGraph card service is enabled, use a Discord native embed with the card URL
            // as the clickable title/image link, while provider links remain inline and clickable
            if (cardService.IsEnabled) {
                string cardUrl = cardService.StoreResult( result );
                _ = await client.Rest.SendMessageAsync(
                    channelId,
                    result.ToDiscordMessagePropertiesWithCardUrl( userId, cardUrl )
                );
            } else {
                // When OpenGraph is disabled, use the original non-OpenGraph approach
                _ = await client.Rest.SendMessageAsync(
                    channelId,
                    result.ToDiscordMessageProperties( userId )
                );
            }
            return true;
        }

        /// <summary>
        /// The primary handler method that is called when a new message is created in a channel the bot has access to.
        /// </summary>
        /// <param name="client">The gateway client that received the message.</param>
        /// <param name="message">The input event message.</param>
        public async ValueTask HandleAsync( GatewayClient client, Message message ) {
            if (client.Shard.HasValue && client.Shard.Value.Id != _nodeNumber) { return; }
            if (message.Author.IsBot) { return; }

            string content = message.Content.Trim();

            bool messageSent = false;
            List<string> inputLinks = [];
            await foreach (MediaLinkResult result in _linkLookupService.GetInfoAsync( content )) {
                inputLinks.AddRange( result._inputLinks );
                messageSent = await SendLinkMessage( client, message.ChannelId, result, message.Author.Id, _cardService );
            }

            // If we sent an embed message and the input message only contained valid links, delete the input message
            if (messageSent && Regex.IsMatch( content, $"^{CombinedInputLinksRegexEscaped( inputLinks )}$" )) {
                await message.DeleteAsync( );
            }

            return;
        }

        private static string CombinedInputLinksRegexEscaped( List<string> inputLinks )
            => string.Join( @"\s*", inputLinks.Select( Regex.Escape ) );

    }
}
