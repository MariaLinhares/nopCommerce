using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Events;
using Nop.Core.Infrastructure;
using Nop.Plugin.Misc.OmnichannelOutbox.Services;
using Nop.Services.Orders;

namespace Nop.Plugin.Misc.OmnichannelOutbox.Infrastructure;

/// <summary>
/// Plugin DI registration. Runs after core NopStartup (Order=2000) so that
/// IOrderProcessingService and IEventPublisher descriptors exist when we replace them.
/// </summary>
public class NopStartup : INopStartup
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Register plugin's own services
        services.AddScoped<IOutboxService, OutboxService>();
        services.AddSingleton<IRabbitMqPublisher, RabbitMqPublisher>();

        // Decorate IEventPublisher (registered as Singleton by core)
        DecorateEventPublisher(services);

        // Decorate IOrderProcessingService (registered as Scoped by core)
        DecorateOrderProcessingService(services);
    }

    private static void DecorateEventPublisher(IServiceCollection services)
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEventPublisher));
        if (descriptor is null)
            return;

        var implementationType = descriptor.ImplementationType;
        services.Remove(descriptor);

        services.AddSingleton<IEventPublisher>(sp =>
        {
            // Instantiate the original EventPublisher
            var inner = (IEventPublisher)ActivatorUtilities.CreateInstance(sp, implementationType!);
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
            return new OutboxEventPublisherDecorator(inner, scopeFactory);
        });
    }

    private static void DecorateOrderProcessingService(IServiceCollection services)
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOrderProcessingService));
        if (descriptor is null)
            return;

        var implementationType = descriptor.ImplementationType;
        services.Remove(descriptor);

        services.AddScoped<IOrderProcessingService>(sp =>
        {
            // Instantiate the original OrderProcessingService with all its dependencies
            var inner = (IOrderProcessingService)ActivatorUtilities.CreateInstance(sp, implementationType!);
            return new OrderProcessingServiceDecorator(inner);
        });
    }

    public void Configure(IApplicationBuilder application)
    {
    }

    /// <summary>
    /// Must run after core NopStartup (Order=2000) so the original service descriptors exist
    /// </summary>
    public int Order => 3000;
}
