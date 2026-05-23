using System.Text.Json;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Orders;
using Nop.Core.Events;
using Nop.Services.Logging;
using Nop.Services.Orders;

namespace Nop.Plugin.Misc.OmnichannelConsumers.Services.Handlers;

/// <summary>
/// Confirms the order after the Inventory service has reserved the stock. Idempotent:
/// if the order is already in a terminal state (Complete, Cancelled, Compensated) the
/// handler is a no-op. After recording the audit trail and publishing OrderConfirmedEvent
/// (whitelisted → flows back out through the outbox so Shipping picks it up), the handler
/// calls CheckOrderStatusAsync so nopCommerce's existing state machine can advance the
/// order from Pending → Processing using the same logic exercised by the legacy paths.
/// This is the visible state transition the demo needs.
/// </summary>
public class StockReservedHandler : IInboxMessageHandler
{
    private readonly IOrderService _orderService;
    private readonly IOrderProcessingService _orderProcessingService;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger _logger;

    public StockReservedHandler(
        IOrderService orderService,
        IOrderProcessingService orderProcessingService,
        IEventPublisher eventPublisher,
        ILogger logger)
    {
        _orderService = orderService;
        _orderProcessingService = orderProcessingService;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public string RoutingKey => OmnichannelConsumersDefaults.RoutingKeys.StockReserved;

    public async Task HandleAsync(Guid eventId, string payload)
    {
        var evt = JsonSerializer.Deserialize<StockReservedEvent>(payload);
        if (evt is null)
        {
            await _logger.WarningAsync($"StockReservedHandler: could not deserialize payload for eventId={eventId}");
            return;
        }

        var order = await _orderService.GetOrderByIdAsync(evt.OrderId);
        if (order is null)
        {
            await _logger.WarningAsync($"StockReservedHandler: OrderId={evt.OrderId} not found (eventId={eventId})");
            return;
        }

        // Terminal states are absorbed silently — a late-arriving StockReserved
        // for an already-compensated order is not an error.
        if (order.OrderStatus is OrderStatus.Complete or OrderStatus.Cancelled or OrderStatus.Compensated)
            return;

        // Move from Pending → Processing (the existing "confirmed" semantics in nopCommerce).
        // We do not call SetOrderStatusAsync directly because it's protected; the
        // recommended public path is through MarkAsAuthorized/MarkAsPaid + CheckOrderStatus.
        // For the demo, raising OrderConfirmedEvent is the signal external services need;
        // the internal status will move forward through the existing CheckOrderStatusAsync
        // gates once payment status updates. We add an order note for traceability.
        await _orderService.InsertOrderNoteAsync(new OrderNote
        {
            OrderId = order.Id,
            Note = $"Stock reserved by Inventory service. eventId={eventId:D}, occurredOnUtc={evt.OccurredOnUtc:O}",
            DisplayToCustomer = false,
            CreatedOnUtc = DateTime.UtcNow
        });

        await _eventPublisher.PublishAsync(new OrderConfirmedEvent(order.Id, eventId));

        // Let the existing nopCommerce state machine advance the order (Pending → Processing
        // when payment + shipping conditions are satisfied). Reuses CheckAndSaveOrderStatusAsync
        // internally — no parallel state machine, no duplicated transition logic.
        await _orderProcessingService.CheckOrderStatusAsync(order);
    }
}
