namespace Nop.Service.Inventory.EventContracts;

// JSON schemas must match the DTOs consumed by nopCommerce's RabbitMqSubscriber
// (OmnichannelConsumers plugin). Routing key = class name (e.g. "StockReservedEvent").

public class StockReservedEvent
{
    public string SchemaVersion { get; set; } = "v1";
    public Guid EventId { get; set; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; set; } = DateTime.UtcNow;
    public int OrderId { get; set; }
    public List<ReservedLine> Lines { get; set; } = [];

    public class ReservedLine
    {
        public int ProductId { get; set; }
        public int WarehouseId { get; set; }
        public int Quantity { get; set; }
    }
}

public class ReservationRejectedEvent
{
    public string SchemaVersion { get; set; } = "v1";
    public Guid EventId { get; set; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; set; } = DateTime.UtcNow;
    public int OrderId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class StockLevelChangedEvent
{
    public string SchemaVersion { get; set; } = "v1";
    public Guid EventId { get; set; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; set; } = DateTime.UtcNow;
    public int ProductId { get; set; }
    public int WarehouseId { get; set; }
    public int NewStockQuantity { get; set; }
    public int ReservedQuantity { get; set; }
}
