using Nop.Services.Logging;
using Nop.Services.Orders;

namespace Nop.Plugin.Misc.OmnichannelConsumers.Services;

public class CompensationService : ICompensationService
{
    private readonly IOrderService _orderService;
    private readonly IOrderProcessingService _orderProcessingService;
    private readonly ILogger _logger;

    public CompensationService(
        IOrderService orderService,
        IOrderProcessingService orderProcessingService,
        ILogger logger)
    {
        _orderService = orderService;
        _orderProcessingService = orderProcessingService;
        _logger = logger;
    }

    public async Task CompensateAsync(int orderId, Guid sourceEventId, string reason)
    {
        var order = await _orderService.GetOrderByIdAsync(orderId);
        if (order is null)
        {
            await _logger.WarningAsync(
                $"Compensation requested for missing OrderId={orderId} (sourceEventId={sourceEventId}). Dropping.");
            return;
        }

        await _orderProcessingService.CompensateOrderAsync(order, sourceEventId, reason);
    }
}
