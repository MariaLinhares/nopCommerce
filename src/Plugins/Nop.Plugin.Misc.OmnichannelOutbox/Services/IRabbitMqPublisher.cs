namespace Nop.Plugin.Misc.OmnichannelOutbox.Services;

/// <summary>
/// Abstraction over the RabbitMQ broker for publishing outbox messages
/// </summary>
public interface IRabbitMqPublisher : IDisposable
{
    /// <summary>
    /// Publishes a message to the configured exchange with publisher confirms
    /// </summary>
    Task PublishAsync(string exchange, string routingKey, string messageType, string payload, Guid eventId);
}
