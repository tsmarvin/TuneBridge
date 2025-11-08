# Configuration Guide

This guide covers how to configure TuneBridge with API credentials and environment variables.

## Environment Variables

TuneBridge requires API credentials for at least one music provider (Apple Music, Spotify, or Tidal). Discord integration is optional.

### Required Music Provider Credentials

At least one complete set of music provider credentials is required:

| Variable | Description | Required |
|----------|-------------|----------|
| `APPLE_TEAM_ID` | Your Apple Developer Team ID | No* |
| `APPLE_KEY_ID` | Your Apple Music API Key ID | No* |
| `APPLE_KEY_PATH` | Path to your Apple Music private key (.p8 file) | No* |
| `SPOTIFY_CLIENT_ID` | Your Spotify API Client ID | No* |
| `SPOTIFY_CLIENT_SECRET` | Your Spotify API Client Secret | No* |
| `TIDAL_CLIENT_ID` | Your Tidal API Client ID | No* |
| `TIDAL_CLIENT_SECRET` | Your Tidal API Client Secret | No* |

\* At least one complete set of music provider credentials is required.

### Optional Integration Credentials

| Variable | Description | Required |
|----------|-------------|----------|
| `DISCORD_TOKEN` | Your Discord bot token | No** |
| `BLUESKY_PDS_URL` | Bluesky PDS URL for storing lookup results | No*** |
| `BLUESKY_IDENTIFIER` | Bluesky account identifier (handle or DID) | No*** |
| `BLUESKY_PASSWORD` | Bluesky app password | No*** |

\*\* Required only if using Discord integration  
\*\*\* Required only if using Bluesky PDS storage for caching lookup results

### Optional Configuration

| Variable | Description | Default |
|----------|-------------|---------|
| `NODE_NUMBER` | Node number for Discord sharding | `0` |
| `ALLOWED_HOSTS` | Allowed hosts for the web server | `*` |
| `DEFAULT_LOGLEVEL` | Default logging level | `Information` |
| `HOSTING_DEFAULT_LOGLEVEL` | ASP.NET hosting logging level | `Information` |
| `CACHE_DAYS` | Number of days to cache Bluesky PDS lookup results | `7` |
| `CACHE_DB_PATH` | Path to SQLite database for cache lookups | `medialinkscache.db` |
| `TuneBridge__BaseUrl` | Base URL for the application (for OpenGraph card URLs) | `http://localhost:5000` |

**Note**: Environment variables use double underscores (`__`) to denote nested configuration sections (e.g., `TuneBridge__BaseUrl` maps to `TuneBridge:BaseUrl` in configuration).

## Obtaining API Credentials

### Apple Music API Credentials

1. Visit the [Apple Developer Portal](https://developer.apple.com/account)
2. Follow the guide to [Create a Media Identifier and Private Key](https://developer.apple.com/help/account/configure-app-capabilities/create-a-media-identifier-and-private-key/)
3. Download your `.p8` private key file
4. Note your Team ID and Key ID

### Spotify API Credentials

1. Visit the [Spotify Developer Dashboard](https://developer.spotify.com/dashboard)
2. Follow the guide to [Register Your App](https://developer.spotify.com/documentation/general/guides/app-settings/#register-your-app)
3. Note your Client ID and Client Secret

### Tidal API Credentials

1. Visit the [Tidal Developer Portal](https://developer.tidal.com/)
2. Create a new application to get API access
3. Note your Client ID and Client Secret

### Discord Bot Token

1. Visit the [Discord Developer Portal](https://discord.com/developers/applications)
2. Follow the [Getting Started Guide](https://discord.com/developers/docs/quick-start/getting-started)
3. Create a bot and copy its token
4. Invite the bot to your server with appropriate permissions (Read Messages, Send Messages, Embed Links, Manage Messages)

### Bluesky PDS Credentials (Optional - for caching)

If you want to store lookup results on a Bluesky PDS for persistent caching:

1. Create a Bluesky account at [bsky.app](https://bsky.app) if you don't have one
2. Go to Settings → App Passwords
3. Create a new app password for TuneBridge
4. Use your handle (e.g., `yourname.bsky.social`) as `BLUESKY_IDENTIFIER`
5. Use the generated app password as `BLUESKY_PASSWORD`
6. Set `BLUESKY_PDS_URL` to `https://bsky.social` (or your custom PDS URL)

**Note**: Lookup results are stored as custom AT Protocol lexicon records on your PDS. Input links with tracking parameters are kept private in a local SQLite database for privacy protection.

## Configuration Files

### appsettings.json

For local development, you can use an `appsettings.json` file instead of environment variables:

```json
{
  "TuneBridge": {
    "NodeNumber": 0,
    "AppleTeamId": "your_team_id",
    "AppleKeyId": "your_key_id",
    "AppleKeyPath": "/path/to/AuthKey.p8",
    "SpotifyClientId": "your_client_id",
    "SpotifyClientSecret": "your_client_secret",
    "TidalClientId": "your_tidal_client_id",
    "TidalClientSecret": "your_tidal_client_secret",
    "DiscordToken": "your_bot_token",
    "ConnectionString": "Data Source=tunebridge.db",
    "BlueskyPdsUrl": "https://bsky.social",
    "BlueskyIdentifier": "your-handle.bsky.social",
    "BlueskyPassword": "your-app-password",
    "CacheDays": 7,
    "CacheDbPath": "tunebridge.db",
    "BaseUrl": "http://localhost:5000"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "AllowedHosts": "localhost"
}
```

### Docker Configuration

When using Docker, environment variables are passed via the `-e` flag or docker-compose:

```bash
docker run -p 10000:10000 \
  -e APPLE_TEAM_ID="your_team_id" \
  -e APPLE_KEY_ID="your_key_id" \
  -e APPLE_KEY_PATH="/app/key.p8" \
  -e SPOTIFY_CLIENT_ID="your_client_id" \
  -e SPOTIFY_CLIENT_SECRET="your_client_secret" \
  -v /path/to/your/AuthKey_KEYID.p8:/app/key.p8 \
  tunebridge
```

**Important:** The `APPLE_KEY_PATH` environment variable must match the container mount path.

## Security Best Practices

1. **Never commit credentials to source control** - Always use environment variables or secure secret management
2. **Use app passwords for Bluesky** - Generate dedicated app passwords instead of using your main account password
3. **Rotate API keys regularly** - Periodically regenerate API keys and update your configuration
4. **Restrict API key permissions** - Only grant the minimum required permissions for each service
5. **Use HTTPS in production** - Always deploy behind a reverse proxy with SSL/TLS enabled

## Validation

TuneBridge validates configuration at startup. If required credentials are missing or invalid, the application will log warnings and disable features that depend on those credentials.

Check the application logs on startup for any configuration warnings:

```
[Warning] Apple Music credentials not configured - Apple Music lookups will be unavailable
[Warning] Spotify credentials not configured - Spotify lookups will be unavailable
[Information] Discord token not provided - Discord bot will not be started
```
