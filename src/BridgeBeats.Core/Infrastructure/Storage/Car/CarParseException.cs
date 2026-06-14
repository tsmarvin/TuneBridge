namespace BridgeBeats.Core.Infrastructure.Storage.Car;

/// <summary>
/// The single failure type raised by every layer of the CAR/CBOR/MST parser
/// (<see cref="Varint"/>, <see cref="Cid"/>, <see cref="CarV1Reader"/>,
/// <see cref="DagCborConverter"/>, <see cref="MstWalker"/>, and <see cref="CarRepoReader"/>).
/// </summary>
/// <remarks>
/// A single exception type lets callers wrap repo parsing in one catch clause and treat any
/// structural problem, integrity-check failure, or breached safety cap uniformly. Lower-level
/// CBOR decoding faults (for example <see cref="System.Formats.Cbor.CborContentException"/>)
/// are caught and re-thrown wrapped in this type with the original set as the inner exception.
/// </remarks>
internal sealed class CarParseException : Exception {

    /// <summary>
    /// Initializes a new instance with a message describing the parse failure.
    /// </summary>
    /// <param name="message">A description of what went wrong while parsing.</param>
    internal CarParseException( string message ) : base( message ) { }

    /// <summary>
    /// Initializes a new instance with a message and the underlying exception that caused the failure.
    /// </summary>
    /// <param name="message">A description of what went wrong while parsing.</param>
    /// <param name="innerException">The lower-level exception (for example a CBOR decoding error) that triggered this failure.</param>
    internal CarParseException( string message, Exception innerException ) : base( message, innerException ) { }
}
