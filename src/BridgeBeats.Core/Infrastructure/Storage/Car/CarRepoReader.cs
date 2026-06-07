using System.Text.Json.Nodes;

namespace BridgeBeats.Core.Infrastructure.Storage.Car;

/// <summary>
/// Facade for reading atproto repo CAR files.
/// The only type <see cref="ATProtoStorageService"/> touches directly.
/// </summary>
internal static class CarRepoReader {

    /// <summary>
    /// Enumerates all records in the given collection from a CAR v1 repo download.
    /// </summary>
    /// <param name="car">Raw CAR v1 bytes from com.atproto.sync.getRepo.</param>
    /// <param name="collectionNsid">The NSID to filter by (e.g. "link.bridgebeats.lookup").</param>
    /// <param name="cancellationToken">Token to cancel a long-running MST walk.</param>
    /// <returns>
    /// A <see cref="CarEnumerationResult"/> containing the record sequence, block count, and commit
    /// version. The caller is responsible for acting on a non-3 commit version.
    /// Enumeration throws <see cref="CarParseException"/> for CAR/MST structural errors; callers
    /// outside Core can only catch broadly since <see cref="CarParseException"/> is internal.
    /// </returns>
    internal static CarEnumerationResult EnumerateCollection(
        ReadOnlyMemory<byte> car,
        string collectionNsid,
        CancellationToken cancellationToken = default
    ) {
        string prefix = collectionNsid + "/";
        CarFile carFile = CarV1Reader.Read( car );
        int blockCount = carFile.Blocks.Count;

        (IEnumerable<(string Key, string ValueCidHex)> records, int commitVersion) =
            MstWalker.EnumerateRecords( carFile, cancellationToken );

        return new CarEnumerationResult(
            Records: ProjectRecords( records, carFile, prefix ),
            BlockCount: blockCount,
            CommitVersion: commitVersion
        );
    }

    private static IEnumerable<(string Rkey, JsonNode Record)> ProjectRecords(
        IEnumerable<(string Key, string ValueCidHex)> records,
        CarFile carFile,
        string prefix
    ) {
        foreach ((string key, string valueCidHex) in records) {
            if (!key.StartsWith( prefix, StringComparison.Ordinal )) {
                continue;
            }

            string rkey = key[prefix.Length..];

            if (!carFile.Blocks.TryGetValue( valueCidHex, out ReadOnlyMemory<byte> blockBytes )) {
                throw new CarParseException( $"Value block {valueCidHex} for key '{key}' not found in CAR." );
            }

            JsonNode recordNode = DagCborConverter.ToJsonNode( blockBytes )
                ?? throw new CarParseException( $"Record block for key '{key}' deserialized to null." );

            yield return (rkey, recordNode);
        }
    }
}

/// <summary>
/// Result of <see cref="CarRepoReader.EnumerateCollection"/>.
/// </summary>
internal sealed record CarEnumerationResult(
    IEnumerable<(string Rkey, System.Text.Json.Nodes.JsonNode Record)> Records,
    int BlockCount,
    int CommitVersion
);
