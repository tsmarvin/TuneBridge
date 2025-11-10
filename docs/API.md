# API Reference

TuneBridge provides a RESTful API for music link conversion and lookup. This guide covers authentication, endpoints, and rate limiting.

## Authentication

Most API endpoints require authentication using an API key. To get an API key, you need to create a user account.

### Creating an Account

```http
POST /account/register
Content-Type: application/json

{
  "email": "user@example.com",
  "password": "SecurePassword123"
}
```

**Response:**
```json
{
  "userId": "...",
  "apiKey": "YOUR_API_KEY_HERE",
  "message": "Registration successful. Save your API key - it will not be shown again."
}
```

⚠️ **Important**: Save your API key immediately. It cannot be retrieved later (only regenerated).

### Using Your API Key

Include your API key in the `X-API-Key` header for all protected endpoints:

```http
X-API-Key: YOUR_API_KEY_HERE
```

### Logging In

If you need to retrieve your API key again, you can log in:

```http
POST /account/login
Content-Type: application/json

{
  "email": "user@example.com",
  "password": "SecurePassword123"
}
```

### Regenerating Your API Key

```http
POST /account/regenerate-api-key
X-API-Key: YOUR_CURRENT_API_KEY
```

## Rate Limiting

To ensure fair usage, the API implements rate limiting on protected endpoints.

### Limits

- **Rate**: 20 requests per hour per authenticated user
- **Applies to**: All protected endpoints (ISRC, UPC, title, urlList)
- **Exempt**: Public `/music/lookup/url` endpoint (streaming)

### Rate Limit Response

When you exceed the rate limit, you'll receive an HTTP 429 response:

```http
HTTP/1.1 429 Too Many Requests
Retry-After: 2700
Content-Type: application/json

{
  "error": "Rate limit exceeded",
  "message": "Maximum 20 requests per hour allowed. Please try again in 45 minutes.",
  "retryAfter": 2700
}
```

The `Retry-After` header and `retryAfter` field indicate how many seconds until your limit resets.

## Music Lookup Endpoints

### Lookup by URL (Streaming) - PUBLIC

Convert a music link without authentication. Returns results as a stream.

```http
POST /music/lookup/url
Content-Type: application/json

{
  "uri": "https://music.apple.com/us/album/..."
}
```

**Response:** Server-Sent Events stream with matching results

**Features:**
- ✅ No authentication required
- ✅ No rate limiting
- ✅ Streaming response for real-time results

### Lookup by URL (List) - PROTECTED

Convert a music link and get all results as a single response.

```http
POST /music/lookup/urlList
Content-Type: application/json
X-API-Key: YOUR_API_KEY

{
  "uri": "https://music.apple.com/us/album/..."
}
```

**Response:**
```json
{
  "results": {
    "spotify": {
      "artist": "Artist Name",
      "title": "Track Title",
      "externalId": "USRC12345678",
      "url": "https://open.spotify.com/track/...",
      "artUrl": "https://i.scdn.co/image/...",
      "marketRegion": "us",
      "isAlbum": false
    },
    "appleMusic": {
      "artist": "Artist Name",
      "title": "Track Title",
      "externalId": "USRC12345678",
      "url": "https://music.apple.com/us/album/...",
      "artUrl": "https://is1-ssl.mzstatic.com/image/...",
      "marketRegion": "us",
      "isAlbum": false
    }
  }
}
```

### Lookup by ISRC - PROTECTED

Look up a track using its ISRC (International Standard Recording Code).

```http
POST /music/lookup/isrc
Content-Type: application/json
X-API-Key: YOUR_API_KEY

{
  "isrc": "USVI20000123"
}
```

**ISRC Format:**
- 12 characters (hyphens optional)
- Example: `USVI20000123` or `US-VI2-00-00123`
- Case-insensitive

### Lookup by UPC - PROTECTED

Look up an album using its UPC (Universal Product Code).

```http
POST /music/lookup/upc
Content-Type: application/json
X-API-Key: YOUR_API_KEY

{
  "upc": "123456789012"
}
```

**UPC Format:**
- 12-13 digits
- Leading zeros should be preserved
- Example: `012345678901`

### Lookup by Title/Artist - PROTECTED

Search for a track or album by title and artist name.

```http
POST /music/lookup/title
Content-Type: application/json
X-API-Key: YOUR_API_KEY

{
  "title": "Song Name",
  "artist": "Artist Name"
}
```

**Tips for best results:**
- Use the primary/main artist name
- Avoid extra descriptors in the title (like "- Radio Edit")
- Try both the album title and track title if searching for albums

## Response Format

All lookup endpoints return a `MediaLinkResult` object with the following structure:

### MediaLinkResult

```typescript
{
  // Dictionary of results keyed by provider name
  results: {
    [provider: string]: {
      artist: string,      // Primary artist name
      title: string,       // Track or album title
      externalId?: string, // ISRC (tracks) or UPC (albums)
      url: string,         // Platform link
      artUrl?: string,     // Artwork image URL
      marketRegion: string,// ISO 3166-1 alpha-2 country code
      isAlbum?: boolean    // true for albums, false for tracks
    }
  },

  // Optional user messages (warnings, info)
  messages?: string[]
}
```

### Supported Providers

- `spotify` - Spotify
- `appleMusic` - Apple Music
- `tidal` - Tidal

## Error Responses

### 400 Bad Request

Invalid request format or parameters.

```json
{
  "error": "Invalid request",
  "message": "ISRC must be 12 characters"
}
```

### 401 Unauthorized

Missing or invalid API key.

```json
{
  "error": "Unauthorized",
  "message": "Valid API key required"
}
```

### 404 Not Found

Content not found on any configured platform.

```json
{
  "results": {},
  "messages": ["No matches found"]
}
```

### 429 Too Many Requests

Rate limit exceeded.

```json
{
  "error": "Rate limit exceeded",
  "message": "Maximum 20 requests per hour allowed. Please try again in 30 minutes.",
  "retryAfter": 1800
}
```

### 500 Internal Server Error

Server-side error.

```json
{
  "error": "Internal server error",
  "message": "An unexpected error occurred"
}
```

## OpenGraph Card Endpoints

TuneBridge generates shareable OpenGraph cards for music links.

### View Card

```http
GET /card/{id}
```

Returns an HTML page with OpenGraph metadata and a responsive UI displaying:
- Track/album title and artist
- Cover artwork
- Links to all available streaming platforms
- Mobile-friendly responsive design

Cards automatically expire after 24 hours.

### OpenGraph Metadata

Cards include standard OpenGraph tags:
- `og:type`: `music.song` or `music.album`
- `og:title`: Track or album title
- `og:description`: Artist and available platforms
- `og:image`: Album/track artwork
- `music:musician`: Artist name

This ensures proper rendering on platforms like Discord, Slack, Twitter, and Facebook.

## Code Examples

### Python

```python
import requests

API_KEY = "your_api_key_here"
BASE_URL = "https://tunebridge.example.com"

# Lookup by URL
response = requests.post(
    f"{BASE_URL}/music/lookup/urlList",
    headers={"X-API-Key": API_KEY},
    json={"uri": "https://open.spotify.com/track/..."}
)

if response.status_code == 200:
    data = response.json()
    for provider, result in data["results"].items():
        print(f"{provider}: {result['url']}")
```

### JavaScript/Node.js

```javascript
const axios = require('axios');

const API_KEY = 'your_api_key_here';
const BASE_URL = 'https://tunebridge.example.com';

async function lookup(uri) {
  try {
    const response = await axios.post(
      `${BASE_URL}/music/lookup/urlList`,
      { uri },
      { headers: { 'X-API-Key': API_KEY } }
    );

    return response.data.results;
  } catch (error) {
    if (error.response?.status === 429) {
      console.log(`Rate limited. Retry after ${error.response.headers['retry-after']} seconds`);
    }
    throw error;
  }
}
```

### cURL

```bash
curl -X POST https://tunebridge.example.com/music/lookup/urlList \
  -H "Content-Type: application/json" \
  -H "X-API-Key: your_api_key_here" \
  -d '{"uri": "https://open.spotify.com/track/..."}'
```

## Best Practices

1. **Cache results** - Store lookup results locally to avoid redundant API calls
2. **Handle rate limits gracefully** - Respect the `Retry-After` header
3. **Use ISRC/UPC when available** - More reliable than title/artist searches
4. **Validate user input** - Check URL formats before sending requests
5. **Handle errors** - Implement proper error handling for all response codes

## Swagger Documentation

When running TuneBridge, interactive API documentation is available at:

```
http://localhost:10000/swagger
```

The Swagger UI allows you to:
- Browse all available endpoints
- Test API calls directly from your browser
- View request/response schemas
- Download the OpenAPI specification
