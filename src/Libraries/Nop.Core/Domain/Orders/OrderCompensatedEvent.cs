namespace Nop.Core.Domain.Orders;

public partial class OrderCompensatedEvent
{
    public OrderCompensatedEvent()
    {
    }

    public OrderCompensatedEvent(int orderId, Guid sourceEventId, string reason)
    {
        EventId = Guid.NewGuid();
        OccurredOnUtc = DateTime.UtcNow;
        OrderId = orderId;
        SourceEventId = sourceEventId;
        Reason = reason;
    }

    public string SchemaVersion { get; set; } = "v1";

    public Guid EventId { get; set; }

    public DateTime OccurredOnUtc { get; set; }

    public int OrderId { get; set; }

    public Guid SourceEventId { get; set; }

    public string Reason { get; set; } = string.Empty;
}
