namespace BridgeBeats.Contracts.Records {

    /// <summary>
    /// Request for web-specific lookup.
    /// </summary>
    /// <param name="Uri">Music URL(s) to look up (can contain multiple URLs).</param>
    public record WebLookupRequest( string Uri );

}
