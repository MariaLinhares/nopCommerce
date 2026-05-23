namespace Nop.Plugin.Misc.OmnichannelConsumers.Services;

public interface IInboxDeduplicationService
{
    /// <summary>
    /// Atomically records that an inbound message has been processed.
    /// Returns true on first sight, false if the event was already processed.
    /// </summary>
    Task<bool> TryMarkProcessedAsync(Guid eventId, string messageType);

    /// <summary>
    /// Deletes processed inbox rows older than the cutoff.
    /// </summary>
    Task DeleteOlderThanAsync(DateTime cutoff);
}
