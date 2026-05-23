using Nop.Services.ScheduleTasks;

namespace Nop.Plugin.Misc.OmnichannelConsumers.Services;

/// <summary>
/// Schedule task wrapping the subscriber drain loop. Cadence is owned by the
/// ScheduleTask row in the DB (default 5s — see OmnichannelConsumersDefaults).
/// </summary>
public class RabbitMqSubscriberTask : IScheduleTask
{
    private readonly IRabbitMqSubscriber _subscriber;

    public RabbitMqSubscriberTask(IRabbitMqSubscriber subscriber)
    {
        _subscriber = subscriber;
    }

    public Task ExecuteAsync()
        => _subscriber.DrainAsync(OmnichannelConsumersDefaults.SubscriberBatchSize);
}
