# MediaLinkResult Caching with Redis and ATProto PDS Storage

This document describes the caching and storage system for MediaLinkResult DTOs in BridgeBeats.

## Overview

BridgeBeats implements a two-tier caching system for MediaLinkResult lookups:

1. **Redis**: Distributed cache for fast lookups with support for horizontal scaling
2. **ATProto PDS**: Persistent storage of MediaLinkResults as ATProto posts

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                       Lookup Request Flow                       │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  User Request                                                   │
│       │                                                         │
│       ▼                                                         │
│  ┌──────────────────────────────────────┐                       │
│  │   Request Deduplicator (Redis)      │                       │
│  │   SETNX lock on request key         │                       │
│  └─────────────┬────────────────────────┘                       │
│                │                                                │
│       ┌────────┴────────┐                                       │
│       ▼                 ▼                                       │
│   Duplicate        New Request                                  │
│   (wait)            │                                           │
│       │             ▼                                           │
│       │        ┌────────────────────────────┐                   │
│       │        │   Redis Cache Lookup       │                   │
│       │        │   (Check for RecordUri)    │                   │
│       │        └────────┬───────────────────┘                   │
│       │                 │                                       │
│       │        ┌────────┴────────┐                              │
│       │        ▼                 ▼                              │
│       │    Cache Hit         Cache Miss                         │
│       │        │                 │                              │
│       │        ▼                 ▼                              │
│       │   ┌─────────────┐   ┌────────────┐                     │
│       │   │ Fetch from  │   │  Parallel  │                     │
│       │   │   PDS       │   │  Provider  │                     │
│       │   │             │   │  Lookups   │                     │
│       │   └──────┬──────┘   └─────┬──────┘                     │
│       │          │                 │                            │
│       │          ▼                 ▼                            │
│       │   ┌──────────────────────────┐                          │
│       │   │   Check Staleness        │                          │
│       │   │   (LookedUpAt vs Now)    │                          │
│       │   └──────┬───────────────────┘                          │
│       │          │                                              │
│       │     ┌────┴─────┐                                        │
│       │     ▼          ▼                                        │
│       │  Fresh      Stale                                       │
│       │     │          │                                        │
│       │     │          ▼                                        │
│       │     │    ┌──────────────┐                               │
│       │     │    │  Queue for   │                               │
│       │     │    │  Background  │                               │
│       │     │    │  Refresh     │                               │
│       │     │    └──────────────┘                               │
│       │     │                                                   │
│       └─────┴──────────▼                                        │
│              Return Result                                      │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

## Configuration

Add the following settings to your `appsettings.json` or environment variables:

```json
{
  "BridgeBeats": {
    "ATProtoIdentifier": "your-handle.bsky.social",
    "ATProtoPassword": "your-app-password",
    "CacheDays": 7,
    "RedisConnectionString": "localhost:6379"
  }
}
```

### Configuration Parameters

- **ATProtoIdentifier**: Your ATProto handle or DID
- **ATProtoPassword**: Your ATProto password or app password (recommended: use app password)
- **CacheDays**: Number of days to keep cache entries valid (default: 7)
- **RedisConnectionString**: Redis connection string (provided by Aspire in development)

> **Security Note**: Use a ATProto app password instead of your main account password. Generate an app password at: Settings → App Passwords in ATProto.

## Redis Key Patterns

The cache uses the following Redis key patterns for efficient lookups:

| Key Pattern | Description | Example |
|-------------|-------------|---------|
| `lookup:isrc:{isrc}` | Track lookup by ISRC code | `lookup:isrc:USRC12345678` |
| `lookup:upc:{upc}` | Album lookup by UPC code | `lookup:upc:123456789012` |
| `lookup:url:{urlHash}` | Lookup by URL hash (SHA256) | `lookup:url:abc123...` |
| `lookup:card:{cardId}` | Lookup by card ID | `lookup:card:xyz789` |
| `lookup:provider:{provider}:{id}` | Lookup by provider ID | `lookup:provider:spotify:track123` |
| `lookup:metadata:{metadataHash}` | Lookup by title+artist hash | `lookup:metadata:def456...` |
| `meta:{rkey}` | Metadata for a record | `meta:track_abc123` |
| `keys:{rkey}` | Set of lookup keys for cleanup | `keys:track_abc123` |

All keys have a TTL equal to `CacheDays` and automatically expire when stale.

## Caching Behavior

### Lookup Flow

1. **Deduplication Check**: Redis SETNX used to prevent duplicate in-flight requests
2. **Cache Check**: Multiple lookup strategies tried in order of specificity:
   - By ISRC/UPC (for tracks/albums)
   - By provider ID (Spotify/Apple Music/Tidal)
   - By metadata hash (title + artist)
   - By URL hash (input link)
3. **PDS Fetch**: If RecordUri found in Redis, fetch actual data from ATProto PDS
4. **Staleness Check**: Check if record's `lookedUpAt` timestamp exceeds `CacheDays`
5. **Cache Hit**: If fresh, return immediately (with stale-while-revalidate queuing if stale)
6. **Cache Miss**: Perform fresh lookup via providers

### Storage Flow

1. **Lookup Execution**: Parallel lookup across configured music providers
2. **ATProto Storage**: Store MediaLinkResult as ATProto PDS record with deterministic rkey
3. **Redis Indexing**: Create lookup indices in Redis (no data duplication)
4. **Link Association**: Associate all input links with the RecordUri in Redis

### Stale-While-Revalidate

When a stale record is found (older than `CacheDays`):
1. The stale result is returned immediately to the user
2. A background refresh job is enqueued to update the record
3. The refresh writes a new PDS record and updates all Redis indices
4. Future requests get the refreshed data

This ensures users always get fast responses while keeping data fresh.

### Request Deduplication

To prevent thundering herd problems with concurrent identical requests:
1. Redis SETNX creates a lock with TTL (default: job timeout duration)
2. First request acquires lock and processes the lookup
3. Concurrent identical requests wait for the result via Redis Pub/Sub
4. When complete, the result is published to all waiting requests
5. All waiting requests receive the same cached result

This dramatically reduces provider API calls during traffic spikes.

## ATProto Storage Format

MediaLinkResults are stored as custom AT Protocol records using the `link.bridgebeats.lookup` lexicon.

### Lexicon Definition

The custom lexicon is defined in `wwwroot/.well-known/atproto-lexicon/link.bridgebeats.lookup` and served at `https://<your-domain>/.well-known/atproto-lexicon/link.bridgebeats.lookup`.

See [ATPROTO_LEXICON.md](ATPROTO_LEXICON.md) for the complete lexicon definition.

### Record Key Generation

Records use deterministic `rkey` generation for idempotent storage:

- **Tracks with ISRC**: `track_{base32(sha256(isrc))}`
- **Albums with UPC**: `album_{base32(sha256(upc))}`
- **Metadata-based**: `meta_{base32(sha256(title|artist))}`

This ensures identical music content always maps to the same PDS record, even if looked up via different URLs or providers.

### Record Structure

Example record structure stored on PDS:

```json
{
  "results": [
    {
      "provider": "spotify",
      "artist": "Artist Name",
      "title": "Track Title",
      "externalId": "USRC12345678",
      "url": "https://open.spotify.com/track/...",
      "artUrl": "https://i.scdn.co/image/...",
      "marketRegion": "us",
      "isAlbum": false
    }
  ],
  "lookedUpAt": "2025-01-19T23:00:00Z"
}
```

**Important Privacy Note**: Input links (the URLs users provide) are intentionally NOT stored in ATProto PDS records. They are tracked only in Redis as URL hashes for lookup purposes. This protects user privacy by not exposing potentially tracking-laden URLs on the public ATProto network.

## Performance Considerations

- **Redis Lookup**: ~1-5ms (O(1) key lookup)
- **PDS Fetch**: ~100-500ms (ATProto PDS API call)
- **Fresh Lookup**: ~500-2000ms (Music provider API calls + PDS storage)
- **Record Update**: ~100-500ms (PDS API call to update existing record)
- **Memory Usage**: ~500 bytes per cached record (Redis index keys only, no data)

### Horizontal Scaling

Redis-based caching enables horizontal scaling:
- Multiple application instances share the same Redis cache
- Request deduplication prevents duplicate API calls across instances
- No cache coordination needed (Redis handles it)
- Stale-while-revalidate minimizes provider API load

### Key Cleanup

Redis keys automatically expire via TTL:
- All lookup keys have TTL = `CacheDays`
- Stale keys are automatically removed by Redis
- Explicit cleanup on record refresh removes old keys before adding new ones
- `keys:{rkey}` set tracks all keys for a record for efficient cleanup

## API Interfaces

### IATProtoStorageService

```csharp
public interface IATProtoStorageService {
    Task<string> StoreMediaLinkResultAsync(MediaLinkResult result);
    Task<MediaLinkResult?> GetMediaLinkResultAsync(string recordUri);
}
```

### IMediaLinkCacheRepository

```csharp
public interface IMediaLinkCacheRepository {
    Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultAsync(string inputLink);
    Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByISRCAsync(string isrc);
    Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByUPCAsync(string upc);
    Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByProviderIdAsync(string providerId, SupportedProviders provider, bool isAlbum);
    Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByMetadataAsync(string title, string artist);
    Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByCardIdAsync(string cardId);
    Task<string> CacheResultAsync(MediaLinkResult result);
    Task AddInputLinksAsync(string recordUri, MediaLinkResult result);
}
```

### CachingMediaLinkService

A decorator for `IMediaLinkService` that transparently adds caching:
- Checks cache before performing lookups
- Stores results after successful lookups
- Implements stale-while-revalidate pattern
- Handles request deduplication automatically

## Maintenance

### Record Refresh

Records older than `CacheDays` are automatically queued for background refresh:
- Stale data is served immediately
- Background job performs fresh provider lookups
- PDS record is created/updated with new data
- All Redis indices are updated atomically

### Manual Cache Invalidation

To invalidate specific cache entries:

```bash
# Connect to Redis
redis-cli

# Delete all lookup keys for a specific record
SMEMBERS keys:track_abc123
# (copy the keys returned)
DEL lookup:isrc:USRC12345678 lookup:url:xyz789 ...
DEL keys:track_abc123
DEL meta:track_abc123
```

### Migration from SQLite

The system includes a `SqliteToRedisMigrator` for migrating existing SQLite cache data to Redis:

```bash
# Run the migration (via CacheBootstrap worker or manual invocation)
# All SQLite MediaLinkCacheEntry records are migrated to Redis indices
# ATProto PDS records remain unchanged (source of truth)
```

## Limitations

- Redis keys expire after `CacheDays` (stale records are refreshed on access)
- ATProto PDS is the source of truth; Redis is an index layer only
- Provider URLs may change (cache uses stable ISRC/UPC when available)
- Rate limits apply to ATProto API (authenticated: 3000/hour, 30000/day)
- Maximum record size depends on PDS configuration

## Security Considerations

- Use ATProto app passwords (not main password)
- Store credentials securely (environment variables or secret management)
- Redis should be secured (password, firewall, TLS in production)
- URL hashes in Redis prevent exposure of tracking parameters
- No PII is stored in the cache by default
