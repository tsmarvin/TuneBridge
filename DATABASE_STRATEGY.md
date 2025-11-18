# Database Strategy for TuneBridge Aspire AppHost Architecture

## Current State

TuneBridge currently uses **three separate SQLite databases**, each managed by its own DbContext:

### 1. MediaLinkCacheDbContext (Core/ApiService)
**Location**: `/app/data/tunebridge.db` (configurable via `LinkCacheConnectionString`)

**Purpose**: Cache MediaLinkResults on ATProto PDS for social sharing

**Tables**:
- `MediaLinkCacheEntries` - Stores cached MediaLinkResults
  - `Rkey` (PK) - ATProto record key
  - `RecordUri` - Full AT-URI to the record
  - `CreatedAt` - When cached
  - `LastLookedUpAt` - Last access time
  
- `MediaLookupEntries` - Maps lookups to cache entries
  - `Id` (PK) - Auto-increment
  - `LookupValue` - URL, ISRC, UPC, or metadata
  - `LookupType` - Type of lookup (Url, Isrc, Upc, Metadata)
  - `IsAlbum` - Track vs album flag
  - `MediaLinkCacheEntryRkey` (FK) - Links to cache entry
  - `CreatedAt` - When created
  - Unique index on `(LookupValue, LookupType, IsAlbum)`

### 2. IdentityDbContext (Web)
**Location**: `/app/data/tunebridge.db` (configurable via `IdentityConnectionString`)

**Purpose**: ASP.NET Identity for authentication and API key management

**Tables** (standard ASP.NET Identity schema):
- `AspNetUsers` - User accounts
  - `ApiKeyHash` - Hashed API key (unique index)
- `AspNetRoles` - Roles (e.g., "AspireDashboardAccess")
- `AspNetUserRoles` - User-role mapping
- `AspNetUserClaims` - User claims
- `AspNetUserLogins` - External login providers
- `AspNetUserTokens` - Authentication tokens
- `AspNetRoleClaims` - Role-based claims

### 3. JetstreamMonitorContext (JetstreamMonitor)
**Location**: `/app/data/jetstream-monitor.db` (configurable via `DBConnectionString`)

**Purpose**: Track music links discovered from Bluesky Jetstream

**Tables**:
- `DetectedMusicLinks` - Music links found in posts
  - `Id` (PK) - Auto-increment
  - `Url` - The music link (unique index)
  - `Provider` - Provider name (index)
  - `PostUri` - AT-URI of the post
  - `FirstDetectedAt` - First seen timestamp (index)
  - `LastSeenAt` - Last seen timestamp
  - `SeenCount` - Frequency counter

## Database Strategy Options

### Option A: Keep Separate Databases (RECOMMENDED)

**Rationale:**
- **Clear separation of concerns** - Each project owns its data
- **Independent scaling** - Databases can be moved/scaled separately
- **Easier deployment** - No cross-project schema conflicts
- **Simpler rollback** - Changes don't affect other domains
- **Already working** - Current configuration

**Implementation:**
1. Keep three separate SQLite files
2. Add EF Core migrations to each DbContext
3. Each project manages its own database initialization
4. Connection strings configured independently in `entrypoint.sh`

**Default Configuration:**
```bash
LINK_CACHE_CONNECTION_STRING="Data Source=/app/data/tunebridge.db"
IDENTITY_CONNECTION_STRING="Data Source=/app/data/identity.db"
JETSTREAM_DB_CONNECTION_STRING="Data Source=/app/data/jetstream-monitor.db"
```

**Pros:**
- ✅ No cross-domain transactions needed
- ✅ Each service independently deployable
- ✅ Clearer bounded contexts
- ✅ Easier to understand and maintain
- ✅ Can use different DB engines later (SQLite → PostgreSQL for one)

**Cons:**
- ❌ Cannot join across databases
- ❌ Three separate backup/restore operations
- ❌ Slightly more disk space

### Option B: Single Shared Database

**Rationale:**
- **Atomic transactions** - Cross-domain consistency
- **Easier queries** - Can join across tables
- **Single backup** - One file to manage
- **Less configuration** - One connection string

**Implementation:**
1. Create `TuneBridge.Infrastructure` project
2. Merge all DbContexts into one `TuneBridgeDbContext`
3. Use table prefixes to namespace: `Cache_`, `Identity_`, `Jetstream_`
4. Shared migrations
5. Single connection string

**Pros:**
- ✅ One backup file
- ✅ Cross-domain queries possible
- ✅ Atomic multi-table operations

**Cons:**
- ❌ Tight coupling between projects
- ❌ Schema conflicts harder to resolve
- ❌ Can't scale databases independently
- ❌ Migrations affect all projects
- ❌ Violates bounded context principle

## Recommendation: Option A (Separate Databases)

**Why:**
1. **Microservices alignment** - Aspire AppHost encourages service independence
2. **Current architecture** - Already separated into Web/Core/JetstreamMonitor
3. **No cross-domain queries needed** - Each database serves its own purpose
4. **Future-proof** - Easier to migrate to different DB engines or hosting

## Implementation Plan

### Step 1: Add EF Core Migrations

#### Core (MediaLinkCacheDbContext)
```bash
cd src/TuneBridge.Core
dotnet ef migrations add InitialCreate --context MediaLinkCacheDbContext --output-dir Infrastructure/Migrations
```

#### Web (IdentityDbContext)  
```bash
cd src/TuneBridge.Web
dotnet ef migrations add InitialCreate --context IdentityDbContext --output-dir Identity/Migrations
```

#### JetstreamMonitor (JetstreamMonitorContext)
```bash
cd src/TuneBridge.JetstreamMonitor
dotnet ef migrations add InitialCreate --context JetstreamMonitorContext --output-dir Infrastructure/Migrations
```

### Step 2: Update Connection Strings

Edit `entrypoint.sh`:
```bash
# Separate database files for clear separation of concerns
LINK_CACHE_CONNECTION_STRING="${LINK_CACHE_CONNECTION_STRING:-Data Source=/app/data/tunebridge-cache.db}"
IDENTITY_CONNECTION_STRING="${IDENTITY_CONNECTION_STRING:-Data Source=/app/data/tunebridge-identity.db}"
JETSTREAM_DB_CONNECTION_STRING="${JETSTREAM_DB_CONNECTION_STRING:-Data Source=/app/data/jetstream-monitor.db}"
```

### Step 3: Update Database Initialization

Each project should call `Database.Migrate()` on startup:

**Core** (in ApiService/Program.cs or Core StartupExtensions):
```csharp
using var scope = app.Services.CreateScope();
var context = scope.ServiceProvider.GetRequiredService<MediaLinkCacheDbContext>();
await context.Database.MigrateAsync();
```

**Web** (already done in StartupExtensions.InitializeDatabaseAsync):
```csharp
await context.Database.MigrateAsync();
```

**JetstreamMonitor** (in Program.cs):
```csharp
using var scope = host.Services.CreateScope();
var context = scope.ServiceProvider.GetRequiredService<JetstreamMonitorContext>();
await context.Database.MigrateAsync();
```

### Step 4: Add EF Design-Time Tools

Each project needs:
```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.0">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```

### Step 5: Test Migrations

1. Run each project independently
2. Verify databases are created
3. Verify tables have correct schema
4. Test CRUD operations
5. Verify indexes exist

## Data Relationships

Despite being in separate databases, the data has logical relationships:

```
Identity.AspNetUsers (Web)
    │
    └─ (logical, no FK) creates API requests
           │
           └─> Cache.MediaLookupEntries (Core)
                   │
                   └─> Cache.MediaLinkCacheEntries (Core)

Jetstream.DetectedMusicLinks (JetstreamMonitor)
    │
    └─ (via HTTP call) triggers cache creation
           │
           └─> Cache.MediaLookupEntries (Core)
```

These are application-level relationships, not database-level foreign keys.

## Backup and Restore

With separate databases:

**Backup:**
```bash
tar -czf tunebridge-backup-$(date +%Y%m%d).tar.gz \
  /app/data/tunebridge-cache.db \
  /app/data/tunebridge-identity.db \
  /app/data/jetstream-monitor.db
```

**Restore:**
```bash
tar -xzf tunebridge-backup-YYYYMMDD.tar.gz -C /app/data/
```

## Future Considerations

1. **PostgreSQL Migration** - Consider migrating to PostgreSQL for production
2. **Read Replicas** - Separate read/write connections for scaling
3. **Database per Service** - In Kubernetes, each service could have its own DB pod
4. **Caching Layer** - Add Redis for hot data (user sessions, frequent lookups)

## Decision Record

**Date**: 2025-11-18  
**Decision**: Use separate SQLite databases (Option A)  
**Rationale**: Better separation of concerns, aligns with microservices architecture, easier to maintain and scale independently
