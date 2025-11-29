# ATProto Lexicon Resolution Setup Guide

This guide explains how to set up and deploy ATProto lexicon resolution for BridgeBeats's custom media lookup lexicon, ensuring full compliance with the [AT Protocol Lexicon Specification](https://atproto.com/specs/lexicon#lexicon-publication-and-resolution).

## Table of Contents

- [Overview](#overview)
- [Lexicon Schema](#lexicon-schema)
- [Creating the lexicon record](#creating-the-lexicon/schema-record)
- [DNS Configuration](#dns-configuration)

## Overview

BridgeBeats uses a custom ATProto lexicon to store music lookup results as structured records on ATProto Personal Data Servers (PDS). The lexicon defines the schema for `media.tunebridge.dev.lookup` records.

**NSID (Namespaced Identifier):** `media.tunebridge.dev.lookup`

**Authority Domain:** `dev.bridgebeats.link` (from NSID reverse-DNS: `media.tunebridge.dev` → `dev.bridgebeats.link`)

According to the ATProto specification, lexicon schemas must be:
1. Published at a predictable HTTPS endpoint on the authority domain
2. Optionally registered via DNS TXT records for enhanced authority verification
3. Served with proper CORS headers for cross-origin access
4. Accessible to ATProto clients and tools for schema validation

## Lexicon Schema

The lexicon schema is located at:
```
src/Web/wwwroot/.well-known/atproto-lexicon/media.tunebridge.dev.lookup
```

And is served at:
```
https://dev.bridgebeats.link/.well-known/atproto-lexicon/media.tunebridge.dev.lookup
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


## Creating the lexicon/schema record

Use [goat](https://github.com/bluesky-social/goat) to create the schema record.

```sh
cp BridgeBeats/src/Web/wwwroot/.well-known/atproto-lexicon/media.tunebridge.dev.lookup ./media.tunebridge.dev.lookup.json
goat account login -u "stage-atproto.pds.bridgebeats.link" --app-password $(cat BridgeBeats/secrets/atproto_password.txt)
goat record create --rkey media.tunebridge.dev.lookup ./media.tunebridge.dev.lookup.json
```
The output from the record create command will look something like this:
```
at://did:plc:{did_value}/com.atproto.lexicon.schema/media.tunebridge.dev.lookup ...
```

Take the DID portion `did:plc:{did_value}` and create your DNS TXT Record.


## DNS Configuration

To establish domain authority for the lexicon NSID, you should configure DNS TXT records. This step is **required** for full ATProto compliance and enhanced trust.

### Required DNS TXT Records

Add the following DNS TXT record to your domain (`dev.bridgebeats.link`):

**Record Type:** TXT  
**Host/Name:** `_lexicon`  
**Value:** `did=<your-did-here>`  
**TTL:** 3600 (1 hour) or your preferred value

### Example DNS Configuration

```
_lexicon.dev.bridgebeats.link.    3600    IN    TXT    "did=did:plc:your-did-identifier"
```
