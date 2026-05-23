using Nop.Data;
using Nop.Plugin.Misc.OmnichannelOutbox.Domain;

namespace Nop.Plugin.Misc.OmnichannelOutbox.Services;

/// <summary>
/// Outbox service implementation using IRepository
/// </summary>
public class OutboxService : IOutboxService
{
    private readonly IRepository<OutboxMessage> _repository;

    public OutboxService(IRepository<OutboxMessage> repository)
    {
        _repository = repository;
    }

    public async Task WriteAsync(string messageType, string payload, Guid eventId)
    {
        var message = new OutboxMessage
        {
            EventId = eventId,
            MessageType = messageType,
            Payload = payload,
            OccurredOnUtc = DateTime.UtcNow,
            DispatchedOnUtc = null,
            Attempts = 0
        };

        // publishEvent: false to avoid recursion — inserting an outbox row
        // must not trigger EntityInsertedEvent through the outbox again
        await _repository.InsertAsync(message, publishEvent: false);
    }

    public async Task<IList<OutboxMessage>> GetPendingAsync(int batchSize)
    {
        var messages = await _repository.GetAllAsync(query =>
        {
            query = query.Where(m => m.DispatchedOnUtc == null
                                     && m.Attempts < OmnichannelOutboxDefaults.MaxAttempts);
            query = query.OrderBy(m => m.OccurredOnUtc);
            query = query.Take(batchSize);
            return query;
        });

        return messages;
    }

    public async Task MarkDispatchedAsync(int id)
    {
        var message = await _repository.GetByIdAsync(id);
        if (message is null)
            return;

        message.DispatchedOnUtc = DateTime.UtcNow;
        await _repository.UpdateAsync(message, publishEvent: false);
    }

    public async Task IncrementAttemptsAsync(int id)
    {
        var message = await _repository.GetByIdAsync(id);
        if (message is null)
            return;

        message.Attempts++;
        await _repository.UpdateAsync(message, publishEvent: false);
    }

    public async Task DeleteDispatchedOlderThanAsync(DateTime cutoff)
    {
        await _repository.DeleteAsync(m => m.DispatchedOnUtc != null && m.DispatchedOnUtc < cutoff);
    }
}
