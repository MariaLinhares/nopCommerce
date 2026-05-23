using Nop.Core;

namespace Nop.Plugin.Misc.OmnichannelOutbox.Domain;

/// <summary>
/// Represents a transactional outbox message
/// </summary>
public class OutboxMessage : BaseEntity
{
    /// <summary>
    /// Gets or sets the unique event identifier for consumer idempotency
    /// </summary>
    public Guid EventId { get; set; }

    /// <summary>
    /// Gets or sets the event type name (e.g. "OrderPlacedEvent")
    /// </summary>
    public string MessageType { get; set; }

    /// <summary>
    /// Gets or sets the JSON-serialized event payload
    /// </summary>
    public string Payload { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the event occurred
    /// </summary>
    public DateTime OccurredOnUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the message was dispatched to the broker (null if pending)
    /// </summary>
    public DateTime? DispatchedOnUtc { get; set; }

    /// <summary>
    /// Gets or sets the number of dispatch attempts
    /// </summary>
    public int Attempts { get; set; }
}
