using System.Text.RegularExpressions;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using TuneBridge.Configuration;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Implementations.Extensions;
using TuneBridge.Domain.Interfaces;

namespace TuneBridge.Domain.Implementations.DiscordGatewayHandlers {

    public partial class MessageCreateGatewayHandler(
        DiscordNodeConfig discordConfig
    ) : IMessageCreateShardedGatewayHandler {

        private readonly int _nodeNumber = discordConfig.NodeNumber;
        private readonly IMediaLinkService _linkLookupService = discordConfig.LinkLookupService;

        internal async Task<bool> SendLinkMessage( GatewayClient client, ulong channelId, MediaLinkResult result, ulong userId ) {
            _ = await client.Rest.SendMessageAsync(
                channelId,
                result.ToDiscordMessageProperties( userId )
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
                messageSent = await SendLinkMessage( client, message.ChannelId, result, message.Author.Id );
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
