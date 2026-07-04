using OrderRouting.Api.Models;
using OrderRouting.Api.Routing;

namespace OrderRouting.Api.Data;

public sealed class CsvProductRepository
{
    private readonly Dictionary<string, Product> _products;

    private CsvProductRepository(Dictionary<string, Product> products)
    {
        _products = products;
    }

    public int Count => _products.Count;

    public IReadOnlyList<Product> All => _products.Values.ToArray();

    public Product? Find(string productCode)
    {
        return _products.TryGetValue(productCode.Trim(), out var product) ? product : null;
    }

    public static CsvProductRepository Load(string path, ILogger logger)
    {
        var table = CsvTableReader.Read(path);
        var products = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in table.Rows)
        {
            var code = row.Required("product_code");
            var name = row.Required("product_name");
            var category = CategoryNormalizer.Normalize(row.Required("category"));
            var product = new Product(code, name, category);

            if (products.TryGetValue(code, out var existing))
            {
                if (string.Equals(existing.ProductName, name, StringComparison.Ordinal) && string.Equals(existing.Category, category, StringComparison.Ordinal))
                {
                    logger.LogWarning("Ignoring identical duplicate product code {ProductCode} at row {RowNumber}.", code, row.RowNumber);
                    continue;
                }

                throw new DataLoadException($"Product CSV row {row.RowNumber} has conflicting duplicate product_code '{code}'.");
            }

            products.Add(code, product);
        }

        return new CsvProductRepository(products);
    }
}
