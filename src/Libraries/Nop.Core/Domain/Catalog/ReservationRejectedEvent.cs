namespace Nop.Core.Domain.Catalog;

public partial class ReservationRejectedEvent
{
    public string SchemaVersion { get; set; } = "v1";

    public Guid EventId { get; set; }

    public DateTime OccurredOnUtc { get; set; }

    public int OrderId { get; set; }

    public string Reason { get; set; } = string.Empty;
}
