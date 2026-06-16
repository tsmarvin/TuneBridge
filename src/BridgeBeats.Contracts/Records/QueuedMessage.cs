using System.Text.Json.Serialization;

namespace BridgeBeats.Contracts.Records;

/// <summary>
/// A generic transport envelope wrapping a dequeued payload together with the broker's
/// message identifier and the time it was enqueued.
/// </summary>
/// <typeparam name="T">The type of the wrapped payload.</typeparam>
/// <param name="MessageId">The broker-assigned identifier for this message, used to acknowledge or requeue it.</param>
/// <param name="Payload">The wrapped payload.</param>
/// <param name="EnqueuedAt">The absolute instant the message was placed on the queue.</param>
public sealed record QueuedMessage<T>(

    [property: JsonPropertyName( "messageId" )]
    string MessageId,

    [property: JsonPropertyName( "payload" )]
    T Payload,

    [property: JsonPropertyName( "enqueuedAt" )]
    DateTimeOffset EnqueuedAt

);
