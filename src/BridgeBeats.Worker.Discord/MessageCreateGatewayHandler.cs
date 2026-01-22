using System.Text.RegularExpressions;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Worker.Discord.Extensions;
using BridgeBeats.Worker.Discord.Services;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;

namespace BridgeBeats.Worker.Discord {

    /// <summary>
    /// Handles Discord message creation events to detect and respond to music links.
    /// </summary>
    /// <param name="discordConfig">Configuration containing the node identifier for sharding.</param>
    /// <param name="apiClient">HTTP client for calling BridgeBeats Web API.</param>
    public partial class MessageCreateGatewayHandler(
        DiscordNodeConfig discordConfig,
        BridgeBeatsApiClient apiClient
    ) : IMessageCreateShardedGatewayHandler {

        private readonly int _nodeNumber = discordConfig.NodeNumber;
        private readonly BridgeBeatsApiClient _apiClient = apiClient;

        /// <summary>
        /// Sends a message with a link to the OpenGraph card for the media link result.
        /// </summary>
        /// <param name="client">The Discord gateway client.</param>
        /// <param name="channelId">The channel ID to send the message to.</param>
        /// <param name="result">The media link result to create a card for.</param>
        /// <param name="userId">The user ID who shared the link.</param>
        /// <param name="apiClient">HTTP client for storing the result and generating a card URL.</param>
        /// <returns>True if the message was sent successfully.</returns>
        internal static async Task<bool> SendLinkMessage(
            GatewayClient client,
            ulong channelId,
            MediaLinkResult result,
            ulong userId,
            BridgeBeatsApiClient apiClient
        ) {
            // When OpenGraph card service is enabled, use a Discord native embed with the card URL
            // as the clickable title/image link, while provider links remain inline and clickable
            if (apiClient.IsCardServiceEnabled) {
                string? cardUrl = await apiClient.StoreCardAsync( result );
                if (cardUrl != null) {
                    _ = await client.Rest.SendMessageAsync(
                        channelId,
                        result.ToDiscordMessagePropertiesWithCardUrl( userId, cardUrl )
                    );
                } else {
                    // Fall back to non-card embed if storage failed
                    _ = await client.Rest.SendMessageAsync(
                        channelId,
                        result.ToDiscordMessageProperties( userId )
                    );
                }
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

            string content = message.Content.Trim( );

            // Extract music links from the message content before API call
            List<string> inputLinks = ExtractMusicLinks( content );
            if (inputLinks.Count == 0) { return; }

            bool messageSent = false;
            await foreach (MediaLinkResult result in _apiClient.GetInfoAsync( content )) {
                if (result.Results.Count > 0) {
                    messageSent = await SendLinkMessage( client, message.ChannelId, result, message.Author.Id, _apiClient );
                }
            }

            // If we sent an embed message and the input message only contained valid links, delete the input message
            if (messageSent && Regex.IsMatch( content, $"^{CombinedInputLinksRegexEscaped( inputLinks )}$" )) {
                await message.DeleteAsync( );
            }

            return;
        }

        /// <summary>
        /// Extracts music platform URLs from the message content.
        /// </summary>
        /// <param name="content">The message content to extract links from.</param>
        /// <returns>A list of extracted music links.</returns>
        private static List<string> ExtractMusicLinks( string content ) {
            List<string> links = [];

            // Match Spotify, Apple Music, and Tidal URLs
            MatchCollection matches = MusicLinkPattern( ).Matches( content );
            foreach (Match match in matches) {
                links.Add( match.Value );
            }

            return links;
        }

        /// <summary>
        /// Regex pattern for matching music platform URLs.
        /// </summary>
        [GeneratedRegex( @"https?://(?:open\.spotify\.com|music\.apple\.com|tidal\.com|listen\.tidal\.com)/\S+", RegexOptions.IgnoreCase )]
        private static partial Regex MusicLinkPattern( );

        private static string CombinedInputLinksRegexEscaped( List<string> inputLinks )
            => string.Join( @"\s*", inputLinks.Select( Regex.Escape ) );

    }
}
