using OrderRouting.Api.Routing;

namespace OrderRouting.Api.Models;

public sealed record Supplier(
    string SupplierId,
    string SupplierName,
    ZipCoverage ServiceZips,
    IReadOnlySet<string> ProductCategories,
    decimal? CustomerSatisfactionScore,
    bool CanMailOrder);
