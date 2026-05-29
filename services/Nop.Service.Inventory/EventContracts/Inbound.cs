using System.Text.Json.Serialization;

namespace Nop.Service.Inventory.EventContracts;

// Matches the JSON written by nopCommerce's OutboxEventPublisherDecorator
// when it serializes OrderPlacedEvent. The Order property mirrors the nopCommerce
// Order entity — only the fields this service actually reads are mapped.
public class OrderPlacedPayload
{
    [JsonPropertyName("Order")]
    public OrderDto? Order { get; set; }

    public class OrderDto
    {
        [JsonPropertyName("Id")]
        public int Id { get; set; }

        [JsonPropertyName("OrderItems")]
        public List<OrderItemDto> OrderItems { get; set; } = [];
    }

    public class OrderItemDto
    {
        [JsonPropertyName("ProductId")]
        public int ProductId { get; set; }

        [JsonPropertyName("Quantity")]
        public int Quantity { get; set; }
    }
}
