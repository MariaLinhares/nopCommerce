using Nop.Services.Logging;
using Nop.Services.ScheduleTasks;

namespace Nop.Plugin.Misc.OmnichannelOutbox.Services;

/// <summary>
/// Schedule task that polls the outbox table and publishes pending messages to RabbitMQ.
/// Runs every 10 seconds (configurable via ScheduleTask.Seconds in the database).
/// </summary>
public class OutboxDispatcherTask : IScheduleTask
{
    private readonly IOutboxService _outboxService;
    private readonly IRabbitMqPublisher _rabbitMqPublisher;
    private readonly ILogger _logger;

    public OutboxDispatcherTask(
        IOutboxService outboxService,
        IRabbitMqPublisher rabbitMqPublisher,
        ILogger logger)
    {
        _outboxService = outboxService;
        _rabbitMqPublisher = rabbitMqPublisher;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        var pending = await _outboxService.GetPendingAsync(OmnichannelOutboxDefaults.DispatchBatchSize);

        foreach (var message in pending)
        {
            try
            {
                await _rabbitMqPublisher.PublishAsync(
                    exchange: OmnichannelOutboxDefaults.ExchangeName,
                    routingKey: message.MessageType,
                    messageType: message.MessageType,
                    payload: message.Payload,
                    eventId: message.EventId);

                await _outboxService.MarkDispatchedAsync(message.Id);
            }
            catch (Exception ex)
            {
                await _outboxService.IncrementAttemptsAsync(message.Id);
                await _logger.WarningAsync(
                    $"Outbox dispatch failed for message {message.Id} (EventId={message.EventId}, " +
                    $"Type={message.MessageType}, Attempt={message.Attempts + 1})", ex);
            }
        }
    }
}
