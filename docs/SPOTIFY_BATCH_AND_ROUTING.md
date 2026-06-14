# Spotify batch lookups and interactive routing

This document explains how BridgeBeats batches Spotify ID lookups, how it keeps
interactive requests off the batch path, when a batch flushes, and how the saga
time-to-live is reconciled with the flush horizon. It is a design explanation,
not a configuration reference; for the settings themselves see
[Configuration Guide](CONFIGURATION.md).

The behavior described here is what the code does on the current branch. File
and line references point to the source so the document can be re-verified when
the code changes.

## Why batching exists

Spotify's web API offers per-entity endpoints (`GET /tracks/{id}`,
`GET /albums/{id}`) and bulk endpoints that take up to 50 track IDs or 20 album
IDs in one call (`GET /tracks?ids=`, `GET /albums?ids=`). High-volume,
non-interactive traffic — the AT Protocol firehose watcher and the cache refresh
job — produces large numbers of ID lookups that do not need a low-latency
answer. Aggregating those into bulk calls collapses thousands of single requests
into a much smaller number of batch requests.

Interactive traffic has the opposite need: a user waiting on a Discord reply or
a web lookup wants an answer in seconds, not whenever a batch happens to fill.
The system therefore routes the two kinds of traffic down different paths.

## The lookup types that drive routing

A queued lookup carries a `LookupRequestType`. Three values matter here:

- `SongIdLookup` — a request to resolve a Spotify track by its ID.
- `AlbumIdLookup` — a request to resolve a Spotify album by its ID.
- `UriLookup` — a request to resolve a link by URL.

The AT Protocol firehose watcher classifies each incoming link. For a Spotify
track or album URL it extracts the Spotify ID and emits `SongIdLookup` or
`AlbumIdLookup` with the ID as the lookup value
(`JetStreamWatcherService.IdentifyProviderAsync`,
`src/BridgeBeats.Worker.JetStreamWatcher/JetStreamWatcherService.cs:345-359`).
Spotify short links (`spotify.link`) stay `UriLookup` because the ID is not yet
known (`:340-343`). Spotify prerelease links also stay `UriLookup` — the batch
API does not accept them. Artist and playlist links are not recognized by the
parser and are dropped.

Every producer that derives a lookup key — and therefore a saga ID — uses
`LookupKeyBuilder` so that keys for the same entity are byte-for-byte identical
across components
(`src/BridgeBeats.Core/Infrastructure/Utilities/LookupKeyBuilder.cs`). Typed ID
lookups use the shape `{lookupType}:{provider}:{normalizedId}` (`TypedKey`,
`:28-30`); URL lookups use `UriLookup:{hash}` (`UrlKey`, `:39-41`). Identical
keys are what let a firehose-origin lookup and an interactive lookup for the same
track share one saga.

## Routing: interactive versus bulk

Routing happens in `SpotifyBulkQueueDecorator`, which wraps the Spotify request
queue (`src/BridgeBeats.Core/Infrastructure/Queue/SpotifyBulkQueueDecorator.cs`).
The decorator inspects each enqueue:

- A `SongIdLookup` or `AlbumIdLookup` enqueued at a **non-interactive** priority
  is written directly to the type-specific bulk stream —
  `queue:spotify:bulk:track-id` for tracks, `queue:spotify:bulk:album-id` for
  albums — bypassing the generic priority queue (`EnqueueAsync`, `:60-89`).
- The same lookup type enqueued at `QueuePriority.Interactive` is **not**
  intercepted. It passes through to the inner queue and is served on the
  interactive lane as a single-item lookup (`:91-93`). This preserves the
  interactive latency budget; an interactive caller never waits on a batch.
- Every other lookup type is forwarded to the inner queue unchanged, regardless
  of priority.

The discriminator is the `priority` argument, checked as
`priority != QueuePriority.Interactive` (`:65-66`). Routing is keyed on priority,
not on the producer's identity. The firehose watcher enqueues at
`QueuePriority.Bulk` (`JetStreamWatcherService.cs:294,304`), so its Spotify
track and album lookups land in the bulk streams.

The stream names are defined once as constants (`BulkTrackIdStream`,
`BulkAlbumIdStream`, `src/BridgeBeats.Contracts/Constants/SpotifyConstants.cs:67,73`).
Sharing the constant across the producer (the decorator) and the consumer (the
bulk processor) prevents the silent failure where the two disagree on a stream
name and no message is ever delivered.

### Invariant: interactive lookups never ride the bulk stream

Because routing is by priority and not by call site, the system depends on no
interactive caller enqueuing a Spotify `SongIdLookup` or `AlbumIdLookup` at a
non-interactive priority. Today no interactive path does so. The decorator's
pass-through for `QueuePriority.Interactive` is the explicit guard: even a
typed ID lookup, if it arrives interactive, stays on the interactive lane.

## Flushing a batch: size or age

The bulk processor polls the type-specific streams and decides when to flush
(`SpotifyBulkProcessorService`,
`src/BridgeBeats.Worker.Spotify/SpotifyBulkProcessorService.cs`). The decision is
a pure predicate, `ShouldFlush` (`:131-136`):

```
flush when count >= threshold
   OR  (count > 0 AND oldestAge >= linger)
```

Two triggers, evaluated as an OR:

- **Size (primary).** A track batch flushes as soon as the stream holds 50
  entries; an album batch at 20 (`MaxTracksPerBatchLookup` = 50,
  `MaxAlbumsPerBatchLookup` = 20, `SpotifyConstants.cs:16,25`). When the count
  meets the threshold the age is not even read (`ShouldFlush` returns at
  `:132-134`).
- **Age (backstop).** A stream that never fills still drains. Once the oldest
  pending entry reaches the linger age, a below-threshold stream flushes
  (`:135`). The age is read only when the count is below the threshold
  (`ShouldProcessBulkTracksAsync:172-178`, `ShouldProcessBulkAlbumsAsync:212-218`).

The linger default is 24 hours (`SpotifyBatchSettings.DefaultLingerMs` =
`86_400_000` ms, `src/BridgeBeats.Worker.Spotify/SpotifyBatchSettings.cs:26`).
The age trigger is a staleness valve, not a latency bound: a low-volume,
non-interactive stream may hold an entry for up to a day before the backstop
fires, which is acceptable because nothing interactive waits on these streams.
Operators can shorten the backstop per environment with
`BridgeBeats:Spotify:Batch:LingerMs`; values at or below zero fall back to the
24-hour default (`SpotifyBatchSettings.cs:50-53`).

The processor polls every 500 ms (`s_checkInterval`,
`SpotifyBulkProcessorService.cs:48`). Each cycle reads the stream length and,
only when the stream is non-empty and below threshold, the oldest entry's
timestamp. The age is computed from the `enqueuedAt` field written when the
entry was added (`SpotifyBulkQueueDecorator.cs:77`), so it measures true
wall-clock age and survives a worker restart.

A request-failure cooldown sits in front of the flush check. After a bulk call
returns an empty result — the convention for a network or auth failure — the
processor suppresses further flushes for that stream with an exponential backoff
(5 s, doubling, capped at 60 s) so a transient Spotify outage does not burn
through retry attempts (`ArmRequestFailureCooldown:550-558`,
`ComputeCooldownSeconds:570-573`). Track and album cooldowns are independent.

## Saga time-to-live reconciliation

A firehose-origin bulk lookup is fire-and-forget: it precomputes a saga ID and
enqueues without creating the saga. The saga is materialized later, when the
bulk processor handles the entry (`ProcessBulkResultAsync` calls
`GetOrCreateAsync`, `SpotifyBulkProcessorService.cs:392-400`). With a 24-hour
age backstop, an entry can sit in the bulk stream for up to a day before that
happens.

This collides with saga retention. Every saga key and provider-state key is
written with a TTL of `QueueSettings.JobExpirationMinutes` minutes
(`RedisSagaStateManager.cs:70,221,251` and following). If a saga were created
before a bulk entry was enqueued — for example, an interactive lookup that
shares a deduplicated key with a firehose lookup — and the TTL were shorter than
the flush horizon, the saga and its accumulated cross-provider state would
expire before the batch wrote its result. Cross-provider assembly would silently
degrade to Spotify-only for that record.

The reconciliation is to set the saga TTL above the longest possible bulk wait.
`JobExpirationMinutes` defaults to 2880 minutes (48 hours,
`src/BridgeBeats.Contracts/Records/QueueSettings.cs:27`) — twice the 24-hour
linger backstop, leaving visible margin. A saga therefore outlives any bulk
entry waiting on the age backstop. The trade-off is Redis retention: every
saga, not only the bulk-origin ones, lives for 48 hours. That cost was accepted
in favor of the simpler invariant "a saga always outlives its slowest flush."

The constraint is one-directional and must hold if either value is retuned:
`JobExpirationMinutes` (in minutes) must stay greater than `LingerMs` (in
milliseconds) converted to minutes. At the defaults, 2880 min > 1440 min.

## How a result reaches its saga

When a batch flushes, the processor calls the bulk API, then maps each returned
result back to the queued message(s) that asked for that ID
(`ProcessBulkTrackLookupsAsync:230-300`, `ProcessBulkAlbumLookupsAsync:305-370`).
For each message it ensures the saga exists, writes the Spotify provider state
(found or not-found), publishes completion, and acknowledges the message
(`ProcessBulkResultAsync:380-431`). Because the saga ID is deterministic from the
lookup key, a bulk result lands on the same saga an interactive caller would
have created, so a user lookup in flight for the same track sees the bulk
result.

Failure handling preserves the requeue contract:

- An empty result dictionary means the whole request failed; every message is
  requeued and the cooldown is armed (`:264-269`, `:336-341`).
- A non-empty dictionary missing one ID means a partial parse failure for that
  ID; only that message is requeued (`:281-285`, `:353-356`).
- A `RetryAfterExceededException` (a 429) records the rate-limit state, marks
  each affected saga partial, publishes the rate-limited sentinel so any waiter
  unblocks, and requeues (`HandleBulkRateLimitAsync:441-481`).

A rate-limit guard also sits ahead of every flush: if the bulk track or album
endpoint is currently rate-limited, the processor skips the flush rather than
issuing a call that will fail (`ShouldProcessBulkTracksAsync:146-159`,
`ShouldProcessBulkAlbumsAsync:187-200`).

## Interactive rate-limit deferral signal

Interactive lookups travel the generic worker path, not the bulk path. When an
interactive lookup is rate-limited there, the worker requeues it at background
priority so it retries off the interactive lane, and emits two signals
(`QueueProcessorBackgroundService.cs:262-265`):

- A warning log, EventId **3018** (`InteractiveDeferredToBackground`,
  `src/BridgeBeats.Core/Infrastructure/Logging/LogEventIds.cs:1209`; declared at
  `LogLevel.Warning`, `QueueProcessorBackgroundService.cs:477-486`), tagged with
  the saga ID, lookup type, endpoint, and retry-after time.
- A counter metric, `bridgebeats.ratelimit.interactive_deferred.total`
  (`QueueMetrics.InteractiveDeferredTotal`,
  `src/BridgeBeats.Core/Infrastructure/Queue/QueueMetrics.cs:104-108`), tagged
  with provider and endpoint (`RecordInteractiveDeferral:345-353`).

The trigger is the requeued request's `OriginPriority == QueuePriority.Interactive`
at the moment of a rate limit. The signal is provider-agnostic: it fires for any
provider whose interactive lookup is deferred, not only Spotify. A rising count
means interactive users are increasingly absorbing rate-limit delay, which is
the operational symptom to watch.

## Quick reference

| Concern | Where |
|---|---|
| Firehose link typing | `JetStreamWatcherService.IdentifyProviderAsync` (`src/BridgeBeats.Worker.JetStreamWatcher/JetStreamWatcherService.cs:334-359`) |
| Routing decorator | `SpotifyBulkQueueDecorator.EnqueueAsync` (`src/BridgeBeats.Core/Infrastructure/Queue/SpotifyBulkQueueDecorator.cs:60-94`) |
| Flush predicate | `SpotifyBulkProcessorService.ShouldFlush` (`src/BridgeBeats.Worker.Spotify/SpotifyBulkProcessorService.cs:131-136`) |
| Linger backstop default | `SpotifyBatchSettings.DefaultLingerMs` (`src/BridgeBeats.Worker.Spotify/SpotifyBatchSettings.cs:26`) |
| Batch size caps | `SpotifyConstants` (`src/BridgeBeats.Contracts/Constants/SpotifyConstants.cs:16,25`) |
| Bulk stream names | `SpotifyConstants` (`src/BridgeBeats.Contracts/Constants/SpotifyConstants.cs:67,73`) |
| Saga TTL | `QueueSettings.JobExpirationMinutes` (`src/BridgeBeats.Contracts/Records/QueueSettings.cs:27`); applied in `RedisSagaStateManager` |
| Lookup key shape | `LookupKeyBuilder` (`src/BridgeBeats.Core/Infrastructure/Utilities/LookupKeyBuilder.cs`) |
| Interactive deferral signal | EventId 3018 + `bridgebeats.ratelimit.interactive_deferred.total` |
