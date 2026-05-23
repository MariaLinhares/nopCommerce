namespace Nop.Core.Domain.Catalog;

public partial class StockReservedEvent
{
    public string SchemaVersion { get; set; } = "v1";

    public Guid EventId { get; set; }

    public DateTime OccurredOnUtc { get; set; }

    public int OrderId { get; set; }

    public List<ReservedLine> Lines { get; set; } = new();

    public partial class ReservedLine
    {
        public int ProductId { get; set; }
        public int WarehouseId { get; set; }
        public int Quantity { get; set; }
    }
}
