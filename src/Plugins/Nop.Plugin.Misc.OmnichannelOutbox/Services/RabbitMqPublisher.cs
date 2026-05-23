using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Services.Logging;
using RabbitMQ.Client;

namespace Nop.Plugin.Misc.OmnichannelOutbox.Services;

/// <summary>
/// RabbitMQ publisher using publisher confirms for reliable delivery.
/// Registered as singleton — manages its own connection lifecycle.
/// Uses IServiceScopeFactory to resolve scoped ILogger per-call.
/// </summary>
public class RabbitMqPublisher : IRabbitMqPublisher
{
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private IConnection _connection;
    private IChannel _channel;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    public RabbitMqPublisher(IConfiguration configuration, IServiceScopeFactory scopeFactory)
    {
        _configuration = configuration;
        _scopeFactory = scopeFactory;
    }

    private async Task EnsureConnectionAsync()
    {
        if (_connection is { IsOpen: true } && _channel is { IsOpen: true })
            return;

        // Dispose stale connection/channel before creating new ones
        if (_channel is not null)
        {
            _channel.Dispose();
            _channel = null;
        }
        if (_connection is not null)
        {
            _connection.Dispose();
            _connection = null;
        }

        var hostName = _configuration["RabbitMQ:Host"] ?? "localhost";
        var port = int.TryParse(_configuration["RabbitMQ:Port"], out var p) ? p : 5672;
        var userName = _configuration["RabbitMQ:UserName"] ?? "guest";
        var password = _configuration["RabbitMQ:Password"] ?? "guest";

        var factory = new ConnectionFactory
        {
            HostName = hostName,
            Port = port,
            UserName = userName,
            Password = password,
            AutomaticRecoveryEnabled = true
        };

        _connection = await factory.CreateConnectionAsync();
        _channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true));

        // Declare the exchange (idempotent)
        await _channel.ExchangeDeclareAsync(
            exchange: OmnichannelOutboxDefaults.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false);
    }

    public async Task PublishAsync(string exchange, string routingKey, string messageType, string payload, Guid eventId)
    {
        await _lock.WaitAsync();
        try
        {
            await EnsureConnectionAsync();

            var body = Encoding.UTF8.GetBytes(payload);

            var properties = new BasicProperties
            {
                DeliveryMode = DeliveryModes.Persistent,
                ContentType = "application/json",
                MessageId = eventId.ToString(),
                Type = messageType,
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            };

            // In RabbitMQ.Client 7.x with PublisherConfirmationsEnabled,
            // BasicPublishAsync awaits the broker ack before returning
            await _channel.BasicPublishAsync(
                exchange: exchange,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: properties,
                body: body);
        }
        catch (Exception ex)
        {
            // Resolve scoped ILogger via a temporary scope (RabbitMqPublisher is singleton)
            using var scope = _scopeFactory.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger>();
            await logger.ErrorAsync($"Failed to publish message {eventId} of type {messageType} to RabbitMQ", ex);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _channel?.Dispose();
        _connection?.Dispose();
        _lock.Dispose();
    }
}
