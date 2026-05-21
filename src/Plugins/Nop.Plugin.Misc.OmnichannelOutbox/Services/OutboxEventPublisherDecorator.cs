using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Domain.Orders;
using Nop.Core.Events;

namespace Nop.Plugin.Misc.OmnichannelOutbox.Services;

/// <summary>
/// Wraps IEventPublisher to intercept whitelisted cross-context events and write them
/// to the outbox table instead of publishing directly. Non-whitelisted events pass through
/// to the original publisher unchanged.
///
/// This decorator is registered as singleton (matching the original EventPublisher lifetime).
/// Scoped services (IOutboxService) are resolved via IServiceScopeFactory per-call.
/// </summary>
public class OutboxEventPublisherDecorator : IEventPublisher
{
    private readonly IEventPublisher _inner;
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// When true (set by OrderProcessingServiceDecorator during PlaceOrderAsync),
    /// whitelisted events are written to the outbox instead of published directly.
    /// Uses AsyncLocal to flow correctly across async/await boundaries.
    /// </summary>
    internal static readonly AsyncLocal<bool> OutboxScope = new();

    /// <summary>
    /// Event types that should be routed to the outbox when inside an outbox scope.
    /// All other events pass through to in-process consumers unchanged.
    /// </summary>
    private static readonly HashSet<Type> WhitelistedTypes = new()
    {
        typeof(OrderPlacedEvent),
        // Person 2 will add: typeof(OrderConfirmedEvent), typeof(StockReservedEvent), etc.
    };

    public OutboxEventPublisherDecorator(IEventPublisher inner, IServiceScopeFactory scopeFactory)
    {
        _inner = inner;
        _scopeFactory = scopeFactory;
    }

    public async Task PublishAsync<TEvent>(TEvent @event)
    {
        if (OutboxScope.Value && WhitelistedTypes.Contains(typeof(TEvent)))
        {
            // Resolve scoped IOutboxService via a new scope.
            // This is necessary because the decorator is singleton but IOutboxService
            // (and its IRepository dependency) are scoped.
            using var scope = _scopeFactory.CreateScope();
            var outboxService = scope.ServiceProvider.GetRequiredService<IOutboxService>();

            var messageType = typeof(TEvent).Name;
            var payload = JsonSerializer.Serialize(@event);
            var eventId = Guid.NewGuid();

            await outboxService.WriteAsync(messageType, payload, eventId);

            // Also call in-process consumers so monolith-side handlers
            // (e.g., order confirmation email) still fire synchronously
            await _inner.PublishAsync(@event);
            return;
        }

        // Outside outbox scope or non-whitelisted: pass through unchanged
        await _inner.PublishAsync(@event);
    }
}
