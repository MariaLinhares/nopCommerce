using Nop.Services.ScheduleTasks;

namespace Nop.Plugin.Misc.OmnichannelConsumers.Services;

/// <summary>
/// Daily cleanup of ProcessedInboxMessage rows older than 7 days. Mirrors
/// OutboxRetentionTask in the outbox plugin so both retention horizons match.
/// </summary>
public class InboxRetentionTask : IScheduleTask
{
    private readonly IInboxDeduplicationService _dedup;

    public InboxRetentionTask(IInboxDeduplicationService dedup)
    {
        _dedup = dedup;
    }

    public Task ExecuteAsync()
        => _dedup.DeleteOlderThanAsync(DateTime.UtcNow.AddDays(-7));
}
