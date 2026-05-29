using Nop.Service.Shipping;
using Nop.Service.Shipping.Data;
using Nop.Service.Shipping.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient<CarrierClient>(client =>
{
    var baseUrl = builder.Configuration["Carrier:BaseUrl"] ?? "http://localhost:8080";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});

var dbPath = builder.Configuration["DeduplicationDb"] ?? "/app/data/dedup.db";
builder.Services.AddSingleton(new DeduplicationStore(dbPath));

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
