using Nop.Core;

namespace Nop.Plugin.Misc.OmnichannelConsumers.Domain;

/// <summary>
/// Deduplication record for messages consumed from RabbitMQ. The EventId column has a
/// unique index — a duplicate insert is the idempotency primitive.
/// </summary>
public class ProcessedInboxMessage : BaseEntity
{
    public Guid EventId { get; set; }

    public string MessageType { get; set; }

    public DateTime ProcessedOnUtc { get; set; }
}
