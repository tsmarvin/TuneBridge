using System.Text.RegularExpressions;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Rest;
using TuneBridge.Configuration;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Implementations.Extensions;
using TuneBridge.Domain.Interfaces;

namespace TuneBridge.Domain.Implementations.DiscordGatewayHandlers {

    /// <summary>
    /// Handles Discord message creation events to detect and respond to music links.
    /// </summary>
    /// <param name="discordConfig">Configuration containing the media link service and node identifier.</param>
    /// <param name="cardService">Service for storing MediaLinkResult objects for OpenGraph card generation.</param>
    /// <param name="configuration">Application configuration for retrieving base URL.</param>
    public partial class MessageCreateGatewayHandler(
        DiscordNodeConfig discordConfig,
        IOpenGraphCardService cardService,
        IConfiguration configuration
    ) : IMessageCreateShardedGatewayHandler {

        private readonly int _nodeNumber = discordConfig.NodeNumber;
        private readonly IMediaLinkService _linkLookupService = discordConfig.LinkLookupService;
        private readonly IOpenGraphCardService _cardService = cardService;
        private readonly IConfiguration _configuration = configuration;

        /// <summary>
        /// Sends a message with a link to the OpenGraph card for the media link result.
        /// </summary>
        /// <param name="client">The Discord gateway client.</param>
        /// <param name="channelId">The channel ID to send the message to.</param>
        /// <param name="result">The media link result to create a card for.</param>
        /// <param name="userId">The user ID who shared the link.</param>
        /// <param name="cardService">Service for storing the result and generating a card ID.</param>
        /// <param name="baseUrl">The base URL for the application.</param>
        /// <returns>True if the message was sent successfully.</returns>
        internal static async Task<bool> SendLinkMessage( 
            GatewayClient client, 
            ulong channelId, 
            MediaLinkResult result, 
            ulong userId,
            IOpenGraphCardService cardService,
            string baseUrl ) {
            
            // Store the result and get a unique card ID
            string cardId = cardService.StoreResult( result );
            
            // Generate the OpenGraph card URL
            string cardUrl = $"{baseUrl.TrimEnd( '/' )}/card/{cardId}";
            
            // Send a simple message with the card URL (Discord will auto-embed the OpenGraph preview)
            _ = await client.Rest.SendMessageAsync(
                channelId,
                new MessageProperties {
                    Content = $"<@{userId}> Shared: {cardUrl}",
                    AllowedMentions = AllowedMentionsProperties.None
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

            // Get the base URL from configuration, default to localhost for development
            string baseUrl = _configuration["TuneBridge:BaseUrl"] ?? "http://localhost:5000";

            bool messageSent = false;
            List<string> inputLinks = [];
            await foreach (MediaLinkResult result in _linkLookupService.GetInfoAsync( content )) {
                inputLinks.AddRange( result._inputLinks );
                messageSent = await SendLinkMessage( client, message.ChannelId, result, message.Author.Id, _cardService, baseUrl );
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
