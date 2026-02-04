using System.Text.Json.Serialization;

namespace BridgeBeats.Contracts.Records;

/// <summary>
/// A wrapper for messages retrieved from the queue.
/// </summary>
/// <typeparam name="T">The type of the payload.</typeparam>
/// <param name="MessageId">The unique identifier assigned by the queue system.</param>
/// <param name="Payload">The original request payload.</param>
/// <param name="EnqueuedAt">When the message was added to the queue.</param>
public sealed record QueuedMessage<T>(

    [property: JsonPropertyName( "messageId" )]
    string MessageId,

    [property: JsonPropertyName( "payload" )]
    T Payload,

    [property: JsonPropertyName( "enqueuedAt" )]
    DateTimeOffset EnqueuedAt

);
