using System.Text.Json;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Services.Logging;
using Nop.Services.Orders;

namespace Nop.Plugin.Misc.OmnichannelConsumers.Services.Handlers;

/// <summary>
/// Records that the Shipping service has handed the parcel to the carrier. We keep the
/// monolith's Shipment entity untouched (no carrier integration on this side) and just
/// add an order note carrying tracking metadata for the operator audit trail.
/// </summary>
public class ShipmentDispatchedHandler : IInboxMessageHandler
{
    private readonly IOrderService _orderService;
    private readonly ILogger _logger;

    public ShipmentDispatchedHandler(IOrderService orderService, ILogger logger)
    {
        _orderService = orderService;
        _logger = logger;
    }

    public string RoutingKey => OmnichannelConsumersDefaults.RoutingKeys.ShipmentDispatched;

    public async Task HandleAsync(Guid eventId, string payload)
    {
        var evt = JsonSerializer.Deserialize<ShipmentDispatchedEvent>(payload);
        if (evt is null)
        {
            await _logger.WarningAsync($"ShipmentDispatchedHandler: could not deserialize payload for eventId={eventId}");
            return;
        }

        var order = await _orderService.GetOrderByIdAsync(evt.OrderId);
        if (order is null)
        {
            await _logger.WarningAsync($"ShipmentDispatchedHandler: OrderId={evt.OrderId} not found (eventId={eventId})");
            return;
        }

        await _orderService.InsertOrderNoteAsync(new OrderNote
        {
            OrderId = order.Id,
            Note = $"Shipment dispatched by carrier {evt.Carrier}. tracking={evt.TrackingNumber}, eventId={eventId:D}, occurredOnUtc={evt.OccurredOnUtc:O}",
            DisplayToCustomer = true,
            CreatedOnUtc = DateTime.UtcNow
        });
    }
}
