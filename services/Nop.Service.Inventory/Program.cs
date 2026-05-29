using Nop.Service.Inventory;
using Nop.Service.Inventory.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient<OpenBoxesClient>(client =>
{
    var baseUrl = builder.Configuration["OpenBoxes:BaseUrl"] ?? "http://localhost:8080";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
