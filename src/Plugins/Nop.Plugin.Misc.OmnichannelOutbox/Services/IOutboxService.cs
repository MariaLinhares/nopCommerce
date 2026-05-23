using Nop.Plugin.Misc.OmnichannelOutbox.Domain;

namespace Nop.Plugin.Misc.OmnichannelOutbox.Services;

/// <summary>
/// Outbox service interface
/// </summary>
public interface IOutboxService
{
    /// <summary>
    /// Writes a new outbox message
    /// </summary>
    Task WriteAsync(string messageType, string payload, Guid eventId);

    /// <summary>
    /// Gets pending (undispatched) messages up to batchSize, ordered by occurrence time
    /// </summary>
    Task<IList<OutboxMessage>> GetPendingAsync(int batchSize);

    /// <summary>
    /// Marks a message as dispatched
    /// </summary>
    Task MarkDispatchedAsync(int id);

    /// <summary>
    /// Increments the attempt count for a failed dispatch
    /// </summary>
    Task IncrementAttemptsAsync(int id);

    /// <summary>
    /// Deletes dispatched messages older than the given cutoff
    /// </summary>
    Task DeleteDispatchedOlderThanAsync(DateTime cutoff);
}
