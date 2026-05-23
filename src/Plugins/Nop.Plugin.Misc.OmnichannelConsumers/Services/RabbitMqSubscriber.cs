using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Services.Logging;
using RabbitMQ.Client;

namespace Nop.Plugin.Misc.OmnichannelConsumers.Services;

/// <summary>
/// Singleton RabbitMQ subscriber managing one connection + one channel. Declares the
/// monolith.* queues and binds them to the shared topic exchange on first use. Each
/// DrainAsync tick does BasicGet per queue, dispatches to the matching IInboxMessageHandler
/// resolved through a fresh DI scope (handlers are scoped because they touch IRepository),
/// and acks/nacks per outcome.
/// </summary>
public class RabbitMqSubscriber : IRabbitMqSubscriber, IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private IConnection _connection;
    private IChannel _channel;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _topologyDeclared;
    private bool _disposed;

    private static readonly (string Queue, string RoutingKey)[] Bindings =
    {
        (OmnichannelConsumersDefaults.Queues.StockReserved,        OmnichannelConsumersDefaults.RoutingKeys.StockReserved),
        (OmnichannelConsumersDefaults.Queues.ReservationRejected,  OmnichannelConsumersDefaults.RoutingKeys.ReservationRejected),
        (OmnichannelConsumersDefaults.Queues.StockLevelChanged,    OmnichannelConsumersDefaults.RoutingKeys.StockLevelChanged),
        (OmnichannelConsumersDefaults.Queues.ShipmentDispatched,   OmnichannelConsumersDefaults.RoutingKeys.ShipmentDispatched),
    };

    public RabbitMqSubscriber(IConfiguration configuration, IServiceScopeFactory scopeFactory)
    {
        _configuration = configuration;
        _scopeFactory = scopeFactory;
    }

    private async Task EnsureChannelAsync()
    {
        if (_connection is { IsOpen: true } && _channel is { IsOpen: true } && _topologyDeclared)
            return;

        _channel?.Dispose();
        _connection?.Dispose();

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
        _channel = await _connection.CreateChannelAsync();

        // Main exchange (idempotent — outbox plugin also declares it).
        await _channel.ExchangeDeclareAsync(
            exchange: OmnichannelConsumersDefaults.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false);

        // Dead-letter exchange (fanout — any rejected message goes to one DLQ).
        await _channel.ExchangeDeclareAsync(
            exchange: OmnichannelConsumersDefaults.DeadLetterExchange,
            type: ExchangeType.Fanout,
            durable: true,
            autoDelete: false);

        var queueArgs = new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"] = OmnichannelConsumersDefaults.DeadLetterExchange
        };

        foreach (var (queue, routingKey) in Bindings)
        {
            await _channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, arguments: queueArgs);
            await _channel.QueueBindAsync(queue, OmnichannelConsumersDefaults.ExchangeName, routingKey);
        }

        _topologyDeclared = true;
    }

    public async Task<int> DrainAsync(int batchSize)
    {
        await _lock.WaitAsync();
        int processed = 0;
        try
        {
            await EnsureChannelAsync();

            foreach (var (queue, _) in Bindings)
            {
                for (int i = 0; i < batchSize; i++)
                {
                    var result = await _channel.BasicGetAsync(queue, autoAck: false);
                    if (result is null)
                        break;

                    var routingKey = result.RoutingKey;
                    var payload = Encoding.UTF8.GetString(result.Body.Span);
                    var eventIdRaw = result.BasicProperties.MessageId;
                    var eventId = Guid.TryParse(eventIdRaw, out var g) ? g : Guid.NewGuid();

                    using var scope = _scopeFactory.CreateScope();
                    var logger = scope.ServiceProvider.GetRequiredService<ILogger>();
                    var dedup = scope.ServiceProvider.GetRequiredService<IInboxDeduplicationService>();
                    var handler = scope.ServiceProvider
                        .GetServices<IInboxMessageHandler>()
                        .FirstOrDefault(h => h.RoutingKey == routingKey);

                    if (handler is null)
                    {
                        await logger.WarningAsync($"RabbitMqSubscriber: no handler registered for routingKey={routingKey}. Dead-lettering.");
                        await _channel.BasicNackAsync(result.DeliveryTag, multiple: false, requeue: false);
                        continue;
                    }

                    var first = await dedup.TryMarkProcessedAsync(eventId, routingKey);
                    if (!first)
                    {
                        // Already processed — ack and move on.
                        await _channel.BasicAckAsync(result.DeliveryTag, multiple: false);
                        continue;
                    }

                    try
                    {
                        await handler.HandleAsync(eventId, payload);
                        await _channel.BasicAckAsync(result.DeliveryTag, multiple: false);
                        processed++;
                    }
                    catch (Exception ex)
                    {
                        await logger.ErrorAsync($"RabbitMqSubscriber: handler {handler.GetType().Name} failed for eventId={eventId} (routingKey={routingKey}). Dead-lettering.", ex);
                        await _channel.BasicNackAsync(result.DeliveryTag, multiple: false, requeue: false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            using var scope = _scopeFactory.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger>();
            await logger.ErrorAsync("RabbitMqSubscriber: drain failed", ex);
        }
        finally
        {
            _lock.Release();
        }

        return processed;
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
