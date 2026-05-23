using System.Text.Json;
using Nop.Core.Domain.Catalog;
using Nop.Services.Logging;

namespace Nop.Plugin.Misc.OmnichannelConsumers.Services.Handlers;

/// <summary>
/// Drives the compensation path: payment release + status transition + customer notification.
/// All orchestration lives in ICompensationService so the handler stays trivial.
/// </summary>
public class ReservationRejectedHandler : IInboxMessageHandler
{
    private readonly ICompensationService _compensationService;
    private readonly ILogger _logger;

    public ReservationRejectedHandler(
        ICompensationService compensationService,
        ILogger logger)
    {
        _compensationService = compensationService;
        _logger = logger;
    }

    public string RoutingKey => OmnichannelConsumersDefaults.RoutingKeys.ReservationRejected;

    public async Task HandleAsync(Guid eventId, string payload)
    {
        var evt = JsonSerializer.Deserialize<ReservationRejectedEvent>(payload);
        if (evt is null)
        {
            await _logger.WarningAsync($"ReservationRejectedHandler: could not deserialize payload for eventId={eventId}");
            return;
        }

        await _compensationService.CompensateAsync(evt.OrderId, eventId, evt.Reason);
    }
}
