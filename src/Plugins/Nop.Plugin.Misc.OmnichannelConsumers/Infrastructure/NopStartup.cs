using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Plugin.Misc.OmnichannelConsumers.Services;
using Nop.Plugin.Misc.OmnichannelConsumers.Services.Handlers;

namespace Nop.Plugin.Misc.OmnichannelConsumers.Infrastructure;

/// <summary>
/// DI registration. Order = 3100 so we run after both core (2000) and Misc.OmnichannelOutbox (3000) —
/// none of our registrations decorate existing services, but staying ordered keeps the
/// startup log easy to read.
/// </summary>
public class NopStartup : INopStartup
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IInboxDeduplicationService, InboxDeduplicationService>();
        services.AddScoped<ICompensationService, CompensationService>();

        // One handler per inbound routing key — registered as a set so the subscriber
        // can resolve by RoutingKey property.
        services.AddScoped<IInboxMessageHandler, StockReservedHandler>();
        services.AddScoped<IInboxMessageHandler, ReservationRejectedHandler>();
        services.AddScoped<IInboxMessageHandler, StockLevelChangedHandler>();
        services.AddScoped<IInboxMessageHandler, ShipmentDispatchedHandler>();

        // Subscriber owns a connection — singleton.
        services.AddSingleton<IRabbitMqSubscriber, RabbitMqSubscriber>();
    }

    public void Configure(IApplicationBuilder application)
    {
    }

    public int Order => 3100;
}
