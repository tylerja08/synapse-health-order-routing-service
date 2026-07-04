using System.Text.Json.Serialization;

namespace OrderRouting.Api.Models;

public sealed record RouteOrderResponse(
    [property: JsonPropertyName("feasible")] bool Feasible,
    [property: JsonPropertyName("routing")] IReadOnlyList<RouteSupplierResponse>? Routing = null,
    [property: JsonPropertyName("errors")] IReadOnlyList<string>? Errors = null)
{
    public static RouteOrderResponse Success(IReadOnlyList<RouteSupplierResponse> routing) => new(true, routing, null);

    public static RouteOrderResponse Failure(IReadOnlyList<string> errors) => new(false, null, errors);
}

public sealed record RouteSupplierResponse(
    [property: JsonPropertyName("supplier_id")] string SupplierId,
    [property: JsonPropertyName("supplier_name")] string SupplierName,
    [property: JsonPropertyName("items")] IReadOnlyList<RouteItemResponse> Items);

public sealed record RouteItemResponse(
    [property: JsonPropertyName("product_code")] string ProductCode,
    [property: JsonPropertyName("quantity")] int Quantity,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("fulfillment_mode")] string FulfillmentMode);
