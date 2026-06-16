# ATProto Lexicon Resolution Setup Guide

This guide explains how to set up and deploy ATProto lexicon resolution for BridgeBeats's custom media lookup and playlist lexicons, ensuring compliance with the [AT Protocol Lexicon Specification](https://atproto.com/specs/lexicon#lexicon-publication-and-resolution).

## Table of Contents

- [Overview](#overview)
- [Lexicon Schemas](#lexicon-schemas)
  - [link.bridgebeats.lookup](#linkbridgebeatslookup)
  - [link.bridgebeats.playlist](#linkbridgebeatsplaylist)
- [Creating the lexicon record](#creating-the-lexiconschema-record)
- [DNS Configuration](#dns-configuration)

## Overview

BridgeBeats uses custom ATProto lexicons to store music lookup results and user playlists as structured records on ATProto Personal Data Servers (PDS).

**Lexicons:**

- `link.bridgebeats.lookup` — Music lookup results (stored on the BridgeBeats server PDS)
- `link.bridgebeats.playlist` — User playlists with cross-platform substitutions (stored on the user's PDS)

The `link.bridgebeats.lookup` NSID is also a code constant: `ATProtoStorageService` registers the collection as `new Nsid("link.bridgebeats.lookup")`. Changing the NSIDs would break ATProto compatibility for existing records. The authority domain for serving the lexicon schemas is `bridgebeats.link`.

According to the ATProto specification, lexicon schemas must be:

1. Published at a predictable HTTPS endpoint on the authority domain
2. Optionally registered via DNS TXT records for stronger authority verification
3. Served with proper CORS headers for cross-origin access
4. Accessible to ATProto clients and tools for schema validation

BridgeBeats serves the published schema files from `wwwroot/.well-known/atproto-lexicon/` as static files with `Content-Type: application/json; charset=utf-8`, an `Access-Control-Allow-Origin: *` header (along with `Access-Control-Allow-Methods: GET, HEAD, OPTIONS`), and a one-day cache. This serving behavior is configured in `StartupExtensions`.

## Lexicon Schemas

### link.bridgebeats.lookup

The lookup lexicon schema is located at:

```
src/BridgeBeats.Web/wwwroot/.well-known/atproto-lexicon/link.bridgebeats.lookup
```

And is served at:

```
https://bridgebeats.link/.well-known/atproto-lexicon/link.bridgebeats.lookup
```

#### Schema structure

The lexicon defines two types:

1. **main** (record type), keyed by `any`. Its record requires `results` and `lookedUpAt`:
   - `results`: array of provider results (1–10 items)
   - `lookedUpAt`: ISO8601 datetime timestamp

2. **providerResult** (embedded object), requiring `provider`, `artist`, `title`, `url`, and `marketRegion`:
   - `provider`: enum (`appleMusic`, `spotify`, `tidal`)
   - `artist`: string (max 500 chars)
   - `title`: string (max 500 chars)
   - `externalId`: optional ISRC (tracks) or UPC (albums) string (max 50 chars)
   - `url`: URI string (max 2000 chars)
   - `artUrl`: optional URI string (max 2000 chars)
   - `marketRegion`: ISO3166-1 alpha-2 country code (max 2 chars, default `us`)
   - `isAlbum`: boolean (true for albums/EPs, false for tracks)

See the lexicon file for the complete JSON schema definition.

### link.bridgebeats.playlist

The playlist lexicon schema is located at:

```
src/BridgeBeats.Web/wwwroot/.well-known/atproto-lexicon/link.bridgebeats.playlist
```

And is served at:

```
https://bridgebeats.link/.well-known/atproto-lexicon/link.bridgebeats.playlist
```

#### Schema structure

The playlist lexicon supports user-created playlists stored on the user's own PDS (not the BridgeBeats server PDS).

1. **main** (record type), keyed by `tid`. Its record requires `title`, `createdBy`, `updatedAt`, `lookupRepository`, and `tracks`:
   - `title`: string (required, max 300 chars / max 100 graphemes)
   - `description`: optional string (max 3000 chars / max 1000 graphemes)
   - `createdBy`: DID of the playlist creator (required)
   - `updatedAt`: ISO 8601 UTC datetime timestamp (required)
   - `lookupRepository`: DID of the repository containing the referenced `link.bridgebeats.lookup` records (required; defaults to the BridgeBeats server DID)
   - `tracks`: array of rkey strings referencing `link.bridgebeats.lookup` records (1–16,384 items)
   - `substitutions`: optional per-provider track substitutions

2. **substitutionMap** (embedded object): maps each provider name to its index substitutions.
   - `appleMusic`: an `indexSubstitutions` map for Apple Music
   - `spotify`: an `indexSubstitutions` map for Spotify
   - `tidal`: an `indexSubstitutions` map for Tidal

3. **indexSubstitutions** (embedded object): maps zero-based track-index strings to substitute track rkeys.

#### Key design decisions

- **Tracks are rkeys, not full AT-URIs**: to support up to 16,384 tracks within ATProto's record-size limit, tracks are stored as rkey strings rather than full AT-URIs. Each rkey is at most 18 characters in the format `track:{ISRC}` and must carry a valid ISRC. The `lookupRepository` field provides the DID needed to reconstruct full AT-URIs. The `PlaylistRecord` contract notes that albums are expanded to individual tracks.

- **Substitutions enable cross-platform flexibility**: when a track isn't available on a specific provider, users can specify an alternative track for that provider at a given playlist position.

- **User PDS storage**: unlike lookup records (stored on the BridgeBeats server PDS), playlist records are stored on the user's own PDS, giving users full ownership of their playlist data.

- **TID-based record keys**: playlist record keys use Timestamp Identifiers (TIDs) for chronological sorting, rather than deterministic keys. This is reflected in the lexicon's `"key": "tid"`.

See the lexicon file for the complete JSON schema definition.

## Creating the lexicon/schema record

Use [goat](https://github.com/bluesky-social/goat) to create the schema records.

> The commands below are recovered from the prior version of this guide. Verify the PDS hostname (`stage-atproto.pds.bridgebeats.link`), the secrets path, and the current `goat` CLI flags against your deployment before running them; they were not re-validated against a running PDS for this revision.

### Lookup lexicon

```sh
cp BridgeBeats/src/BridgeBeats.Web/wwwroot/.well-known/atproto-lexicon/link.bridgebeats.lookup ./link.bridgebeats.lookup.json
goat account login -u "stage-atproto.pds.bridgebeats.link" --app-password $(cat BridgeBeats/secrets/atproto_password.txt)
goat record create --rkey link.bridgebeats.lookup ./link.bridgebeats.lookup.json
```

### Playlist lexicon

```sh
cp BridgeBeats/src/BridgeBeats.Web/wwwroot/.well-known/atproto-lexicon/link.bridgebeats.playlist ./link.bridgebeats.playlist.json
goat account login -u "stage-atproto.pds.bridgebeats.link" --app-password $(cat BridgeBeats/secrets/atproto_password.txt)
goat record create --rkey link.bridgebeats.playlist ./link.bridgebeats.playlist.json
```

The output from the record-create command will look something like this:

```
at://did:plc:{did_value}/com.atproto.lexicon.schema/link.bridgebeats.lookup ...
at://did:plc:{did_value}/com.atproto.lexicon.schema/link.bridgebeats.playlist ...
```

Take the DID portion `did:plc:{did_value}` and create your DNS TXT record.

## DNS Configuration

To establish domain authority for the lexicon NSID, configure DNS TXT records. This step is **required** for full ATProto compliance and stronger trust.

### Required DNS TXT records

Add the following DNS TXT record to your domain (`bridgebeats.link`):

- **Record Type:** TXT
- **Host/Name:** `_lexicon`
- **Value:** `did=<your-did-here>`
- **TTL:** 3600 (1 hour) or your preferred value

### Example DNS configuration

```
_lexicon.bridgebeats.link.    3600    IN    TXT    "did=did:plc:your-did-identifier"
```
