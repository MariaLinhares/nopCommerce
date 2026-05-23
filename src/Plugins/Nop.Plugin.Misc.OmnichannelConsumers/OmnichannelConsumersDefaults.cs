namespace Nop.Plugin.Misc.OmnichannelConsumers;

/// <summary>
/// Constants for the consumers plugin: queue names, routing keys, schedule-task metadata.
/// Exchange name is shared with Misc.OmnichannelOutbox (OmnichannelOutboxDefaults.ExchangeName);
/// we re-declare it as a string literal here to keep the consumers plugin compilable
/// independently of the outbox plugin's internal constants.
/// </summary>
public static class OmnichannelConsumersDefaults
{
    public static string SystemName => "Misc.OmnichannelConsumers";

    /// <summary>Topic exchange shared with the outbox dispatcher. Must match.</summary>
    public static string ExchangeName => "nopcommerce.events";

    /// <summary>DLX where messages dead-letter on handler failure / parse error.</summary>
    public static string DeadLetterExchange => "nopcommerce.events.dlq";

    public static int SubscriberBatchSize => 50;

    public static class SubscriberTask
    {
        public static string Name => "Omnichannel RabbitMQ Subscriber (OmnichannelConsumers plugin)";

        public static string Type => "Nop.Plugin.Misc.OmnichannelConsumers.Services.RabbitMqSubscriberTask, Nop.Plugin.Misc.OmnichannelConsumers";

        public static int Period => 5;
    }

    public static class InboxRetentionTask
    {
        public static string Name => "Inbox Retention (OmnichannelConsumers plugin)";

        public static string Type => "Nop.Plugin.Misc.OmnichannelConsumers.Services.InboxRetentionTask, Nop.Plugin.Misc.OmnichannelConsumers";

        public static int Period => 86400;
    }

    /// <summary>
    /// Queue/routing-key bindings. Each inbound event class name is both the routing key
    /// (set by RabbitMqPublisher in the outbox plugin) and the discriminator the
    /// subscriber uses to dispatch to the right handler.
    /// </summary>
    public static class Queues
    {
        public const string StockReserved = "monolith.stock-reserved";
        public const string ReservationRejected = "monolith.reservation-rejected";
        public const string StockLevelChanged = "monolith.stock-level-changed";
        public const string ShipmentDispatched = "monolith.shipment-dispatched";
    }

    public static class RoutingKeys
    {
        public const string StockReserved = "StockReservedEvent";
        public const string ReservationRejected = "ReservationRejectedEvent";
        public const string StockLevelChanged = "StockLevelChangedEvent";
        public const string ShipmentDispatched = "ShipmentDispatchedEvent";
    }
}
