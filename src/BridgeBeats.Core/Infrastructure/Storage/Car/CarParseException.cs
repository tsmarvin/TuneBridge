namespace BridgeBeats.Core.Infrastructure.Storage.Car;

internal sealed class CarParseException : Exception {

    internal CarParseException( string message ) : base( message ) { }

    internal CarParseException( string message, Exception innerException ) : base( message, innerException ) { }
}
