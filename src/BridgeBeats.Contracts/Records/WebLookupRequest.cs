namespace BridgeBeats.Contracts.Records {

    /// <summary>
    /// A web-facing request to look up music links from submitted content.
    /// </summary>
    /// <param name="Uri">The music URL or URLs to look up; the value may contain multiple URLs, each of which is extracted and resolved.</param>
    public record WebLookupRequest( string Uri );

}
