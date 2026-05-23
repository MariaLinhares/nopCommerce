namespace Nop.Core.Domain.Shipping;

public partial class ShipmentDispatchedEvent
{
    public string SchemaVersion { get; set; } = "v1";

    public Guid EventId { get; set; }

    public DateTime OccurredOnUtc { get; set; }

    public int OrderId { get; set; }

    public string TrackingNumber { get; set; } = string.Empty;

    public string Carrier { get; set; } = string.Empty;
}
