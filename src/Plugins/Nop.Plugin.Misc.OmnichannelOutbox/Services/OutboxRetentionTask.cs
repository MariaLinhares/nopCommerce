using Nop.Services.ScheduleTasks;

namespace Nop.Plugin.Misc.OmnichannelOutbox.Services;

/// <summary>
/// Schedule task that deletes dispatched outbox messages older than 7 days.
/// Runs daily to prevent unbounded table growth (see Risk R5).
/// </summary>
public class OutboxRetentionTask : IScheduleTask
{
    private readonly IOutboxService _outboxService;

    public OutboxRetentionTask(IOutboxService outboxService)
    {
        _outboxService = outboxService;
    }

    public async Task ExecuteAsync()
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);
        await _outboxService.DeleteDispatchedOlderThanAsync(cutoff);
    }
}
