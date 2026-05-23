namespace Nop.Plugin.Misc.OmnichannelConsumers.Services;

/// <summary>
/// Orchestrates the compensating-transaction path when a downstream system rejects
/// an optimistically-accepted order. Wraps IOrderProcessingService.CompensateOrderAsync
/// with the lookup-and-guard logic the handlers need.
/// </summary>
public interface ICompensationService
{
    Task CompensateAsync(int orderId, Guid sourceEventId, string reason);
}
