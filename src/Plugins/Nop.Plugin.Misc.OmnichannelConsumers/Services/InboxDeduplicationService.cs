using Nop.Data;
using Nop.Plugin.Misc.OmnichannelConsumers.Domain;

namespace Nop.Plugin.Misc.OmnichannelConsumers.Services;

/// <summary>
/// Idempotency primitive backed by a unique index on ProcessedInboxMessage.EventId.
/// First insert wins; subsequent inserts with the same EventId throw and TryMark returns false.
/// </summary>
public class InboxDeduplicationService : IInboxDeduplicationService
{
    private readonly IRepository<ProcessedInboxMessage> _repository;

    public InboxDeduplicationService(IRepository<ProcessedInboxMessage> repository)
    {
        _repository = repository;
    }

    public async Task<bool> TryMarkProcessedAsync(Guid eventId, string messageType)
    {
        var existing = await _repository.GetAllAsync(q => q.Where(m => m.EventId == eventId));
        if (existing.Any())
            return false;

        try
        {
            await _repository.InsertAsync(new ProcessedInboxMessage
            {
                EventId = eventId,
                MessageType = messageType,
                ProcessedOnUtc = DateTime.UtcNow
            }, publishEvent: false);

            return true;
        }
        catch
        {
            // Unique-index race: another worker won. Treat as already-processed.
            return false;
        }
    }

    public Task DeleteOlderThanAsync(DateTime cutoff)
        => _repository.DeleteAsync(m => m.ProcessedOnUtc < cutoff);
}
