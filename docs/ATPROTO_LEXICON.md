# ATProto Lexicon Resolution Setup Guide

This guide explains how to set up and deploy ATProto lexicon resolution for BridgeBeats's custom media lookup and playlist lexicons, ensuring full compliance with the [AT Protocol Lexicon Specification](https://atproto.com/specs/lexicon#lexicon-publication-and-resolution).

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
- `link.bridgebeats.lookup` - Music lookup results (stored on BridgeBeats server PDS)
- `link.bridgebeats.playlist` - User playlists with cross-platform substitutions (stored on user's PDS)

**Note:** The lexicon NSIDs use "bridgebeats" and cannot be changed without breaking ATProto compatibility for existing records. The authority domain for serving the lexicon schemas is `bridgebeats.link`.

According to the ATProto specification, lexicon schemas must be:
1. Published at a predictable HTTPS endpoint on the authority domain
2. Optionally registered via DNS TXT records for enhanced authority verification
3. Served with proper CORS headers for cross-origin access
4. Accessible to ATProto clients and tools for schema validation

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

### Schema Structure

The lexicon defines two main types:

1. **MediaLinkResult** (main record type):
   - `results`: Array of provider results (1-10 items)
   - `lookedUpAt`: ISO8601 timestamp

2. **ProviderResult** (embedded object):
   - `provider`: enum ("appleMusic", "spotify", "tidal")
   - `artist`: string (max 500 chars)
   - `title`: string (max 500 chars)
   - `externalId`: optional ISRC/UPC string (max 50 chars)
   - `url`: URI string (max 2000 chars)
   - `artUrl`: optional URI string (max 2000 chars)
   - `marketRegion`: ISO3166-1 alpha-2 country code (default "us")
   - `isAlbum`: boolean

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

#### Schema Structure

The playlist lexicon supports user-created playlists stored on the user's own PDS (not the BridgeBeats server PDS):

1. **PlaylistRecord** (main record type):
   - `title`: string (required, max 300 chars)
   - `description`: optional string (max 3000 chars)
   - `createdBy`: DID of playlist creator (required)
   - `updatedAt`: ISO8601 UTC timestamp (required)
   - `lookupRepository`: DID of the repository containing lookup records (defaults to BridgeBeats server DID)
   - `tracks`: Array of rkey strings referencing `link.bridgebeats.lookup` records (1-16,384 items)
   - `substitutions`: Optional per-provider track substitutions

2. **SubstitutionMap** (embedded object):
   - `appleMusic`: Map of track index → substitute rkey
   - `spotify`: Map of track index → substitute rkey
   - `tidal`: Map of track index → substitute rkey

#### Key Design Decisions

- **Tracks are rkeys, not full AT-URIs**: To support up to 16,384 tracks within ATProto's ~1MB record size limit, tracks are stored as rkey strings (e.g., `track:USRC12345678`) rather than full AT-URIs. The `lookupRepository` field provides the DID needed to reconstruct full AT-URIs.

- **Substitutions enable cross-platform flexibility**: When a track isn't available on a specific provider, users can specify an alternative track for that provider at a given playlist position.

- **User PDS storage**: Unlike lookup records (stored on the BridgeBeats server PDS), playlist records are stored on the user's own PDS, giving users full ownership of their playlist data.

- **TID-based record keys**: Playlist record keys use Timestamp Identifiers (TIDs) for chronological sorting, rather than deterministic keys.

See the lexicon file for the complete JSON schema definition.


## Creating the lexicon/schema record

Use [goat](https://github.com/bluesky-social/goat) to create the schema records.

### Lookup Lexicon

```sh
cp BridgeBeats/src/BridgeBeats.Web/wwwroot/.well-known/atproto-lexicon/link.bridgebeats.lookup ./link.bridgebeats.lookup.json
goat account login -u "stage-atproto.pds.bridgebeats.link" --app-password $(cat BridgeBeats/secrets/atproto_password.txt)
goat record create --rkey link.bridgebeats.lookup ./link.bridgebeats.lookup.json
```

### Playlist Lexicon

```sh
cp BridgeBeats/src/BridgeBeats.Web/wwwroot/.well-known/atproto-lexicon/link.bridgebeats.playlist ./link.bridgebeats.playlist.json
goat account login -u "stage-atproto.pds.bridgebeats.link" --app-password $(cat BridgeBeats/secrets/atproto_password.txt)
goat record create --rkey link.bridgebeats.playlist ./link.bridgebeats.playlist.json
```

The output from the record create command will look something like this:
```
at://did:plc:{did_value}/com.atproto.lexicon.schema/link.bridgebeats.lookup ...
at://did:plc:{did_value}/com.atproto.lexicon.schema/link.bridgebeats.playlist ...
```

Take the DID portion `did:plc:{did_value}` and create your DNS TXT Record.


## DNS Configuration

To establish domain authority for the lexicon NSID, you should configure DNS TXT records. This step is **required** for full ATProto compliance and enhanced trust.

### Required DNS TXT Records

Add the following DNS TXT record to your domain (`bridgebeats.link`):

**Record Type:** TXT
**Host/Name:** `_lexicon`
**Value:** `did=<your-did-here>`
**TTL:** 3600 (1 hour) or your preferred value

### Example DNS Configuration

```
_lexicon.bridgebeats.link.    3600    IN    TXT    "did=did:plc:your-did-identifier"
```
