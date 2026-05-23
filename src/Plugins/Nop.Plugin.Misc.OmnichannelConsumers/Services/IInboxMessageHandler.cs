namespace Nop.Plugin.Misc.OmnichannelConsumers.Services;

/// <summary>
/// Handler for a single inbound RabbitMQ message type. Implementations are registered
/// by routing key (event class name) in OmnichannelConsumersDefaults.RoutingKeys.
/// </summary>
public interface IInboxMessageHandler
{
    /// <summary>
    /// Routing key / event-type name this handler processes.
    /// </summary>
    string RoutingKey { get; }

    /// <summary>
    /// Process the inbound message. Implementations MUST be idempotent —
    /// the subscriber may redeliver after a crash.
    /// </summary>
    /// <param name="eventId">Idempotency key from the message header (MessageId).</param>
    /// <param name="payload">Raw JSON body.</param>
    Task HandleAsync(Guid eventId, string payload);
}
