namespace OrderRouting.Api.Models;

public enum OrderPriority
{
    Standard = 0,
    Rush = 1
}

public sealed record ValidatedOrder(
    string? OrderId,
    string CustomerZip,
    bool MailOrder,
    OrderPriority Priority,
    IReadOnlyList<ValidatedOrderItem> Items);

public sealed record ValidatedOrderItem(
    int RequestIndex,
    string ProductCode,
    int Quantity,
    Product Product);
