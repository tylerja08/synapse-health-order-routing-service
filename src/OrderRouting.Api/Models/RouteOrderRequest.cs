using System.Text.Json.Serialization;

namespace OrderRouting.Api.Models;

public sealed record RouteOrderRequest(
    [property: JsonPropertyName("order_id")] string? OrderId,
    [property: JsonPropertyName("customer_zip")] string? CustomerZip,
    [property: JsonPropertyName("mail_order")] bool MailOrder,
    [property: JsonPropertyName("priority")] string? Priority,
    [property: JsonPropertyName("items")] IReadOnlyList<RouteOrderItemRequest>? Items);

public sealed record RouteOrderItemRequest(
    [property: JsonPropertyName("product_code")] string? ProductCode,
    [property: JsonPropertyName("quantity")] int Quantity);
