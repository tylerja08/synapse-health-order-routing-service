using System.Text.RegularExpressions;
using OrderRouting.Api.Data;
using OrderRouting.Api.Models;

namespace OrderRouting.Api.Routing;

public sealed class OrderValidator
{
    private static readonly Regex FiveDigitZip = new("^[0-9]{5}$", RegexOptions.Compiled);
    private readonly CsvProductRepository _products;

    public OrderValidator(CsvProductRepository products)
    {
        _products = products;
    }

    public OrderValidationResult Validate(RouteOrderRequest? request)
    {
        if (request is null)
        {
            return OrderValidationResult.Invalid(["Request body is required."]);
        }

        var errors = new List<string>();
        var zip = request.CustomerZip?.Trim() ?? string.Empty;
        if (!FiveDigitZip.IsMatch(zip))
        {
            errors.Add("Order must include a valid customer_zip.");
        }

        var priority = ParsePriority(request.Priority, errors);

        if (request.Items is null || request.Items.Count == 0)
        {
            errors.Add("Order must include at least one line item.");
        }

        var validatedItems = new List<ValidatedOrderItem>();
        if (request.Items is not null)
        {
            for (var index = 0; index < request.Items.Count; index++)
            {
                var item = request.Items[index];
                var code = item.ProductCode?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(code))
                {
                    errors.Add($"Line item {index + 1} must include a product_code.");
                    continue;
                }

                var product = _products.Find(code);
                if (product is null)
                {
                    errors.Add($"Unknown product_code '{code}' in line item {index + 1}.");
                    continue;
                }

                if (item.Quantity <= 0)
                {
                    errors.Add($"Line item {index + 1} for product {code} must include a positive quantity.");
                    continue;
                }

                validatedItems.Add(new ValidatedOrderItem(index, code, item.Quantity, product));
            }
        }

        if (errors.Count > 0)
        {
            return OrderValidationResult.Invalid(errors);
        }

        return OrderValidationResult.Valid(new ValidatedOrder(request.OrderId?.Trim(), zip, request.MailOrder, priority, validatedItems));
    }

    private static OrderPriority ParsePriority(string? value, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return OrderPriority.Standard;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "rush" => OrderPriority.Rush,
            "standard" => OrderPriority.Standard,
            _ => AddPriorityError(errors)
        };
    }

    private static OrderPriority AddPriorityError(List<string> errors)
    {
        errors.Add("priority must be one of: rush, standard.");
        return OrderPriority.Standard;
    }
}

public sealed record OrderValidationResult(ValidatedOrder? Order, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0 && Order is not null;

    public static OrderValidationResult Valid(ValidatedOrder order) => new(order, []);

    public static OrderValidationResult Invalid(IReadOnlyList<string> errors) => new(null, errors);
}
