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

The cache database consists of two main tables. **Important**: The SQLite database is used only for efficient lookups to find Bluesky PDS record locations. All actual MediaLinkResult data is stored on and retrieved from the Bluesky PDS.

### MediaLinkCacheEntry

Stores Bluesky PDS record locations for efficient lookups.

| Column | Type | Description |
|--------|------|-------------|
| Id | INTEGER | Primary key |
| RecordUri | TEXT | AT-URI of the Bluesky record (e.g., `at://did:plc:xxx/media.tunebridge.lookup.result/yyy`) |
| CreatedAt | DATETIME | When this cache entry was created |
| LastLookedUpAt | DATETIME | When this record was last looked up or refreshed on PDS (used for staleness check) |

**Indexes:**
- Unique index on `RecordUri`
- Index on `LastLookedUpAt` for efficient expiration queries

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

1. **Cache Check**: When a lookup is requested with input links, the system checks the SQLite database for a matching link
2. **PDS Fetch**: If found, fetches the actual MediaLinkResult from Bluesky PDS using the stored RecordUri
3. **Staleness Check**: Checks if the record's `lookedUpAt` timestamp is older than the cache window (X days from `LastLookedUpAt` in SQLite)
4. **Cache Hit**: If fresh, returns the PDS result
5. **Cache Miss/Stale**: If not found, missing from PDS, or stale, performs a fresh lookup

### Storage Flow

1. **Lookup Execution**: Performs lookup across configured music providers
2. **Bluesky Storage**: Stores the MediaLinkResult as a Bluesky PDS record with JSON content and current timestamp
3. **SQLite Storage**: Stores only the Bluesky record URI and lookup timestamp in SQLite (no data duplication)
4. **Link Association**: Associates all input links with the cache entry in SQLite

### Record Refresh

When a record is found to be stale (older than cache window):
1. A fresh lookup is performed via the music provider APIs
2. The existing PDS record is **updated** (using PutRecord) with:
   - New provider results
   - Updated `lookedUpAt` timestamp
   - All accumulated input links (old + new)
3. The SQLite `LastLookedUpAt` is updated
4. The same RecordUri is maintained (no new record created)

### Link Association

When a new lookup is performed with a link that resolves to an already-cached result:
1. The new link is compared against existing links in the PDS record
2. If new, the PDS record is **updated** with the additional link
3. The `lookedUpAt` timestamp is refreshed on the PDS record
4. The new link is added to the SQLite `InputLinkEntry` table for future lookups
5. The SQLite `LastLookedUpAt` is updated

**Important**: There is no practical limit on the number of input links that can be associated with a record. All links that resolve to the same music content will be stored together.

## Bluesky Storage Format

MediaLinkResults are stored as custom AT Protocol records using the `media.tunebridge.lookup.result` lexicon.

### Lexicon Definition

The custom lexicon is defined in `Domain/Lexicons/media.tunebridge.lookup.result.json`:

```json
{
  "lexicon": 1,
  "id": "media.tunebridge.lookup.result",
  "defs": {
    "main": {
      "type": "record",
      "description": "Result of parsing and looking up media links across supported music streaming providers.",
      "key": "tid",
      "record": {
        "type": "object",
        "required": ["results", "lookedUpAt"],
        "properties": {
          "results": { /* array of providerResult */ },
          "inputLinks": { /* array of URIs */ },
          "lookedUpAt": { /* ISO 8601 datetime */ }
        }
      }
    },
    "providerResult": {
      "type": "object",
      "required": ["provider", "artist", "title", "url", "marketRegion"],
      "properties": {
        "provider": { /* appleMusic, spotify, or tidal */ },
        "artist": { /* string */ },
        "title": { /* string */ },
        "externalId": { /* ISRC/UPC */ },
        "url": { /* URI */ },
        "artUrl": { /* URI */ },
        "marketRegion": { /* ISO 3166-1 alpha-2 */ },
        "isAlbum": { /* boolean */ }
      }
    }
  }
}
```

### Record Structure

Records are stored using the AT Protocol's `com.atproto.repo.createRecord` endpoint with:
- **Collection**: `media.tunebridge.lookup.result`
- **Record Key**: Auto-generated TID (timestamp identifier)
- **Record Value**: MediaLinkResultRecord (custom C# record class)

Example record structure:

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
    },
    {
      "provider": "appleMusic",
      "artist": "Artist Name",
      "title": "Track Title",
      "externalId": "USRC12345678",
      "url": "https://music.apple.com/us/album/...",
      "artUrl": "https://is1-ssl.mzstatic.com/image/...",
      "marketRegion": "us",
      "isAlbum": false
    }
  ],
  "inputLinks": [
    "https://open.spotify.com/track/..."
  ],
  "lookedUpAt": "2025-10-19T23:00:00Z"
}
```

### Benefits of Custom Lexicon

- **Structured Data**: Records are properly typed and validated against the lexicon schema
- **Queryable**: Can be indexed and queried efficiently by Bluesky infrastructure
- **Versioned**: Lexicon versioning allows for future schema evolution
- **Interoperable**: Other AT Protocol clients can understand and display the data
- **Efficient**: More compact than storing JSON in post text

## API Interfaces

### IBlueskyStorageService

```csharp
public interface IBlueskyStorageService {
    Task<string> StoreMediaLinkResultAsync(MediaLinkResult result);
    Task<MediaLinkResult?> GetMediaLinkResultAsync(string recordUri);
    Task<bool> UpdateMediaLinkResultAsync(string recordUri, MediaLinkResult result);
}
```

**Methods:**
- `StoreMediaLinkResultAsync`: Creates a new record on Bluesky PDS
- `GetMediaLinkResultAsync`: Retrieves a record from Bluesky PDS by its AT-URI
- `UpdateMediaLinkResultAsync`: Updates an existing record on Bluesky PDS (used for refreshing stale records and adding new links)

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

- **Cache Lookup**: ~1-5ms (SQLite query to find RecordUri)
- **PDS Fetch**: ~100-500ms (Bluesky PDS API call to retrieve record)
- **Fresh Lookup + PDS Storage**: ~500-2000ms (Music provider API calls + PDS record creation/update)
- **Record Update**: ~100-500ms (PDS API call to update existing record)
- **Database Size**: Approximately 100-200 bytes per cached link + record URI (no result data stored in SQLite)

### Key Points

- SQLite is used only for efficient link-to-RecordUri lookups
- All MediaLinkResult data is stored on and retrieved from Bluesky PDS
- Stale records are updated on PDS, not replaced
- No data duplication between SQLite and PDS

## Maintenance

### Record Refresh

Records older than `CacheDays` are automatically refreshed on next access:
- A fresh lookup is performed
- The PDS record is updated (not replaced) with new data and timestamp
- The SQLite `LastLookedUpAt` is updated

### Manual Cache Cleanup

To remove orphaned SQLite entries (where PDS records have been manually deleted):

```sql
-- Find cache entries where PDS record no longer exists
-- (Manual verification required via PDS queries)

-- Remove a specific cache entry
DELETE FROM MediaLinkCacheEntry WHERE RecordUri = 'at://...';

-- Remove very old cache entries (30+ days)
DELETE FROM MediaLinkCacheEntry WHERE LastLookedUpAt < datetime('now', '-30 days');
```

### Database Backup

The SQLite database file can be backed up while the application is running:

```bash
sqlite3 medialinkscache.db ".backup medialinkscache.backup.db"
```

**Note**: Backing up only the SQLite database is insufficient for full data recovery. The actual MediaLinkResult data is stored on Bluesky PDS.

## Limitations

- Custom lexicon records are stored in the user's AT Protocol repository
- Records are not displayed as standard posts in Bluesky feeds
- Records are publicly accessible via AT Protocol if you know the record URI
- Rate limits apply to Bluesky API (authenticated: 3000/hour, 30000/day)
- Maximum record size depends on PDS configuration (typically sufficient for MediaLinkResults)

## Security Considerations

- Use Bluesky app passwords (not main password)
- Store credentials securely (environment variables or secret management)
- SQLite database contains serialized MediaLinkResults (treat as sensitive if results contain user data)
- No PII is stored in the cache by default
