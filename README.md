# TuneBridge

**TuneBridge** is a cross-platform music link converter and lookup service that bridges Apple Music, Spotify, and Tidal. It provides both a web interface and a Discord bot for seamless music sharing across different streaming platforms.

## Features

- 🎵 **Music Link Conversion**: Convert music links between Apple Music, Spotify, and Tidal
- 🔍 **Multiple Lookup Methods**: Search by URL, ISRC, UPC, or title/artist
- 🤖 **Discord Bot Integration**: Automatically detect and convert music links in Discord messages
- 🌐 **Web API**: RESTful API endpoints for programmatic access
- 🖥️ **Web Interface**: Simple browser-based UI for manual lookups
- 🐳 **Docker Support**: Easy deployment with Docker containers

## What It Does

TuneBridge acts as a universal translator for music streaming services. When you share a music link (song or album) from Apple Music, Spotify, or Tidal, TuneBridge automatically finds the equivalent content on the other platforms.

The application uses official APIs from all services to ensure accurate matching through standardized identifiers (ISRC for tracks, UPC for albums). When matches cannot be found via external IDs, the application performs fuzzy matching using metadata to find the equivalent content.

## Configuration

### Environment Variables

TuneBridge requires API credentials for at least one music provider (Apple Music, Spotify, or Tidal). Discord integration is optional.

| Variable | Description | Required |
|----------|-------------|----------|
| `APPLE_TEAM_ID` | Your Apple Developer Team ID | No* |
| `APPLE_KEY_ID` | Your Apple Music API Key ID | No* |
| `APPLE_KEY_PATH` | Path to your Apple Music private key (.p8 file) | No* |
| `SPOTIFY_CLIENT_ID` | Your Spotify API Client ID | No* |
| `SPOTIFY_CLIENT_SECRET` | Your Spotify API Client Secret | No* |
| `TIDAL_CLIENT_ID` | Your Tidal API Client ID | No* |
| `TIDAL_CLIENT_SECRET` | Your Tidal API Client Secret | No* |
| `DISCORD_TOKEN` | Your Discord bot token | No** |

\* At least one complete set of music provider credentials is required (Apple Music, Spotify, or Tidal)  
\*\* Required only if using Discord integration

### Optional Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `NODE_NUMBER` | Node number for Discord sharding | `0` |
| `BASE_URL` | Base URL for the web application (used for OpenGraph card URLs) | `http://localhost:10000` |
| `ALLOWED_HOSTS` | Allowed hosts for the web server | `*` |
| `DEFAULT_LOGLEVEL` | Default logging level | `Information` |
| `HOSTING_DEFAULT_LOGLEVEL` | ASP.NET hosting logging level | `Information` |

### Obtaining API Credentials

#### Apple Music API Credentials

1. Visit the [Apple Developer Portal](https://developer.apple.com/account)
2. Follow the guide to [Create a Media Identifier and Private Key](https://developer.apple.com/help/account/configure-app-capabilities/create-a-media-identifier-and-private-key/)
3. Download your `.p8` private key file
4. Note your Team ID and Key ID

#### Spotify API Credentials

1. Visit the [Spotify Developer Dashboard](https://developer.spotify.com/dashboard)
2. Follow the guide to [Register Your App](https://developer.spotify.com/documentation/general/guides/app-settings/#register-your-app)
3. Note your Client ID and Client Secret

#### Tidal API Credentials

1. Visit the [Tidal Developer Portal](https://developer.tidal.com/)
2. Create a new application to get API access
3. Note your Client ID and Client Secret

#### Discord Bot Token

1. Visit the [Discord Developer Portal](https://discord.com/developers/applications)
2. Follow the [Getting Started Guide](https://discord.com/developers/docs/quick-start/getting-started)
3. Create a bot and copy its token
4. Invite the bot to your server with appropriate permissions (Read Messages, Send Messages, Embed Links, Manage Messages)

## Running the Application

### Using Docker (Recommended)

1. Set your environment variables:
```bash
export APPLE_TEAM_ID="your_team_id"
export APPLE_KEY_ID="your_key_id"
export APPLE_KEY_PATH="/app/key.p8"
export SPOTIFY_CLIENT_ID="your_client_id"
export SPOTIFY_CLIENT_SECRET="your_client_secret"
export TIDAL_CLIENT_ID="your_tidal_client_id"
export TIDAL_CLIENT_SECRET="your_tidal_client_secret"
export DISCORD_TOKEN="your_bot_token"
export BASE_URL="https://your-domain.com"
```

2. Build and run with Docker:
```bash
docker build -t tunebridge .
docker run -p 10000:10000 \
  -e APPLE_TEAM_ID \
  -e APPLE_KEY_ID \
  -e APPLE_KEY_PATH \
  -e SPOTIFY_CLIENT_ID \
  -e SPOTIFY_CLIENT_SECRET \
  -e TIDAL_CLIENT_ID \
  -e TIDAL_CLIENT_SECRET \
  -e DISCORD_TOKEN \
  -e BASE_URL \
  -v /path/to/your/AuthKey_KEYID.p8:/app/key.p8 \
  tunebridge
```

**Important:** The `APPLE_KEY_PATH` environment variable must match the container mount path (`/app/key.p8` in this example).

The application will be available at `http://localhost:10000`

### Running Locally

1. Install [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

2. Update `appsettings.json` with your credentials:
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
    "BaseUrl": "http://localhost:10000"
  }
}
```

3. Build and run:
```bash
dotnet build
dotnet run
```

The application will start on the default ASP.NET Core ports (usually 5000/5001).

## API Endpoints

### Web Interface
- `GET /` - Web-based lookup interface

### Music Lookup API

All endpoints accept POST requests with JSON payloads:

#### Lookup by URL (List)
```http
POST /music/lookup/urlList
Content-Type: application/json

{
  "uri": "https://music.apple.com/us/album/..."
}
```

#### Lookup by URL (Streaming)
```http
POST /music/lookup/url
Content-Type: application/json

{
  "uri": "https://music.apple.com/us/album/..."
}
```

#### Lookup by ISRC
```http
POST /music/lookup/isrc
Content-Type: application/json

{
  "isrc": "USVI20000123"
}
```

#### Lookup by UPC
```http
POST /music/lookup/upc
Content-Type: application/json

{
  "upc": "123456789012"
}
```

#### Lookup by Title/Artist
```http
POST /music/lookup/title
Content-Type: application/json

{
  "title": "Song Name",
  "artist": "Artist Name"
}
```

## OpenGraph Embeddable Cards

TuneBridge generates OpenGraph embeddable cards for music lookup results. These cards can be shared on any platform that supports OpenGraph metadata (Discord, Slack, Twitter, Facebook, etc.), providing rich previews with:

- Album/track artwork
- Artist and title information
- Links to all available streaming platforms (Spotify, Apple Music, Tidal)
- ISRC/UPC identifiers for exact matching

### How It Works

When a music link is shared (via Discord bot or API), TuneBridge:
1. Looks up the track/album across all configured providers
2. Generates a unique OpenGraph card URL (e.g., `/music/card/{id}`)
3. Returns this URL, which platforms automatically preview using OpenGraph metadata

### Card URL Structure

Cards are accessible at: `{BASE_URL}/music/card/{id}`

Example: `https://your-domain.com/music/card/a1b2c3d4e5f6`

### OpenGraph Metadata

Each card includes standard OpenGraph tags:
- `og:type` - "music.song" or "music.album"
- `og:title` - Track or album title
- `og:description` - Artist, ISRC/UPC, and provider links
- `og:image` - Album artwork
- `og:url` - The card URL
- `music:musician` - Artist name
- Twitter Card tags for enhanced Twitter previews

### Card Expiration

Cards are stored in memory for 24 hours after generation. After expiration, the card URL will return a 404 error. This is designed for temporary sharing and reduces storage requirements.

### Integration Examples

#### Discord Bot
The Discord bot automatically generates and shares OpenGraph card URLs when music links are detected in messages. Discord fetches the OpenGraph metadata and displays a rich preview.

#### Direct API Usage
```bash
# Get card URL via API
curl -X POST http://localhost:10000/music/lookup/url \
  -H "Content-Type: application/json" \
  -d '{"uri": "https://open.spotify.com/track/3n3Ppam7vgaVa1iaRUc9Lp"}'

# The response includes a MediaLinkResult with provider URLs
# Use IMediaCardService to generate a card URL for sharing
```

#### Custom Integration
```csharp
// Inject IMediaCardService and IMediaLinkService
MediaLinkResult? result = await _mediaLinkService.GetInfoAsync(title, artist);
if (result != null) {
    string cardId = _mediaCardService.StoreResult(result);
    string cardUrl = $"{baseUrl}/music/card/{cardId}";
    // Share cardUrl - platforms will display OpenGraph preview
}
```

## Discord Bot Usage

Once invited to your Discord server, the bot will automatically:
1. Monitor messages for Apple Music, Spotify, and Tidal links
2. Look up the corresponding track/album on the other platforms
3. Reply with an OpenGraph card URL that Discord renders as a rich preview
4. Deletes the original message (if it only contained music links [keeping the channel clean])

The bot now uses OpenGraph cards instead of Discord-specific embeds, allowing the same cards to be shared across multiple platforms.

## Deployment

### Public Hosting

The application exposes port `10000` by default and is designed to be deployed behind a reverse proxy. Common deployment options include:

- Container platforms (Docker, Kubernetes)
- Cloud services (Azure Container Apps, AWS ECS, Google Cloud Run)
- Platform-as-a-Service (Heroku, Railway, Render)

**Note**: Public hosting location is TBD.

## Technology Stack

- **.NET 9.0** - Cross-platform framework
- **ASP.NET Core** - Web framework
- **NetCord** - Discord bot library
- **Apple MusicKit API** - Apple Music integration
- **Spotify Web API** - Spotify integration
- **Tidal API** - Tidal integration

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Author

Taylor Marvin
