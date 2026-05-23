using Nop.Core.Configuration;

namespace Nop.Core.Domain.Configuration;

/// <summary>
/// Settings controlling the Omnichannel Commerce Core outbox + consumer flow.
/// When OutboxEnabled is true, OrderProcessingService skips the synchronous
/// AdjustInventoryAsync call at order-placement time — stock reconciliation
/// happens asynchronously via the Inventory service over RabbitMQ instead.
/// </summary>
public partial class OmnichannelSettings : ISettings
{
    /// <summary>
    /// When true, the order-placement path defers stock decrement to the
    /// asynchronous outbox / Inventory-service round-trip. When false (default),
    /// classic synchronous behaviour is preserved so existing tests and
    /// deployments are unaffected.
    /// </summary>
    public bool OutboxEnabled { get; set; }
}
