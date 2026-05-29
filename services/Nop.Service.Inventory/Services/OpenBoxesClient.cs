using System.Net.Http.Json;
using Nop.Service.Inventory.EventContracts;

namespace Nop.Service.Inventory.Services;

// ACL adapter to OpenBoxes WMS.
// In production this would call the real OpenBoxes REST API.
// For demo: calls the WireMock stub which always returns available=true unless
// OPENBOXES_FORCE_REJECT=true is set (used to demonstrate the rejection saga).
public class OpenBoxesClient
{
    private readonly HttpClient _http;
    private readonly ILogger<OpenBoxesClient> _logger;
    private readonly bool _forceReject;

    public OpenBoxesClient(HttpClient http, IConfiguration config, ILogger<OpenBoxesClient> logger)
    {
        _http = http;
        _logger = logger;
        _forceReject = bool.TryParse(config["OpenBoxes:ForceReject"], out var v) && v;
    }

    // Returns (available, lines) where lines carry the warehouse assignments.
    // WarehouseId=1 is the default warehouse for the demo environment.
    public async Task<(bool Available, List<OrderPlacedPayload.OrderItemDto> Lines)> CheckStockAsync(
        int orderId,
        List<OrderPlacedPayload.OrderItemDto> requestedLines)
    {
        if (_forceReject)
        {
            _logger.LogInformation("[OpenBoxes] ForceReject=true — rejecting order {OrderId}", orderId);
            return (false, []);
        }

        try
        {
            // POST /api/stockCheck — WireMock returns {"available": true}
            var response = await _http.PostAsJsonAsync("/api/stockCheck", new { orderId, lines = requestedLines });
            response.EnsureSuccessStatusCode();
            _logger.LogInformation("[OpenBoxes] Stock available for order {OrderId}", orderId);
            return (true, requestedLines);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OpenBoxes] Stock check failed for order {OrderId} — rejecting", orderId);
            return (false, []);
        }
    }
}
