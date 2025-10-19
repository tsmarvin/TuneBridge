# MediaLinkResult Caching with Bluesky PDS Storage

This document describes the caching and storage system for MediaLinkResult DTOs in TuneBridge.

## Overview

TuneBridge implements a two-tier caching system for MediaLinkResult lookups:

1. **SQLite Database**: Local cache for fast lookups and tracking input links
2. **Bluesky PDS**: Persistent storage of MediaLinkResults as Bluesky posts

## Configuration

Add the following settings to your `appsettings.json`:

```json
{
  "TuneBridge": {
    "BlueskyPdsUrl": "https://bsky.social",
    "BlueskyIdentifier": "your-handle.bsky.social",
    "BlueskyPassword": "your-app-password",
    "CacheDays": 7,
    "CacheDbPath": "medialinkscache.db"
  }
}
```

### Configuration Parameters

- **BlueskyPdsUrl**: The Bluesky PDS instance URL (default: `https://bsky.social`)
- **BlueskyIdentifier**: Your Bluesky handle or DID
- **BlueskyPassword**: Your Bluesky password or app password (recommended: use app password)
- **CacheDays**: Number of days to keep cache entries valid (default: 7)
- **CacheDbPath**: Path to the SQLite database file (default: `medialinkscache.db`)

> **Security Note**: Use a Bluesky app password instead of your main account password. Generate an app password at: Settings → App Passwords in Bluesky.

## SQLite Database Schema

The cache database consists of two main tables:

### MediaLinkCacheEntry

Stores cached MediaLinkResult records and their Bluesky PDS locations.

| Column | Type | Description |
|--------|------|-------------|
| Id | INTEGER | Primary key |
| RecordUri | TEXT | AT-URI of the Bluesky post (e.g., `at://did:plc:xxx/app.bsky.feed.post/yyy`) |
| SerializedResult | TEXT | JSON representation of the MediaLinkResult |
| CreatedAt | DATETIME | When this cache entry was created |
| LastAccessedAt | DATETIME | When this cache entry was last accessed |

**Indexes:**
- Unique index on `RecordUri`
- Index on `LastAccessedAt` for efficient expiration queries

### InputLinkEntry

Tracks input links that map to cached results. Multiple input links can point to the same cache entry.

| Column | Type | Description |
|--------|------|-------------|
| Id | INTEGER | Primary key |
| Link | TEXT | Normalized input link (without protocol) |
| MediaLinkCacheEntryId | INTEGER | Foreign key to MediaLinkCacheEntry |
| CreatedAt | DATETIME | When this link was first added |

**Indexes:**
- Unique index on `Link`
- Foreign key relationship with MediaLinkCacheEntry (cascade delete)

## Caching Behavior

### Lookup Flow

1. **Cache Check**: When a lookup is requested with input links, the system first checks the SQLite database
2. **Expiration Check**: If found, checks if the entry is within the cache window (X days from `LastAccessedAt`)
3. **Cache Hit**: If valid, returns the cached result and updates `LastAccessedAt`
4. **Cache Miss**: If not found or expired, performs a fresh lookup

### Storage Flow

1. **Lookup Execution**: Performs lookup across configured music providers
2. **Bluesky Storage**: Stores the MediaLinkResult as a Bluesky post with JSON content
3. **SQLite Storage**: Stores the Bluesky record URI and serialized result in SQLite
4. **Link Association**: Associates all input links with the cache entry

### Link Association

When a new lookup is performed with a link that resolves to an already-cached result:
- The new link is added to the `InputLinkEntry` table
- No duplicate Bluesky post is created
- Future lookups with the new link will hit the cache

## Bluesky Storage Format

MediaLinkResults are stored as Bluesky posts with the following format:

```
#TuneBridge MediaLinkResult
{
  "Results": {
    "AppleMusic": { ... },
    "Spotify": { ... }
  },
  "_inputLinks": ["music.apple.com/...", "open.spotify.com/..."]
}
```

The `#TuneBridge MediaLinkResult` marker identifies posts as TuneBridge cache entries.

## API Interfaces

### IBlueskyStorageService

```csharp
public interface IBlueskyStorageService {
    Task<string> StoreMediaLinkResultAsync(MediaLinkResult result);
    Task<MediaLinkResult?> GetMediaLinkResultAsync(string recordUri);
}
```

### IMediaLinkCacheService

```csharp
public interface IMediaLinkCacheService {
    Task<(MediaLinkResult result, string recordUri)?> TryGetCachedResultAsync(string inputLink);
    Task<string> CacheResultAsync(MediaLinkResult result, IEnumerable<string> inputLinks);
    Task AddInputLinksAsync(string recordUri, IEnumerable<string> newLinks);
}
```

### CachedMediaLinkService

A decorator for `IMediaLinkService` that transparently adds caching:
- Checks cache before performing lookups
- Stores results after successful lookups
- Updates link associations automatically

## Performance Considerations

- **Cache Hit**: ~1-5ms (SQLite query)
- **Cache Miss + Bluesky Storage**: ~500-2000ms (API calls + network)
- **Database Size**: Approximately 1-2KB per cached result

## Maintenance

### Cache Expiration

Entries older than `CacheDays` are considered expired but not automatically deleted. They remain in the database for historical tracking.

### Manual Cache Cleanup

To manually clean up old entries:

```sql
DELETE FROM MediaLinkCacheEntry 
WHERE LastAccessedAt < datetime('now', '-30 days');
```

### Database Backup

The SQLite database file can be backed up while the application is running:

```bash
sqlite3 medialinkscache.db ".backup medialinkscache.backup.db"
```

## Limitations

- Bluesky posts are public by default (cache entries are visible on your profile)
- Maximum post size is ~300KB (sufficient for typical MediaLinkResults)
- Rate limits apply to Bluesky API (authenticated: 3000/hour, 30000/day)

## Security Considerations

- Use Bluesky app passwords (not main password)
- Store credentials securely (environment variables or secret management)
- SQLite database contains serialized MediaLinkResults (treat as sensitive if results contain user data)
- No PII is stored in the cache by default
