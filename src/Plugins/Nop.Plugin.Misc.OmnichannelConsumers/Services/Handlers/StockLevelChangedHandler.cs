using System.Text.Json;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;
using Nop.Services.Logging;

namespace Nop.Plugin.Misc.OmnichannelConsumers.Services.Handlers;

/// <summary>
/// Catalog stock projection — writes the Inventory service's authoritative stock value
/// back into the local Product.StockQuantity / ProductWarehouseInventory tables so the
/// storefront's existing GetTotalStockQuantityAsync read path stays unchanged.
/// Idempotent by construction: rewriting the same StockQuantity is a no-op.
/// </summary>
public class StockLevelChangedHandler : IInboxMessageHandler
{
    private readonly IProductService _productService;
    private readonly ILogger _logger;

    public StockLevelChangedHandler(
        IProductService productService,
        ILogger logger)
    {
        _productService = productService;
        _logger = logger;
    }

    public string RoutingKey => OmnichannelConsumersDefaults.RoutingKeys.StockLevelChanged;

    public async Task HandleAsync(Guid eventId, string payload)
    {
        var evt = JsonSerializer.Deserialize<StockLevelChangedEvent>(payload);
        if (evt is null)
        {
            await _logger.WarningAsync($"StockLevelChangedHandler: could not deserialize payload for eventId={eventId}");
            return;
        }

        var product = await _productService.GetProductByIdAsync(evt.ProductId);
        if (product is null)
        {
            await _logger.WarningAsync($"StockLevelChangedHandler: ProductId={evt.ProductId} not found (eventId={eventId})");
            return;
        }

        if (product.UseMultipleWarehouses)
        {
            // Per-warehouse projection: update or insert the corresponding row.
            var warehouses = await _productService.GetAllProductWarehouseInventoryRecordsAsync(product.Id);
            var record = warehouses.FirstOrDefault(w => w.WarehouseId == evt.WarehouseId);
            if (record is not null)
            {
                record.StockQuantity = evt.NewStockQuantity;
                record.ReservedQuantity = evt.ReservedQuantity;
                await _productService.UpdateProductWarehouseInventoryAsync(record);
            }
        }
        else
        {
            product.StockQuantity = evt.NewStockQuantity;
            await _productService.UpdateProductAsync(product);
        }
    }
}
