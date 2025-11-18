# TuneBridge Routing Architecture (Aspire AppHost)

## Overview

TuneBridge uses .NET Aspire AppHost for service orchestration. The routing is designed with a clear separation between public-facing and internal services.

## Service Architecture

```
External Traffic (Caddy)
    ↓
Web Project (Port 10000 - Public)
    ↓ (Service Discovery: https+http://apiservice)
ApiService (Internal Only)
    ↑
JetstreamMonitor (Internal Background Service)
```

## Routing Configuration

### Caddy (External Gateway)
- **Entry Point**: All external traffic enters through Caddy reverse proxy
- **Target**: `tunebridge:10000` → Web project
- **Health Check**: `/health` endpoint
- **Endpoints Exposed**:
  - `/` - Home page
  - `/account/*` - Authentication (login, register, API keys)
  - `/dashboard` - Admin dashboard (Aspire Dashboard access via forward_auth)
  - `/card/*` - OpenGraph embeddable cards
  - `/.well-known/*` - ATProto lexicon files (CORS enabled)
  - Static assets (CSS, JS, images)

### Web Project (Public-Facing)
- **Port**: 10000 (HTTP)
- **Role**: UI layer, authentication, static file serving
- **Controllers**: 
  - `HomeController` - Main landing page
  - `AccountController` - User authentication, API key management
  - `DashboardController` - Authorization checks for Aspire Dashboard
  - `OpenGraphCardController` - Embeddable music cards
- **Middleware Chain**:
  1. Exception handler (production only)
  2. HSTS (production only)
  3. HTTPS redirection
  4. Static files (.well-known with ATProto MIME types)
  5. Static files (wwwroot)
  6. Routing
  7. **HealthEndpointAuthorizationMiddleware** - IP-based health check restriction
  8. Authentication
  9. Authorization
  10. **RateLimitingMiddleware** - API rate limiting
- **API Client**: Calls ApiService via `TuneBridgeApiClient` using service discovery

### ApiService (Internal Only)
- **Port**: Dynamically assigned by Aspire
- **Role**: Business logic, music provider integration, REST API
- **Access**: Only via service discovery (`https+http://apiservice`)
- **Controllers**:
  - `MusicLookupController` - Track/album lookup across providers
    - `POST /music/lookup/urlList` - Batch URL conversion
    - `POST /music/lookup/isrc` - ISRC lookup
    - `POST /music/lookup/upc` - UPC lookup
    - `POST /music/lookup/title` - Title/artist search
- **Authentication**: None (internal service, auth handled by Web)
- **Middleware**:
  - Exception handler
  - Routing
  - Controllers

### JetstreamMonitor (Background Service)
- **Role**: Monitor Bluesky Jetstream for music links
- **Access**: Calls ApiService via service discovery to cache discovered links
- **No HTTP endpoints** - Console application with hosted service

## Service Discovery

Aspire provides automatic service discovery using the `https+http://` scheme:
- **Format**: `https+http://[service-name]`
- **Resolution**: Aspire resolves service name to internal endpoint
- **Benefits**: No hardcoded URLs, automatic failover, load balancing
- **Example**: Web calls ApiService via `https+http://apiservice`

## Health Checks

- **Web**: `/health` - Restricted to internal IPs (via HealthEndpointAuthorizationMiddleware)
- **ApiService**: `/health` - Aspire health check endpoint
- **Usage**: Caddy monitors Web health, Aspire monitors ApiService health

## Security Considerations

1. **ApiService is not exposed externally** - Only accessible via service discovery
2. **Authentication centralized in Web** - All user auth happens before API calls
3. **Rate limiting applied in Web** - Protects ApiService from abuse
4. **Health endpoint restricted** - Only internal IPs can check Web health
5. **ATProto lexicons have CORS** - Allows PDS servers to fetch lexicon definitions

## Development vs Production

### Development
- Aspire Dashboard accessible at `:18888` (via Web's dashboard authorization)
- OpenAPI/Swagger available in ApiService (development mode only)
- More verbose logging

### Production  
- Aspire Dashboard requires authentication via Web
- Swagger disabled
- Exception pages disabled (shows generic error)

## Testing Checklist

- [ ] Caddy can reach Web at :10000
- [ ] Web serves static files from wwwroot
- [ ] Web serves .well-known directory with correct MIME types
- [ ] Health endpoint (/health) responds correctly
- [ ] Health endpoint rejects external IPs (if configured)
- [ ] Web can call ApiService via TuneBridgeApiClient
- [ ] ApiService endpoints return data
- [ ] JetstreamMonitor can call ApiService
- [ ] Aspire Dashboard accessible through Web dashboard authorization
- [ ] Rate limiting works correctly
- [ ] Authentication flows work (login, register, API keys)
