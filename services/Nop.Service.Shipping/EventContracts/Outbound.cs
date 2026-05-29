namespace Nop.Service.Shipping.EventContracts;

// Must match the DTO consumed by nopCommerce's ShipmentDispatchedHandler.
public class ShipmentDispatchedEvent
{
    public string SchemaVersion { get; set; } = "v1";
    public Guid EventId { get; set; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; set; } = DateTime.UtcNow;
    public int OrderId { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public string Carrier { get; set; } = string.Empty;
}
