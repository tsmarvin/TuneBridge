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
| `BridgeBeats__Domain` | Base host for API calls (for example, `bridgebeats.link`) | Yes** |

\*\* Required only when deploying the Discord worker service

### Optional Configuration

| Variable | Description | Default |
|----------|-------------|---------|
| `DEFAULT_LOGLEVEL` | Default logging level | `Information` |
| `HOSTING_DEFAULT_LOGLEVEL` | ASP.NET hosting logging level | `Information` |
| `OTLP_ENDPOINT` | OpenTelemetry OTLP endpoint for Aspire Dashboard | empty (disabled) |
| `LOG_DIR_PATH` | Directory for log files | `/app/data/logs` |
| `CACHE_DAYS` | Number of days to cache ATProto PDS lookup results | `7` |
| `REDIS_HOST` | Redis hostname | `redis` |
| `REDIS_PORT` | Redis port | `6379` |
| `IDENTITY_CONNECTION_STRING` | SQLite connection string for identity database | `Data Source=/app/data/bridgebeats.db` |
| `BridgeBeats__Domain` | Base domain for the application (for OpenGraph card URLs and dashboard auth cookie scope) | `localhost` |
| `CARD_CACHE_EXPIRATION_HOURS` | Number of hours to cache OpenGraph cards in memory | `1` |
| `CARD_CACHE_CLEANUP_INTERVAL` | Number of operations between cleanup cycles for expired cards | `500` |

The container entrypoint reads `REDIS_HOST` and `REDIS_PORT` (plus the `redis_password` secret) and assembles the connection string the application consumes; there is no single `REDIS_CONNECTION_STRING` variable to set.

The container image fixes `AllowedHosts` to `*` and reads no `ALLOWED_HOSTS` environment variable; host filtering is Caddy's responsibility in this topology.

Job-queue tuning (`RateLimitRetryThreshold`, default `00:02:00`; `JobExpirationMinutes`, default `2880`) lives under the `BridgeBeats:Queue` configuration section. Set these through `appsettings.json` rather than the container environment.

**Note**: Environment variables use double underscores (`__`) to denote nested configuration sections (for example, `BridgeBeats__Domain` maps to `BridgeBeats:Domain` in configuration).

`BridgeBeats__Domain` must be a host-only value (no `http://`/`https://`, no path, and no port).

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
4. Use your handle (for example, `yourname.bsky.social`) as `ATPROTO_IDENTIFIER`
5. Use the generated app password as `ATPROTO_PASSWORD`

**Note**: Lookup results are stored as custom AT Protocol lexicon records on your PDS. Input links with tracking parameters are kept private in Redis for efficient lookup (URL hashes only) and are never exposed publicly.

### Redis Configuration (Required - provided by Aspire)

Redis is used for distributed caching, request queuing, and rate limit tracking. In development, Aspire provides and configures Redis automatically. In production:

1. Deploy a Redis instance (standalone or cluster)
2. Set `REDIS_HOST` and `REDIS_PORT` to your Redis endpoint, and provide the `redis_password` secret for authentication
3. Recommended: Use Redis with persistence (RDB or AOF) for queue durability
4. Recommended: Enable TLS and authentication for production deployments

**Note**: Redis stores only lookup indices and queue messages. All actual MediaLinkResult data is stored on ATProto PDS.

## Configuration Files

### appsettings.json

For local development, you can use an `appsettings.json` file instead of environment variables:

```json
{
  "BridgeBeats": {
    "NodeNumber": 100,
    "AppleTeamId": "your_team_id",
    "AppleKeyId": "your_key_id",
    "AppleKeyPath": "/path/to/AuthKey.p8",
    "SpotifyClientId": "your_client_id",
    "SpotifyClientSecret": "your_client_secret",
    "TidalClientId": "your_tidal_client_id",
    "TidalClientSecret": "your_tidal_client_secret",
    "DiscordToken": "your_bot_token",
    "IdentityConnectionString": "Data Source=bridgebeats.db",
    "ApiKeySalt": "your_api_key_salt",
    "RateLimitRequestsPerHour": 20,
    "ATProtoIdentifier": "your-handle.bsky.social",
    "ATProtoPassword": "your-app-password",
    "ATProtoUserDID": "",
    "CacheDays": 7,
    "Domain": "localhost",
    "LogDirPath": "./logs",
    "DataProtectionKeyPath": "./keys",
    "CardCacheExpirationHours": 1,
    "CardCacheCleanupInterval": 500,
    "Resilience": {
      "MaxRetryAfterSeconds": 120,
      "MaxRetryAttempts": 5,
      "TotalTimeoutMinutes": 10,
      "AttemptTimeoutSeconds": 120
    },
    "Queue": {
      "RateLimitRetryThreshold": "00:02:00",
      "JobExpirationMinutes": 2880,
      "Weights": {
        "Interactive": 5,
        "Background": 2,
        "Bulk": 1
      }
    }
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "OpenTelemetry": {
    "OtlpEndpoint": "",
    "EnableTracing": true,
    "EnableMetrics": true
  },
  "AllowedHosts": "localhost;127.0.0.1"
}
```

### HTTP Resilience

The `BridgeBeats:Resilience` block tunes the HTTP resilience pipeline applied to all outbound HTTP clients: provider APIs, the Discord worker's calls to the Web API, and CAR repo downloads.

| Key | Default | Meaning |
|---|---|---|
| `AttemptTimeoutSeconds` | `120` | Per-attempt timeout. Keep it above the ~90s server-side lookup budget so slow but legitimate lookups are not killed mid-flight. |
| `TotalTimeoutMinutes` | `10` | Ceiling across all retry attempts for one logical request. Must exceed `AttemptTimeoutSeconds`. |
| `MaxRetryAttempts` | `5` | Retry count for transient failures on safe (GET) requests. POST is never retried automatically. |
| `MaxRetryAfterSeconds` | `120` | Largest `Retry-After` value honored before failing fast. |

The circuit-breaker sampling window is derived as `2 × AttemptTimeoutSeconds` (240s at the default). Raising `AttemptTimeoutSeconds` widens that window and slows failure detection on hung endpoints; the total timeout and retry cap still bound the operation.

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

- **Location**: the directory set by `LOG_DIR_PATH` (default `/app/data/logs`); files are named `bridgebeats-<date>.log`
- **Rotation**: Daily rotation plus size-based rotation (50MB per file)
- **Retention**: Maximum 5 log files per service (oldest files are deleted)
- **Total Size**: Up to ~250MB per service

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
  - LOG_DIR_PATH=/app/data/logs
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
1. Local development: access the Aspire Dashboard at `http://localhost:18888`
2. Production: access the Aspire Dashboard at `https://dashboard.<your-domain>`
3. Ensure your user has the `AspireDashboardAccess` role
4. Navigate to the "Logs" section to view real-time logs with filtering and search

### Log Rotation Details

Log files are automatically managed:

1. **Daily rotation**: New file created each day (for example, `bridgebeats-20250114.log`)
2. **Size-based rotation**: When a file reaches 50MB, a new file is created
3. **Retention**: Only the 5 most recent files are kept
4. **Automatic cleanup**: Old files are deleted when retention limit is reached

This ensures the container doesn't accumulate excessive log data while maintaining diagnostic history.
