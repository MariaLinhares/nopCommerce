using System.Text;
using System.Text.Json;
using Nop.Service.Inventory.Data;
using Nop.Service.Inventory.EventContracts;
using Nop.Service.Inventory.Services;
using RabbitMQ.Client;

namespace Nop.Service.Inventory;

public class Worker : BackgroundService
{
    private const string Exchange = "nopcommerce.events";
    private const string DeadLetterExchange = "nopcommerce.events.dlq";
    private const string InboundQueue = "inventory.order-placed";
    private const string InboundRoutingKey = "OrderPlacedEvent";

    private readonly IConfiguration _config;
    private readonly ILogger<Worker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DeduplicationStore _dedup;

    private IConnection? _connection;
    private IChannel? _channel;

    public Worker(IConfiguration config, ILogger<Worker> logger, IServiceScopeFactory scopeFactory, DeduplicationStore dedup)
    {
        _config = config;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _dedup = dedup;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ConnectWithRetryAsync(stoppingToken);

        _logger.LogInformation("Inventory service ready. Polling queue '{Queue}'", InboundQueue);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainQueueAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error draining queue — will retry after delay");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                await ConnectWithRetryAsync(stoppingToken);
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task DrainQueueAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var msg = await _channel!.BasicGetAsync(InboundQueue, autoAck: false, ct);
            if (msg is null) break;

            var messageId = msg.BasicProperties.MessageId ?? Guid.NewGuid().ToString();
            var body = Encoding.UTF8.GetString(msg.Body.Span);

            if (!_dedup.TryMarkProcessed(messageId))
            {
                _logger.LogDebug("Duplicate message {MessageId} — acking silently", messageId);
                await _channel.BasicAckAsync(msg.DeliveryTag, false, ct);
                continue;
            }

            try
            {
                await HandleOrderPlacedAsync(body, messageId, ct);
                await _channel.BasicAckAsync(msg.DeliveryTag, false, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle message {MessageId} — sending to DLQ", messageId);
                await _channel.BasicNackAsync(msg.DeliveryTag, false, requeue: false, ct);
            }
        }
    }

    private async Task HandleOrderPlacedAsync(string body, string messageId, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<OrderPlacedPayload>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var orderId = payload?.Order?.Id ?? 0;
        var lines = payload?.Order?.OrderItems ?? [];

        _logger.LogInformation("Processing OrderPlaced: orderId={OrderId}, lines={LineCount}", orderId, lines.Count);

        using var scope = _scopeFactory.CreateScope();
        var openBoxes = scope.ServiceProvider.GetRequiredService<OpenBoxesClient>();
        var (available, reservedLines) = await openBoxes.CheckStockAsync(orderId, lines);

        if (available)
        {
            await PublishAsync(new StockReservedEvent
            {
                OrderId = orderId,
                Lines = reservedLines.Select(l => new StockReservedEvent.ReservedLine
                {
                    ProductId = l.ProductId,
                    WarehouseId = 1,
                    Quantity = l.Quantity
                }).ToList()
            }, "StockReservedEvent", ct);

            // Publish StockLevelChanged for each reserved product so nopCommerce
            // can project the updated stock quantities.
            foreach (var line in reservedLines)
            {
                await PublishAsync(new StockLevelChangedEvent
                {
                    ProductId = line.ProductId,
                    WarehouseId = 1,
                    NewStockQuantity = 99,
                    ReservedQuantity = line.Quantity
                }, "StockLevelChangedEvent", ct);
            }

            _logger.LogInformation("Published StockReservedEvent for order {OrderId}", orderId);
        }
        else
        {
            await PublishAsync(new ReservationRejectedEvent
            {
                OrderId = orderId,
                Reason = "Insufficient stock in warehouse"
            }, "ReservationRejectedEvent", ct);

            _logger.LogWarning("Published ReservationRejectedEvent for order {OrderId}", orderId);
        }
    }

    private async Task PublishAsync<T>(T payload, string routingKey, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        var eventId = Guid.NewGuid();

        var props = new BasicProperties
        {
            DeliveryMode = DeliveryModes.Persistent,
            ContentType = "application/json",
            MessageId = eventId.ToString(),
            Type = routingKey,
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        };

        await _channel!.BasicPublishAsync(
            exchange: Exchange,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: props,
            body: body,
            cancellationToken: ct);
    }

    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        var host = _config["RabbitMQ:Host"] ?? "localhost";
        var port = int.TryParse(_config["RabbitMQ:Port"], out var p) ? p : 5672;
        var user = _config["RabbitMQ:UserName"] ?? "guest";
        var pass = _config["RabbitMQ:Password"] ?? "guest";

        var factory = new ConnectionFactory
        {
            HostName = host,
            Port = port,
            UserName = user,
            Password = pass,
            AutomaticRecoveryEnabled = true
        };

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                _connection?.Dispose();
                _connection = await factory.CreateConnectionAsync(ct);
                _channel = await _connection.CreateChannelAsync(
                    new CreateChannelOptions(publisherConfirmationsEnabled: true,
                        publisherConfirmationTrackingEnabled: true), ct);

                // Topology — idempotent declarations
                await _channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
                await _channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: ct);

                var queueArgs = new Dictionary<string, object?> { ["x-dead-letter-exchange"] = DeadLetterExchange };
                await _channel.QueueDeclareAsync(InboundQueue, durable: true, exclusive: false, autoDelete: false, arguments: queueArgs, cancellationToken: ct);
                await _channel.QueueBindAsync(InboundQueue, Exchange, InboundRoutingKey, cancellationToken: ct);

                _logger.LogInformation("Connected to RabbitMQ at {Host}:{Port}", host, port);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("RabbitMQ connection attempt {Attempt} failed: {Message}. Retrying in 5s...", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}
