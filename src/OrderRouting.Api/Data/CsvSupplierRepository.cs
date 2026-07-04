using System.Globalization;
using OrderRouting.Api.Models;
using OrderRouting.Api.Routing;

namespace OrderRouting.Api.Data;

public sealed class CsvSupplierRepository
{
    private readonly IReadOnlyList<Supplier> _suppliers;

    public CsvSupplierRepository(IReadOnlyList<Supplier> suppliers)
    {
        _suppliers = suppliers;
    }

    public int Count => _suppliers.Count;

    public IReadOnlyList<Supplier> All => _suppliers;

    public static CsvSupplierRepository Load(string path)
    {
        var table = CsvTableReader.Read(path);
        var suppliers = new List<Supplier>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in table.Rows)
        {
            var id = row.Required("supplier_id");
            if (!ids.Add(id))
            {
                throw new DataLoadException($"Supplier CSV row {row.RowNumber} has duplicate supplier_id '{id}'.");
            }

            var name = row.RequiredAny("suplier_name", "supplier_name");
            var zips = ZipCoverage.Parse(row.Required("service_zips"), $"suppliers row {row.RowNumber} service_zips");
            var categories = row.Required("product_categories")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(CategoryNormalizer.Normalize)
                .Where(category => category.Length > 0)
                .ToHashSet(StringComparer.Ordinal);

            if (categories.Count == 0)
            {
                throw new DataLoadException($"Supplier CSV row {row.RowNumber} must include at least one product category.");
            }

            var rating = ParseRating(row.Required("customer_satisfaction_score"), row.RowNumber);
            var canMailOrder = ParseMailOrder(row.Required("can_mail_order?"), row.RowNumber);
            suppliers.Add(new Supplier(id, name, zips, categories, rating, canMailOrder));
        }

        return new CsvSupplierRepository(suppliers);
    }

    private static decimal? ParseRating(string value, int rowNumber)
    {
        if (string.Equals(value.Trim(), "no ratings yet", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var rating) || rating < 1m || rating > 10m)
        {
            throw new DataLoadException($"Supplier CSV row {rowNumber} has invalid customer_satisfaction_score '{value}'.");
        }

        return rating;
    }

    private static bool ParseMailOrder(string value, int rowNumber)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "y" => true,
            "n" => false,
            _ => throw new DataLoadException($"Supplier CSV row {rowNumber} has invalid can_mail_order? value '{value}'.")
        };
    }
}
