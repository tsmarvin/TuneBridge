using System.Net.Http;
using System.Text.Json;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Infrastructure.Storage;
using BridgeBeats.Core.Infrastructure.Storage.Car;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

#pragma warning disable CS1591

namespace BridgeBeats.Tests.Integration;

/// <summary>
/// Live integration test that verifies the CAR-based record enumeration of the AT Protocol storage
/// service yields the same set of AT-URIs (and matching lookup timestamps) as paging the PDS
/// <c>com.atproto.repo.listRecords</c> XRPC endpoint directly. Requires a real PDS and is ignored unless
/// run manually with the PDS URI and user DID supplied via environment variables.
/// </summary>
/// <remarks>
/// This test requires live PDS access and is intentionally excluded from automated runs.
/// Configure PDS access via environment variables or user-secrets before running:
///   BRIDGEBEATS_TEST_PDS_URI — e.g. https://pds.bridgebeats.link
///   BRIDGEBEATS_TEST_USER_DID — e.g. did:plc:xxxxx
/// Run manually with: dotnet test --filter TestCategory=Integration
/// </remarks>
[TestClass]
[TestCategory( "Integration" )]
public class CarVsListRecordsParityTests {

    /// <summary>Name of the environment variable holding the test PDS URI.</summary>
    private const string PdsUriEnvVar = "BRIDGEBEATS_TEST_PDS_URI";
    /// <summary>Name of the environment variable holding the test user DID.</summary>
    private const string UserDidEnvVar = "BRIDGEBEATS_TEST_USER_DID";
    /// <summary>The lexicon collection NSID whose records are compared.</summary>
    private const string CollectionNsid = "link.bridgebeats.lookup";

    /// <summary>The MSTest-injected test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Enumerates the collection via the CAR-based storage service and via direct paged
    /// <c>listRecords</c> calls, then asserts the two AT-URI sets are identical and their per-record
    /// lookup timestamps agree within one second. Marked inconclusive when the required environment
    /// variables are absent.
    /// </summary>
    [TestMethod]
    [Ignore( "Live PDS test — run manually with: dotnet test --filter TestCategory=Integration" )]
    public async Task CarEnumeration_VsListRecords_YieldsSameAtUriSet( ) {
        // Read operator configuration from environment
        string? pdsUriStr = Environment.GetEnvironmentVariable( PdsUriEnvVar );
        string? userDid = Environment.GetEnvironmentVariable( UserDidEnvVar );

        if (string.IsNullOrWhiteSpace( pdsUriStr ) || string.IsNullOrWhiteSpace( userDid )) {
            Assert.Inconclusive(
                $"Live PDS test requires environment variables {PdsUriEnvVar} and {UserDidEnvVar}. " +
                "Set them before running with: dotnet test --filter TestCategory=Integration" );
            return;
        }

        Uri pdsUri = new( pdsUriStr.TrimEnd( '/' ) );

        // ── CAR-based path ────────────────────────────────────────────────────
        Mock<IATProtoSessionManager> sessionMock = new( );
        Mock<IHttpClientFactory> factoryMock = new( );
        using HttpClient httpClient = new( ) { Timeout = TimeSpan.FromMinutes( 10 ) };
        _ = factoryMock
            .Setup( f => f.CreateClient( ATProtoStorageService.ATProtoSyncHttpClientName ) )
            .Returns( httpClient );

        ATProtoStorageService service = new(
            sessionMock.Object,
            NullLogger<ATProtoStorageService>.Instance,
            factoryMock.Object
        );

        Dictionary<string, DateTimeOffset> carAtUris = [];
        await foreach ((string atUri, MediaLinkResult result) in
            service.ListAllRecordsAsync( pdsUri, userDid, TestContext.CancellationToken )) {
            carAtUris[atUri] = result.LookedUpAt;
        }

        // ── listRecords pagination path ───────────────────────────────────────
        Dictionary<string, DateTimeOffset> listAtUris = [];
        string? cursor = null;
        using HttpClient listClient = new( ) { Timeout = TimeSpan.FromMinutes( 5 ) };

        do {
            string url = $"{pdsUri}/xrpc/com.atproto.repo.listRecords" +
                         $"?repo={Uri.EscapeDataString( userDid )}" +
                         $"&collection={Uri.EscapeDataString( CollectionNsid )}" +
                         "&limit=100" +
                         (cursor is null ? "" : $"&cursor={Uri.EscapeDataString( cursor )}");

            using HttpResponseMessage resp = await listClient.GetAsync( url, TestContext.CancellationToken );
            _ = resp.EnsureSuccessStatusCode( );

            using JsonDocument doc = JsonDocument.Parse( await resp.Content.ReadAsStringAsync( TestContext.CancellationToken ) );
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty( "records", out JsonElement records )) {
                foreach (JsonElement record in records.EnumerateArray( )) {
                    string uri = record.GetProperty( "uri" ).GetString( ) ?? "";
                    DateTimeOffset lookedUpAt = DateTimeOffset.MinValue;

                    if (record.TryGetProperty( "value", out JsonElement val ) &&
                        val.TryGetProperty( "lookedUpAt", out JsonElement lat )) {
                        _ = DateTimeOffset.TryParse( lat.GetString( ), out lookedUpAt );
                    }

                    listAtUris[uri] = lookedUpAt;
                }
            }

            cursor = null;
            if (root.TryGetProperty( "cursor", out JsonElement cursorEl )) {
                cursor = cursorEl.GetString( );
            }
        } while (cursor is not null);

        // ── Parity assertions ─────────────────────────────────────────────────
        // Both must contain the same AT-URI set
        HashSet<string> carSet = [.. carAtUris.Keys];
        HashSet<string> listSet = [.. listAtUris.Keys];

        HashSet<string> onlyInCar = [.. carSet.Except( listSet )];
        HashSet<string> onlyInList = [.. listSet.Except( carSet )];

        Assert.IsEmpty( onlyInCar,
            $"AT-URIs in CAR but not in listRecords: {string.Join( ", ", onlyInCar.Take( 10 ) )}" );
        Assert.IsEmpty( onlyInList,
            $"AT-URIs in listRecords but not in CAR: {string.Join( ", ", onlyInList.Take( 10 ) )}" );

        // LookedUpAt values must match for every record
        List<string> mismatchedLookedUpAt = [];
        foreach ((string uri, DateTimeOffset carLookedUpAt) in carAtUris) {
            if (listAtUris.TryGetValue( uri, out DateTimeOffset listLookedUpAt )) {
                if (Math.Abs( (carLookedUpAt - listLookedUpAt).TotalSeconds ) > 1) {
                    mismatchedLookedUpAt.Add( $"{uri}: CAR={carLookedUpAt:O} list={listLookedUpAt:O}" );
                }
            }
        }

        Assert.IsEmpty( mismatchedLookedUpAt,
            $"LookedUpAt mismatches ({mismatchedLookedUpAt.Count}): {string.Join( ", ", mismatchedLookedUpAt.Take( 5 ) )}" );

        TestContext.WriteLine( $"Parity verified: {carAtUris.Count} records match between CAR and listRecords." );
    }
}

#pragma warning restore CS1591
