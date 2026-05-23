namespace Nop.Core.Domain.Catalog;

public partial class StockLevelChangedEvent
{
    public string SchemaVersion { get; set; } = "v1";

    public Guid EventId { get; set; }

    public DateTime OccurredOnUtc { get; set; }

    public int ProductId { get; set; }

    public int WarehouseId { get; set; }

    public int NewStockQuantity { get; set; }

    public int ReservedQuantity { get; set; }
}
