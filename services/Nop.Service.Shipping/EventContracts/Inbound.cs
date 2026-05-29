namespace Nop.Service.Shipping.EventContracts;

// Matches the JSON published by nopCommerce's OutboxEventPublisherDecorator
// when it serializes OrderConfirmedEvent (routed via the outbox from StockReservedHandler).
public class OrderConfirmedPayload
{
    public string SchemaVersion { get; set; } = "v1";
    public Guid EventId { get; set; }
    public DateTime OccurredOnUtc { get; set; }
    public int OrderId { get; set; }
    public Guid SourceEventId { get; set; }
}
