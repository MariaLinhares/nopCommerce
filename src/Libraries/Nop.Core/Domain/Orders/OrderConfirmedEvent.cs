namespace Nop.Core.Domain.Orders;

public partial class OrderConfirmedEvent
{
    public OrderConfirmedEvent()
    {
    }

    public OrderConfirmedEvent(int orderId, Guid sourceEventId)
    {
        EventId = Guid.NewGuid();
        OccurredOnUtc = DateTime.UtcNow;
        OrderId = orderId;
        SourceEventId = sourceEventId;
    }

    public string SchemaVersion { get; set; } = "v1";

    public Guid EventId { get; set; }

    public DateTime OccurredOnUtc { get; set; }

    public int OrderId { get; set; }

    public Guid SourceEventId { get; set; }
}
