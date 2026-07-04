using OrderRouting.Api.Models;

namespace OrderRouting.Api.Routing;

public static class SupplierEligibility
{
    public static SupplierCandidate? GetCandidate(Supplier supplier, Product product, string customerZip, bool mailOrderAllowed)
    {
        if (!supplier.ProductCategories.Contains(product.Category))
        {
            return null;
        }

        var local = supplier.ServiceZips.Contains(customerZip);
        var mail = mailOrderAllowed && supplier.CanMailOrder;

        if (!local && !mail)
        {
            return null;
        }

        return new SupplierCandidate(supplier, local ? "local" : "mail_order", local);
    }
}

public sealed record SupplierCandidate(Supplier Supplier, string FulfillmentMode, bool IsLocal);
