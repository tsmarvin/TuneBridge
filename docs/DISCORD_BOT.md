# Discord Bot Guide

This guide explains how to use the TuneBridge Discord bot, including setup and what to expect when sharing music links in your Discord server.

## 🚀 Getting Started

### Adding the Bot to Your Server

1. Click to [add the Discord bot](https://discord.com/oauth2/authorize?client_id=1417324891161759815)
2. Select the Discord server where you want to add the bot
3. Click **Authorize**

The bot will now appear in your server and is ready to convert music links!

## 🎵 How It Works

### Sharing Music Links

When you or anyone in your server shares a music link from **Spotify**, **Apple Music**, or **Tidal**, TuneBridge will automatically:

1. Detect the link
2. Find the same track or album on other music platforms
3. Post an embed with:
   - Album artwork
   - Links to all available platforms
   - Credit to whoever shared the link

### Message Cleanup

To keep your channels clean, TuneBridge automatically **deletes link-only messages** after posting the conversion embed. For example:

- ✅ **Deleted**: A message containing just `https://open.spotify.com/track/...`
- ✅ **Deleted**: Multiple links with only whitespace between them
- ✅ **Kept**: A message like "Check this out! https://open.spotify.com/track/..."
- ✅ **Kept**: Any message with text or other content alongside the link

The key rule: **If your message is only music links (and whitespace), it gets deleted. Otherwise, it stays.**

If you want to keep a link around, just add a comment or emoji with it!

## 🔐 Channel Permissions

### Required Bot Permissions

For the bot to work properly, it needs these permissions in your channels:

| Permission | Purpose |
|-----------|---------|
| **Read Messages/View Channels** | Detect music links in messages |
| **Send Messages** | Post conversion embeds |
| **Embed Links** | Display music previews with rich formatting |
| **Manage Messages** | Delete link-only messages for cleaner conversations |

**Note:** If the bot lacks the **Manage Messages** permission, it will still convert links and post embeds, but won't be able to delete the original link-only messages.

### Restricting the Bot to Specific Channels

If you want to limit where TuneBridge operates in your server:

1. Go to **Server Settings** → **Roles**
2. Find the **TuneBridge** role
3. Go to the channel you want to restrict
4. Edit **Channel Permissions**
5. Find the **TuneBridge** role and deny **Send Messages** or **View Channel**

#### Quick Examples

- **#music** - Bot fully active (recommended)
- **#serious-discussion** - Bot can't send messages (deny Send Messages)
- **#announcements** - Bot has no access (deny View Channel)

## 🛠️ Troubleshooting

### Bot Not Responding

1. **Check if the bot is online**: Look at the server member list - you should see the TuneBridge bot with a green online indicator
2. **Verify permissions**: Make sure the bot has **Send Messages**, **Read Messages**, and **Embed Links** permissions in the channel
3. **Validate the link**: Ensure you're sharing a valid link from Spotify, Apple Music, or Tidal (not a screenshot or description)
4. **Try Directly On the TuneBridge Website**: Validate the link on [TuneBridge](https://dev.tunebridge.media) to see if it can find matches

If the bot still isn't responding after these steps, contact your server admin - they may need to check the TuneBridge configuration on their end.
As a last resort, you can submit an issue on [GitHub](https://github.com/tsmarvin/TuneBridge/issues)

### Bot Can't Find a Match

Sometimes a track or album isn't available on all platforms. If the bot doesn't respond to your link, it means:

- The song/album isn't available on the other platforms
- It may have been removed or is region-restricted
- The bot will simply skip it (no error message or response)

Try a different song or check if it's available in your region on the music app directly.

### Messages Not Deleting

The bot only deletes **link-only messages**. If your message has any text or other content with the link, it will be preserved so your conversation isn't lost.

Additionally, the bot needs **Manage Messages** permission to delete messages. If it doesn't have this permission, links won't be deleted (but they'll still be converted).

## ❓ FAQ

**Q: What platforms does the bot support?**
A: Spotify, Apple Music, and Tidal. Share a link from any of these, and get matches on the others!

**Q: Does the bot work in DMs?**
A: No, it only works in server channels.

**Q: Can I prevent the bot from deleting my message?**
A: Yes! Add a comment, reaction, or any text with your link and it won't be deleted.

**Q: What if the bot is broken or not responding?**
A: First, check that the bot is online in your server member list and has the required permissions. If it's still not working, contact your server admin or submit an issue on [GitHub](https://github.com/tsmarvin/TuneBridge/issues).

**Q: Who can see the bot's messages?**
A: Everyone in the channel can see the conversion embeds posted by the bot. The embeds display music service URLs (Spotify, Apple Music, Tidal links) and album artwork so that anyone in the channel can easily access the music from their preferred platform. Your original message is visible to everyone until deleted (which only happens for link-only messages).

**Q: Can I see which parts of my message the bot kept?**
A: The bot keeps your entire message if it contains anything other than music links—including text, emojis, or other content. Only pure link messages are replaced with the conversion embed.

---

For more information or to report issues, visit the [TuneBridge GitHub](https://github.com/tsmarvin/TuneBridge).
