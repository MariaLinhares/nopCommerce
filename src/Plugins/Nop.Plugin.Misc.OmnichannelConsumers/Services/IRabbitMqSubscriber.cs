namespace Nop.Plugin.Misc.OmnichannelConsumers.Services;

/// <summary>
/// Polling-style RabbitMQ subscriber. The schedule task calls DrainAsync each tick;
/// it issues BasicGet per queue up to batchSize, ack on handler success, nack-no-requeue
/// (= DLQ) on handler failure. Push consumers via EventingBasicConsumer remain a
/// drop-in alternative if QA-2 latency targets demand it.
/// </summary>
public interface IRabbitMqSubscriber
{
    /// <summary>
    /// Drain a batch from each bound monolith.* queue. Returns the number of messages processed.
    /// </summary>
    Task<int> DrainAsync(int batchSize);
}
