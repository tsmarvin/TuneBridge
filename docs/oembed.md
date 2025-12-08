# oEmbed Support

BridgeBeats supports the [oEmbed specification](https://oembed.com/) for easy embedding of music lookup result cards on third-party platforms.

## Overview

The oEmbed implementation allows you to embed BridgeBeats music cards as rich HTML iframes on your website or application by simply providing a card URL.

## Usage

### Basic oEmbed Request

```http
GET https://bridgebeats.link/oembed?url=https://bridgebeats.link/card/{cardId}
```

**Query Parameters:**
- `url` (required): The URL of the BridgeBeats card to embed
- `maxwidth` (optional): Maximum width in pixels for the embed (default: 550, range: 200-1000)
- `maxheight` (optional): Maximum height in pixels for the embed (default: 250, range: 150-600)
- `format` (optional): Response format (only "json" is supported)

### Example Request

```bash
curl "https://bridgebeats.link/oembed?url=https://bridgebeats.link/card/abc123&maxwidth=400&maxheight=300"
```

### Example Response

```json
{
  "version": "1.0",
  "type": "rich",
  "title": "Mr. Brightside",
  "author_name": "The Killers",
  "provider_name": "BridgeBeats",
  "provider_url": "https://bridgebeats.link",
  "width": 400,
  "height": 300,
  "html": "<iframe src=\"https://bridgebeats.link/card/abc123/embed\" width=\"400\" height=\"300\" frameborder=\"0\" allowtransparency=\"true\" title=\"Mr. Brightside\"></iframe>",
  "thumbnail_url": "https://i.scdn.co/image/...",
  "thumbnail_width": 300,
  "thumbnail_height": 300,
  "cache_age": 518400
}
```

## Auto-Discovery

BridgeBeats card pages include an oEmbed discovery link in the HTML `<head>`, allowing oEmbed-aware consumers to automatically discover the embed endpoint:

```html
<link rel="alternate" 
      type="application/json+oembed" 
      href="https://bridgebeats.link/oembed?url=..." 
      title="Song/Album Title" />
```

Many platforms (WordPress, Medium, etc.) will automatically detect and embed cards when you paste a card URL.

## Embedding the Card

To embed a card on your website, use the HTML returned in the `html` field of the oEmbed response:

```html
<iframe 
  src="https://bridgebeats.link/card/abc123/embed" 
  width="550" 
  height="250" 
  frameborder="0" 
  allowtransparency="true" 
  title="Song Title">
</iframe>
```

## Response Fields

| Field | Type | Description |
|-------|------|-------------|
| `version` | string | Always "1.0" (oEmbed version) |
| `type` | string | Always "rich" (oEmbed type) |
| `title` | string | Track or album title |
| `author_name` | string | Artist name |
| `provider_name` | string | Always "BridgeBeats" |
| `provider_url` | string | BridgeBeats base URL |
| `width` | integer | Width of the embed in pixels |
| `height` | integer | Height of the embed in pixels |
| `html` | string | HTML iframe code for embedding |
| `thumbnail_url` | string | URL to album/track artwork |
| `thumbnail_width` | integer | Thumbnail width (300px) |
| `thumbnail_height` | integer | Thumbnail height (300px) |
| `cache_age` | integer | Suggested cache lifetime in seconds (6 days) |

## Error Responses

### 400 Bad Request
Returned when:
- URL parameter is missing
- URL format is invalid (not a card URL)
- Format parameter is not "json"

### 404 Not Found
Returned when:
- Card ID doesn't exist
- Card has expired (cards expire after 6 days)

## Security Considerations

- All content in the generated HTML is properly escaped to prevent XSS attacks
- Embed dimensions are validated and clamped to safe ranges
- Cards automatically expire after 6 days
- The endpoint is read-only and cannot modify existing data

## Integration Examples

### WordPress

WordPress automatically supports oEmbed. Simply paste a BridgeBeats card URL into a post:

```
https://bridgebeats.link/card/abc123
```

### Custom HTML

```html
<!DOCTYPE html>
<html>
<head>
  <title>My Music Page</title>
</head>
<body>
  <h1>Check out this song!</h1>
  
  <!-- Embed the card -->
  <iframe 
    src="https://bridgebeats.link/card/abc123/embed" 
    width="550" 
    height="250" 
    frameborder="0" 
    allowtransparency="true" 
    title="Song Title"
    style="max-width: 100%;">
  </iframe>
</body>
</html>
```

### JavaScript (Dynamic)

```javascript
async function embedBridgeBeatsCard(cardUrl, container) {
  const oembedUrl = `https://bridgebeats.link/oembed?url=${encodeURIComponent(cardUrl)}`;
  
  try {
    const response = await fetch(oembedUrl);
    const data = await response.json();
    
    // Insert the HTML into your page
    container.innerHTML = data.html;
  } catch (error) {
    console.error('Failed to embed card:', error);
  }
}

// Usage
const container = document.getElementById('music-embed');
embedBridgeBeatsCard('https://bridgebeats.link/card/abc123', container);
```

## Limitations

- Only JSON format is supported (XML format is not available)
- Cards expire after 6 days
- Maximum embed dimensions: 1000x600 pixels
- Minimum embed dimensions: 200x150 pixels

## Support

For issues or questions about oEmbed support, please file an issue on the [BridgeBeats GitHub repository](https://github.com/tsmarvin/BridgeBeats).
