using System.Net.Http.Json;

namespace Nop.Service.Shipping.Services;

// ACL adapter to the carrier API, backed by WireMock in the demo environment.
// POST /api/shipments → { "trackingNumber": "...", "carrier": "..." }
public class CarrierClient
{
    private readonly HttpClient _http;
    private readonly ILogger<CarrierClient> _logger;

    public CarrierClient(HttpClient http, ILogger<CarrierClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<(string TrackingNumber, string Carrier)> BookShipmentAsync(int orderId)
    {
        var response = await _http.PostAsJsonAsync("/api/shipments", new { orderId });
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<BookShipmentResponse>();

        _logger.LogInformation("[Carrier] Booked shipment for order {OrderId}: tracking={Tracking}", orderId, result?.TrackingNumber);
        return (result?.TrackingNumber ?? $"TRACK-{orderId}", result?.Carrier ?? "WireMock Carrier");
    }

    private class BookShipmentResponse
    {
        public string TrackingNumber { get; set; } = string.Empty;
        public string Carrier { get; set; } = string.Empty;
    }
}
