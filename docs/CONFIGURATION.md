# Configuration Guide

This guide covers how to configure BridgeBeats with API credentials and environment variables.

## Environment Variables

BridgeBeats requires API credentials for at least one music provider (Apple Music, Spotify, or Tidal). Discord integration is optional.

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
| `ATPROTO_IDENTIFIER` | ATProto account identifier (handle or DID) | No* |
| `ATPROTO_PASSWORD` | ATProto app password | No* |

\* Required only if using ATProto PDS storage for caching lookup results. See [ATProto Lexicon Resolution Setup Guide](ATPROTO_LEXICON.md) for complete configuration instructions.

### Discord Worker Configuration

The Discord integration runs as a separate worker service (`BridgeBeats.Worker.Discord`). These settings are only required if deploying the Discord worker:

| Variable | Description | Required |
|----------|-------------|----------|
| `BridgeBeats__DiscordToken` | Your Discord bot token | Yes** |
| `BridgeBeats__NodeNumber` | Node number for Discord sharding | No (default: `0`) |
| `BridgeBeats__BaseUrl` | Base URL for API calls (e.g., `https://bridgebeats.link`) | Yes** |

\*\* Required only when deploying the Discord worker service

### Optional Configuration

| Variable | Description | Default |
|----------|-------------|---------|
| `ALLOWED_HOSTS` | Allowed hosts for the web server | `*` |
| `DEFAULT_LOGLEVEL` | Default logging level | `Information` |
| `HOSTING_DEFAULT_LOGLEVEL` | ASP.NET hosting logging level | `Information` |
| `OTLP_ENDPOINT` | OpenTelemetry OTLP endpoint for Aspire Dashboard | `http://aspire-dashboard:4317` |
| `LOG_FILE_PATH` | File path for log files | `/app/data/logs/bridgebeats-.log` |
| `CACHE_DAYS` | Number of days to cache ATProto PDS lookup results | `7` |
| `REDIS_CONNECTION_STRING` | Redis connection string for caching and queuing | `localhost:6379` (provided by Aspire) |
| `BridgeBeats__IdentityConnectionString` | SQLite connection string for identity database | `Data Source=bridgebeats.db` |
| `BridgeBeats__BaseUrl` | Base URL for the application (for OpenGraph card URLs) | `localhost` |
| `CARD_CACHE_EXPIRATION_HOURS` | Number of hours to cache OpenGraph cards in memory | `1` |
| `CARD_CACHE_CLEANUP_INTERVAL` | Number of operations between cleanup cycles for expired cards | `500` |
| `RATE_LIMIT_RETRY_THRESHOLD` | Re-queue requests if Retry-After exceeds this (format: HH:MM:SS) | `00:02:00` |
| `JOB_EXPIRATION_MINUTES` | Minutes before incomplete lookup jobs expire | `60` |

**Note**: Environment variables use double underscores (`__`) to denote nested configuration sections (e.g., `BridgeBeats__BaseUrl` maps to `BridgeBeats:BaseUrl` in configuration).

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

### ATProto PDS Credentials (Optional - for caching)

If you want to store lookup results on a ATProto PDS for persistent caching:

1. Create a ATProto account at [bsky.app](https://bsky.app) if you don't have one
2. Go to Settings → App Passwords
3. Create a new app password for BridgeBeats
4. Use your handle (e.g., `yourname.bsky.social`) as `ATPROTO_IDENTIFIER`
5. Use the generated app password as `ATPROTO_PASSWORD`

**Note**: Lookup results are stored as custom AT Protocol lexicon records on your PDS. Input links with tracking parameters are kept private in Redis for efficient lookup (URL hashes only) and are never exposed publicly.

### Redis Configuration (Required - provided by Aspire)

Redis is used for distributed caching, request queuing, and rate limit tracking. In development, Aspire provides and configures Redis automatically. In production:

1. Deploy a Redis instance (standalone or cluster)
2. Set `REDIS_CONNECTION_STRING` to your Redis endpoint
3. Recommended: Use Redis with persistence (RDB or AOF) for queue durability
4. Recommended: Enable TLS and authentication for production deployments

**Note**: Redis stores only lookup indices and queue messages. All actual MediaLinkResult data is stored on ATProto PDS.

## Configuration Files

### appsettings.json

For local development, you can use an `appsettings.json` file instead of environment variables:

```json
{
  "BridgeBeats": {
    "NodeNumber": 0,
    "AppleTeamId": "your_team_id",
    "AppleKeyId": "your_key_id",
    "AppleKeyPath": "/path/to/AuthKey.p8",
    "SpotifyClientId": "your_client_id",
    "SpotifyClientSecret": "your_client_secret",
    "TidalClientId": "your_tidal_client_id",
    "TidalClientSecret": "your_tidal_client_secret",
    "DiscordToken": "your_bot_token",
    "IdentityConnectionString": "Data Source=bridgebeats.db",
    "ATProtoIdentifier": "your-handle.bsky.social",
    "ATProtoPassword": "your-app-password",
    "CacheDays": 7,
    "RedisConnectionString": "localhost:6379",
    "BaseUrl": "localhost",
    "LogDirPath": "./logs",
    "CardCacheExpirationHours": 1,
    "CardCacheCleanupInterval": 500,
    "RateLimitRetryThreshold": "00:02:00",
    "JobExpirationMinutes": 60
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "OpenTelemetry": {
    "OtlpEndpoint": "http://aspire-dashboard:4317"
  },
  "AllowedHosts": "localhost"
}
```

### Docker Configuration

When using Docker, environment variables are passed via the `-e` flag or docker compose:

```bash
docker run -p 10000:10000 \
  -e APPLE_TEAM_ID="your_team_id" \
  -e APPLE_KEY_ID="your_key_id" \
  -e APPLE_KEY_PATH="/app/key.p8" \
  -e SPOTIFY_CLIENT_ID="your_client_id" \
  -e SPOTIFY_CLIENT_SECRET="your_client_secret" \
  -v /path/to/your/AuthKey_KEYID.p8:/app/key.p8 \
  bridgebeats
```

**Important:** The `APPLE_KEY_PATH` environment variable must match the container mount path.

## Security Best Practices

1. **Never commit credentials to source control** - Always use environment variables or secure secret management
2. **Use app passwords for ATProto** - Generate dedicated app passwords instead of using your main account password
3. **Rotate API keys regularly** - Periodically regenerate API keys and update your configuration
4. **Restrict API key permissions** - Only grant the minimum required permissions for each service
5. **Use HTTPS in production** - Always deploy behind a reverse proxy with SSL/TLS enabled

## Validation

BridgeBeats validates configuration at startup. If required credentials are missing or invalid, the application will log warnings and disable features that depend on those credentials.

Check the application logs on startup for any configuration warnings:

```
[Warning] Apple Music credentials not configured - Apple Music lookups will be unavailable
[Warning] Spotify credentials not configured - Spotify lookups will be unavailable
[Information] Discord token not provided - Discord bot will not be started
```

## Logging Configuration

BridgeBeats supports two logging destinations that work simultaneously:

### File Logging

Logs are written to persistent files with automatic rotation and retention:

- **Location**: `/app/data/logs/bridgebeats-.log` (configurable via `LOG_FILE_PATH`)
- **Rotation**: Daily rotation + size-based rotation (10MB per file)
- **Retention**: Maximum 5 log files (oldest files are automatically deleted)
- **Total Size**: Up to ~50MB total log storage

File logging provides local backup for diagnostics when the Aspire Dashboard is unavailable.

### OpenTelemetry (OTLP) Logging

Logs are sent to the Aspire Dashboard for real-time observability:

- **Endpoint**: `http://aspire-dashboard:4317` (configurable via `OTLP_ENDPOINT`)
- **Protocol**: OpenTelemetry Protocol (OTLP)
- **Dashboard**: Access via Aspire Dashboard (requires `AspireDashboardAccess` role)

To disable OpenTelemetry logging, set `OTLP_ENDPOINT` to an empty string.

### Log Levels

Configure log verbosity using these environment variables:

- `DEFAULT_LOGLEVEL`: Overall application log level (`Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`)
- `HOSTING_DEFAULT_LOGLEVEL`: ASP.NET hosting infrastructure log level

Example in docker-compose.yml:

```yaml
environment:
  - DEFAULT_LOGLEVEL=Debug
  - HOSTING_DEFAULT_LOGLEVEL=Information
  - OTLP_ENDPOINT=http://aspire-dashboard:4317
  - LOG_FILE_PATH=/app/data/logs/bridgebeats-.log
```

### Accessing Logs

**File Logs** (Docker):
```bash
# View logs from the persistent volume
docker exec -it bridgebeats cat /app/data/logs/bridgebeats-*.log

# Tail logs in real-time
docker exec -it bridgebeats tail -f /app/data/logs/bridgebeats-*.log
```

**OpenTelemetry Logs** (Aspire Dashboard):
1. Access the Aspire Dashboard at `http://localhost:18888` (or your configured endpoint)
2. Ensure your user has the `AspireDashboardAccess` role (see [Aspire Dashboard Access Guide](ASPIRE_DASHBOARD_ACCESS.md))
3. Navigate to the "Logs" section to view real-time logs with filtering and search

### Log Rotation Details

Log files are automatically managed:

1. **Daily rotation**: New file created each day (e.g., `bridgebeats-20250114.log`)
2. **Size-based rotation**: When a file reaches 10MB, a new file is created
3. **Retention**: Only the 5 most recent files are kept
4. **Automatic cleanup**: Old files are deleted when retention limit is reached

This ensures the container doesn't accumulate excessive log data while maintaining diagnostic history.
