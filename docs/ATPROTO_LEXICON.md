# ATProto Lexicon Resolution Setup Guide

This guide explains how to set up and deploy ATProto lexicon resolution for TuneBridge's custom media lookup lexicon, ensuring full compliance with the [AT Protocol Lexicon Specification](https://atproto.com/specs/lexicon#lexicon-publication-and-resolution).

## Table of Contents

- [Overview](#overview)
- [Lexicon Schema](#lexicon-schema)
- [DNS Configuration](#dns-configuration)
- [HTTPS Endpoint Setup](#https-endpoint-setup)
- [CORS and Security Policies](#cors-and-security-policies)
- [Lexicon Authority Resolution](#lexicon-authority-resolution)
- [Verification](#verification)
- [Troubleshooting](#troubleshooting)

## Overview

TuneBridge uses a custom ATProto lexicon to store music lookup results as structured records on ATProto Personal Data Servers (PDS). The lexicon defines the schema for `media.tunebridge.dev.lookup.result` records.

**NSID (Namespaced Identifier):** `media.tunebridge.dev.lookup.result`

**Deployment Domain:** `dev.tunebridge.media`

**Authority Domain:** `tunebridge.dev` (from NSID reverse-DNS)

According to the ATProto specification, lexicon schemas must be:
1. Published at a predictable HTTPS endpoint on the authority domain
2. Optionally registered via DNS TXT records for enhanced authority verification
3. Served with proper CORS headers for cross-origin access
4. Accessible to ATProto clients and tools for schema validation

## Lexicon Schema

The lexicon schema is located at:
```
src/Web/wwwroot/.well-known/atproto-lexicon/media.tunebridge.dev.lookup.result
```

And is served at:
```
https://dev.tunebridge.media/.well-known/atproto-lexicon/media.tunebridge.dev.lookup.result
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

## DNS Configuration

To establish domain authority for the lexicon NSID, you should configure DNS TXT records. This step is **optional but recommended** for full ATProto compliance and enhanced trust.

**Note:** The NSID `media.tunebridge.dev.lookup.result` suggests authority domain `tunebridge.dev`, but TuneBridge is deployed at `dev.tunebridge.media`. You have two options:

1. **Configure DNS for `tunebridge.dev`** (NSID authority domain) with delegation to `dev.tunebridge.media`
2. **Configure DNS for `dev.tunebridge.media`** (deployment domain) directly

This guide focuses on option 2 (deployment domain configuration) as it's simpler for most deployments.

### Required DNS TXT Records

Add the following DNS TXT record to your deployment domain (`dev.tunebridge.media`):

**Record Type:** TXT  
**Host/Name:** `_lexicon`  
**Value:** `did=<your-did-here>`  
**TTL:** 3600 (1 hour) or your preferred value

### Example DNS Configuration

For the deployment domain:
```
_lexicon.dev.tunebridge.media.    3600    IN    TXT    "did=did:plc:your-did-identifier"
```

Or if using `did:web`:
```
_lexicon.dev.tunebridge.media.    3600    IN    TXT    "did=did:web:dev.tunebridge.media"
```

### DNS Provider-Specific Instructions

<details>
<summary><b>Cloudflare</b></summary>

1. Log in to Cloudflare dashboard
2. Select your domain
3. Go to **DNS** → **Records**
4. Click **Add record**
5. Set:
   - **Type:** TXT
   - **Name:** `_lexicon`
   - **Content:** `did=did:plc:your-did-identifier`
   - **TTL:** Auto
6. Click **Save**
</details>

<details>
<summary><b>AWS Route 53</b></summary>

1. Open Route 53 console
2. Select your hosted zone
3. Click **Create record**
4. Set:
   - **Record name:** `_lexicon`
   - **Record type:** TXT
   - **Value:** `"did=did:plc:your-did-identifier"`
   - **TTL:** 3600
5. Click **Create records**
</details>

<details>
<summary><b>Google Cloud DNS</b></summary>

1. Open Cloud DNS console
2. Select your DNS zone
3. Click **Add record set**
4. Set:
   - **DNS name:** `_lexicon`
   - **Resource record type:** TXT
   - **TXT data:** `"did=did:plc:your-did-identifier"`
   - **TTL:** 1 hour
5. Click **Create**
</details>

<details>
<summary><b>Namecheap</b></summary>

1. Log in to Namecheap
2. Go to **Domain List** → select domain
3. Click **Advanced DNS**
4. Click **Add New Record**
5. Set:
   - **Type:** TXT Record
   - **Host:** `_lexicon`
   - **Value:** `did=did:plc:your-did-identifier`
   - **TTL:** Automatic
6. Click the checkmark to save
</details>

### Obtaining Your DID

Your DID (Decentralized Identifier) depends on which ATProto identity method you use:

- **did:plc** - Generated when you create an account on an ATProto PDS (like bsky.social)
  - Find it in your account settings or via: `https://bsky.social/xrpc/com.atproto.identity.resolveHandle?handle=yourhandle.bsky.social`
  
- **did:web** - Based on your domain name
  - Format: `did:web:yourdomain.com`
  - Requires publishing a DID document at: `https://yourdomain.com/.well-known/did.json`

## HTTPS Endpoint Setup

The lexicon file is automatically served by TuneBridge's web server when deployed. No additional configuration is required if you're using the standard deployment methods.

### Endpoint Details

**URL Pattern:**
```
https://<your-domain>/.well-known/atproto-lexicon/<full-nsid>
```

**For TuneBridge:**
```
https://dev.tunebridge.media/.well-known/atproto-lexicon/media.tunebridge.dev.lookup.result
```

### Content Type

The endpoint serves the lexicon file with:
- **Content-Type:** `application/json`
- **Character Encoding:** UTF-8

### Automatic Configuration

TuneBridge's startup configuration (`StartupExtensions.cs`) automatically:
1. Serves files from `wwwroot/.well-known/` at `/.well-known/` URL path
2. Sets `Content-Type: application/json` for files in the `atproto-lexicon` directory
3. Adds proper CORS headers (see next section)
4. Implements cache control headers

### Deployment Verification

After deployment, verify the endpoint is accessible:

```bash
curl -i https://dev.tunebridge.media/.well-known/atproto-lexicon/media.tunebridge.dev.lookup.result
```

Expected response:
```
HTTP/2 200
content-type: application/json
access-control-allow-origin: *
access-control-allow-methods: GET, HEAD, OPTIONS
access-control-allow-headers: Content-Type
cache-control: public, max-age=86400
```

## CORS and Security Policies

TuneBridge implements appropriate CORS (Cross-Origin Resource Sharing) headers to allow ATProto clients and tools to fetch the lexicon schema from any origin.

### CORS Headers

The following CORS headers are automatically set for lexicon files:

```
Access-Control-Allow-Origin: *
Access-Control-Allow-Methods: GET, HEAD, OPTIONS
Access-Control-Allow-Headers: Content-Type
```

**Rationale:**
- `Allow-Origin: *` - Allows any ATProto client to fetch the lexicon
- `Allow-Methods: GET, HEAD, OPTIONS` - Supports standard HTTP methods for schema retrieval
- `Allow-Headers: Content-Type` - Permits content-type negotiation

### Cache Control

Lexicon files are cached for 24 hours to reduce server load:

```
Cache-Control: public, max-age=86400
```

This is safe because lexicon schemas are versioned and changes require NSID version bumps.

### Security Considerations

- **Read-Only Access:** Lexicon endpoints serve files read-only (no POST/PUT/DELETE)
- **No Authentication Required:** Lexicon schemas are public by design
- **Rate Limiting:** Standard TuneBridge rate limiting applies (20 requests/hour per user)
- **HTTPS Required:** Lexicon resolution MUST use HTTPS in production

### Additional Security Measures

If you need to restrict lexicon access, you can:

1. **IP Allowlisting** (not recommended for public lexicons):
   ```csharp
   // In OnPrepareResponse
   if (ctx.File.PhysicalPath?.Contains("atproto-lexicon") == true) {
       var remoteIp = ctx.Context.Connection.RemoteIpAddress;
       // Add IP validation logic
   }
   ```

2. **Require API Key** (breaks ATProto spec compliance):
   - Not recommended as it prevents standard lexicon resolution

3. **Monitor Access Logs:**
   ```bash
   # View lexicon access logs
   grep "atproto-lexicon" /var/log/nginx/access.log
   ```

## Lexicon Authority Resolution

ATProto clients resolve lexicon authority in the following order:

### 1. DNS TXT Record Resolution (Recommended)

Clients query the `_lexicon` TXT record to find the authoritative DID.

For TuneBridge's deployment domain:
```bash
dig _lexicon.dev.tunebridge.media TXT
```

Expected response:
```
_lexicon.dev.tunebridge.media. 3600 IN TXT "did=did:plc:your-identifier"
```

*Note: Theoretically, the NSID authority domain (`tunebridge.dev`) could also be used, but using the deployment domain is simpler.*

### 2. HTTPS Endpoint Resolution (Fallback)

If DNS TXT record is not found, clients directly fetch from the deployment endpoint:

```
https://dev.tunebridge.media/.well-known/atproto-lexicon/media.tunebridge.dev.lookup.result
```

### 3. DID Method Support

TuneBridge supports both ATProto DID methods:

#### did:plc (Portable Linked Credentials)

- **Format:** `did:plc:<base32-encoded-identifier>`
- **Resolution:** Via PLC directory (`https://plc.directory/`)
- **Use Case:** Portable identity across PDS instances
- **Example:** `did:plc:7iza6de2dwap2sbkpav7c6c6`

**Setup:**
1. Create ATProto account on a PDS (e.g., bsky.social)
2. Retrieve your DID from account settings
3. Add DNS TXT record: `_lexicon.dev.tunebridge.media TXT "did=did:plc:your-identifier"`

#### did:web (Web DID)

- **Format:** `did:web:<domain>`
- **Resolution:** Via HTTPS at `https://<domain>/.well-known/did.json`
- **Use Case:** Domain-based identity
- **Example:** `did:web:dev.tunebridge.media`

**Setup:**
1. Create DID document at `src/Web/wwwroot/.well-known/did.json`:
   ```json
   {
     "@context": [
       "https://www.w3.org/ns/did/v1",
       "https://w3id.org/security/suites/jws-2020/v1"
     ],
     "id": "did:web:dev.tunebridge.media",
     "verificationMethod": [
       {
         "id": "did:web:dev.tunebridge.media#key-1",
         "type": "JsonWebKey2020",
         "controller": "did:web:dev.tunebridge.media",
         "publicKeyJwk": {
           "kty": "EC",
           "crv": "P-256",
           "x": "your-public-key-x",
           "y": "your-public-key-y"
         }
       }
     ],
     "service": [
       {
         "id": "did:web:dev.tunebridge.media#atproto_pds",
         "type": "AtprotoPersonalDataServer",
         "serviceEndpoint": "https://pds.dev.tunebridge.media"
       }
     ]
   }
   ```
2. Add DNS TXT record: `_lexicon.dev.tunebridge.media TXT "did=did:web:dev.tunebridge.media"`

### Resolution Flow Diagram

```
┌─────────────────────────────────────────┐
│ ATProto Client needs lexicon schema     │
│ for "media.tunebridge.dev.lookup.result"│
└────────────────┬────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────┐
│ Extract domain from NSID: tunebridge.dev│
│ Or use deployment domain: dev.tunebridge.media│
└────────────────┬────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────┐
│ Query DNS: _lexicon.dev.tunebridge.media TXT│
└────────────┬────────────────────────────┘
             │
    ┌────────┴────────┐
    │                 │
    ▼                 ▼
┌────────┐     ┌──────────────┐
│ Found  │     │  Not Found   │
└───┬────┘     └──────┬───────┘
    │                 │
    ▼                 ▼
┌─────────┐   ┌───────────────────────────┐
│ Get DID │   │ Fetch from deployment:    │
│         │   │ https://dev.tunebridge.   │
│         │   │ media/.well-known/        │
│         │   │ atproto-lexicon/<nsid>    │
└───┬─────┘   └───────────┬───────────────┘
    │                     │
    ▼                     │
┌──────────────┐          │
│ Resolve DID  │          │
│ Document     │          │
└───┬──────────┘          │
    │                     │
    └──────────┬──────────┘
               │
               ▼
    ┌────────────────────┐
    │ Return lexicon JSON│
    └────────────────────┘
```

## Verification

After completing the setup, verify your lexicon resolution is working correctly:

### 1. DNS Verification

Check DNS TXT record:
```bash
dig _lexicon.dev.tunebridge.media TXT +short
```

Expected output:
```
"did=did:plc:your-identifier"
```

Or use online DNS checkers:
- [Google DNS Checker](https://dns.google/)
- [MXToolbox](https://mxtoolbox.com/TXTLookup.aspx)

### 2. HTTPS Endpoint Verification

Test the lexicon endpoint:
```bash
curl -i https://dev.tunebridge.media/.well-known/atproto-lexicon/media.tunebridge.dev.lookup.result
```

Verify:
- ✅ HTTP 200 status code
- ✅ `Content-Type: application/json` header
- ✅ CORS headers present
- ✅ Valid JSON response
- ✅ `"lexicon": 1` and `"id": "media.tunebridge.dev.lookup.result"` in JSON

### 3. CORS Verification

Test cross-origin access:
```bash
curl -i -H "Origin: https://example.com" https://dev.tunebridge.media/.well-known/atproto-lexicon/media.tunebridge.dev.lookup.result
```

Verify `Access-Control-Allow-Origin: *` header is present.

### 4. DID Resolution Verification

For `did:plc`:
```bash
curl https://plc.directory/did:plc:your-identifier
```

For `did:web`:
```bash
curl https://dev.tunebridge.media/.well-known/did.json
```

### 5. ATProto Client Validation

Use ATProto SDK to resolve the lexicon:

**TypeScript:**
```typescript
import { LexiconResolver } from '@atproto/lexicon-resolver';

const resolver = new LexiconResolver();
const lexicon = await resolver.resolve('media.tunebridge.dev.lookup.result');
console.log(lexicon);
```

**Python (using atproto package):**
```python
from atproto import Client

client = Client()
# Lexicon resolution is handled automatically when creating records
```

### 6. Schema Validation

Validate that stored records conform to the lexicon:

```bash
# Get a record
curl -X GET "https://pds.example.com/xrpc/com.atproto.repo.getRecord" \
  -H "Authorization: Bearer $TOKEN" \
  -G \
  --data-urlencode "repo=$DID" \
  --data-urlencode "collection=media.tunebridge.dev.lookup.result" \
  --data-urlencode "rkey=$RKEY"
```

The PDS should validate the record against the lexicon schema.

## Troubleshooting

### DNS Issues

**Problem:** DNS TXT record not resolving

**Solutions:**
1. Wait for DNS propagation (can take up to 48 hours)
2. Check DNS syntax - value must be quoted: `"did=did:plc:..."`
3. Verify record name is exactly `_lexicon` (no trailing dots)
4. Test with `dig` or `nslookup`:
   ```bash
   nslookup -type=TXT _lexicon.dev.tunebridge.media
   ```

### HTTPS Endpoint Issues

**Problem:** 404 Not Found on lexicon endpoint

**Solutions:**
1. Verify file exists at `src/Web/wwwroot/.well-known/atproto-lexicon/media.tunebridge.dev.lookup.result`
2. Check file has no `.json` extension (per ATProto spec)
3. Ensure deployment copied `wwwroot` directory
4. Test locally: `curl http://localhost:10000/.well-known/atproto-lexicon/media.tunebridge.dev.lookup.result`

**Problem:** Wrong Content-Type (not `application/json`)

**Solutions:**
1. Verify `StartupExtensions.cs` has proper content-type configuration
2. Check server logs for static file serving errors
3. Test with verbose curl: `curl -v https://dev.tunebridge.media/.well-known/atproto-lexicon/...`

### CORS Issues

**Problem:** CORS errors in browser console

**Solutions:**
1. Verify CORS headers in response: `curl -i -H "Origin: https://example.com" <url>`
2. Check `OnPrepareResponse` callback is executing
3. Ensure no proxy/CDN is stripping CORS headers
4. For Cloudflare: Check that CORS headers aren't being overridden

### Validation Issues

**Problem:** PDS rejects records with validation errors

**Solutions:**
1. Verify lexicon JSON is valid: `jq . < media.tunebridge.dev.lookup.result`
2. Check all required fields are present in lexicon schema
3. Ensure field types match (string, number, boolean, array, object)
4. Verify enum values are correct (e.g., "appleMusic" not "Apple Music")
5. Test record creation with validation enabled:
   ```csharp
   await agent.CreateRecord(
       record: record,
       collection: nsid,
       validate: true  // Enable validation
   );
   ```

### DID Resolution Issues

**Problem:** DID not resolving

**For did:plc:**
1. Verify DID exists: `curl https://plc.directory/$DID`
2. Check DID format: must be `did:plc:<base32-identifier>`
3. Ensure account is active on PDS

**For did:web:**
1. Verify DID document exists at `/.well-known/did.json`
2. Check DID document JSON is valid
3. Ensure HTTPS is enabled (HTTP not supported)
4. Test: `curl https://dev.tunebridge.media/.well-known/did.json`

### Caching Issues

**Problem:** Changes to lexicon not reflecting

**Solutions:**
1. Wait for cache expiration (24 hours)
2. Clear CDN/proxy cache if using Cloudflare/AWS CloudFront
3. Force browser refresh: Ctrl+Shift+R (Windows/Linux) or Cmd+Shift+R (Mac)
4. For testing, temporarily reduce cache TTL in `StartupExtensions.cs`:
   ```csharp
   ctx.Context.Response.Headers.Append("Cache-Control", "public, max-age=60");
   ```

## References

- [ATProto Lexicon Specification](https://atproto.com/specs/lexicon)
- [ATProto Lexicon Publication and Resolution](https://atproto.com/specs/lexicon#lexicon-publication-and-resolution)
- [ATProto DID Specification](https://atproto.com/specs/did)
- [ATProto Identity Guide](https://atproto.com/guides/identity)
- [ATProto Handle Resolution](https://atproto.com/specs/handle)
- [Bluesky Identity Resolution Guide](https://docs.bsky.app/docs/advanced-guides/resolving-identities)
- [Lexicon Resolution RFC (GitHub Discussion)](https://github.com/bluesky-social/atproto/discussions/3074)

## Support

For issues or questions about lexicon resolution:

1. Check existing [GitHub Issues](https://github.com/tsmarvin/TuneBridge/issues)
2. Review [ATProto Community Wiki](https://atproto.wiki/)
3. Ask in [Bluesky/ATProto Discord](https://discord.gg/bluesky)
4. Open a new issue with:
   - DNS record configuration
   - curl output from lexicon endpoint
   - Error messages or logs
   - Steps to reproduce

---

*Last Updated: November 2025*
