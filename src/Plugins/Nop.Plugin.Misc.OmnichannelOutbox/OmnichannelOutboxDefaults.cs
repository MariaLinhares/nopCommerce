namespace Nop.Plugin.Misc.OmnichannelOutbox;

/// <summary>
/// Represents plugin constants
/// </summary>
public static class OmnichannelOutboxDefaults
{
    public static string SystemName => "Misc.OmnichannelOutbox";

    public static string ExchangeName => "nopcommerce.events";

    public static int MaxAttempts => 5;

    public static int DispatchBatchSize => 100;

    public static class DispatcherTask
    {
        public static string Name => "Outbox Dispatcher (OmnichannelOutbox plugin)";

        public static string Type => "Nop.Plugin.Misc.OmnichannelOutbox.Services.OutboxDispatcherTask, Nop.Plugin.Misc.OmnichannelOutbox";

        public static int Period => 10;
    }

    public static class RetentionTask
    {
        public static string Name => "Outbox Retention (OmnichannelOutbox plugin)";

        public static string Type => "Nop.Plugin.Misc.OmnichannelOutbox.Services.OutboxRetentionTask, Nop.Plugin.Misc.OmnichannelOutbox";

        public static int Period => 86400;
    }
}
