# API Access Patterns

BridgeBeats provides three distinct access patterns for music lookup functionality:

## 1. Public Access (No Authentication)

For basic URL lookups that don't require authentication:

- **Endpoint**: `POST /lookup/web`
- **Authentication**: None required
- **Use case**: Public access to convert music URLs
- **Example**:
```bash
curl -X POST https://bridgebeats.app/lookup/web \
  -H "Content-Type: application/json" \
  -d '{"uri": "https://open.spotify.com/track/3n3Ppam7vgaVa1iaRUc9Lp"}'
```

## 2. Browser Access (Cookie + CSRF Token)

For logged-in users accessing restricted endpoints from the browser:

- **Endpoints**: 
  - `POST /lookup/browser/isrc`
  - `POST /lookup/browser/upc`
  - `POST /lookup/browser/title`
- **Authentication**: Cookie-based session auth
- **CSRF Protection**: Requires `X-XSRF-TOKEN` header
- **Use case**: Authenticated users calling from JavaScript in the browser

### How to use from JavaScript:

```javascript
// 1. Get the antiforgery token
const tokenResponse = await fetch('/account/antiforgery-token');
const { token } = await tokenResponse.json();

// 2. Make the request with the token
const response = await fetch('/lookup/browser/isrc', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    'X-XSRF-TOKEN': token
  },
  body: JSON.stringify({ isrc: 'USRC17607839' }),
  credentials: 'include' // Include cookies
});
```

## 3. External API Access (API Key)

For external applications and programmatic access:

- **Endpoints**:
  - `POST /music/lookup/urlList`
  - `POST /music/lookup/url`
  - `POST /music/lookup/isrc`
  - `POST /music/lookup/upc`
  - `POST /music/lookup/title`
- **Authentication**: API key in `X-API-Key` header
- **Use case**: External applications, scripts, integrations
- **Rate limits**: Applied per API key

### How to use:

```bash
# Get your API key by registering at /account/register
# Then use it in the X-API-Key header

curl -X POST https://bridgebeats.app/music/lookup/isrc \
  -H "Content-Type: application/json" \
  -H "X-API-Key: your-api-key-here" \
  -d '{"isrc": "USRC17607839"}'
```

## Summary

| Access Pattern | Authentication | CSRF Protection | Rate Limits | Use Case |
|---|---|---|---|---|
| Public (`/lookup/web`) | None | N/A | None | Basic URL conversion |
| Browser (`/lookup/browser/*`) | Cookie | Required | Per user | Logged-in browser users |
| API (`/music/lookup/*`) | API Key | N/A | Per API key | External applications |

## Getting an API Key

1. Register at `/account/register`
2. Save the API key from the registration response
3. Use the key in the `X-API-Key` header for all API requests
4. The key can be regenerated at `/account/regenerate-api-key`
