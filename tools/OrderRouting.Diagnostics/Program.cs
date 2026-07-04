using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OrderRouting.Api.Data;
using OrderRouting.Api.Models;
using OrderRouting.Api.Routing;

if (args.Contains("stress", StringComparer.OrdinalIgnoreCase) || args.Contains("--stress", StringComparer.OrdinalIgnoreCase))
{
    return await RunStressTest(args);
}

if (args.Contains("data-audit", StringComparer.OrdinalIgnoreCase) || args.Contains("--data-audit", StringComparer.OrdinalIgnoreCase))
{
    return RunDataAudit();
}

Console.WriteLine("Usage:");
Console.WriteLine("  dotnet run --project tools\\OrderRouting.Diagnostics -- stress --orders test_data\\performance_orders.json --concurrency 25");
Console.WriteLine("  dotnet run --project tools\\OrderRouting.Diagnostics -- data-audit");
return 1;

static async Task<int> RunStressTest(string[] args)
{
    var root = FindRepoRoot();
    var ordersPath = Path.GetFullPath(GetOption(args, "--orders") ?? Path.Combine(root, "test_data", "performance_orders.json"), root);
    if (!TryGetPositiveInt(args, "--concurrency", 25, out var concurrency, out var error) ||
        !TryGetPort(args, "--port", 18090, out var port, out error))
    {
        Console.Error.WriteLine(error);
        return 2;
    }

    if (!File.Exists(ordersPath))
    {
        Console.Error.WriteLine($"Orders file '{ordersPath}' does not exist.");
        return 2;
    }

    var orders = JsonSerializer.Deserialize<List<RouteOrderRequest>>(File.ReadAllText(ordersPath)) ?? [];
    var results = new List<StressResult>();

    using var process = StartApi(root, port);
    try
    {
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        await WaitForHealth(client);

        using var throttle = new SemaphoreSlim(concurrency);
        var stopwatch = Stopwatch.StartNew();
        var tasks = orders.Select(async (order, index) =>
        {
            await throttle.WaitAsync();
            try
            {
                var started = Stopwatch.GetTimestamp();
                var statusCode = HttpStatusCode.InternalServerError;
                var feasible = false;
                var shipmentCount = 0;
                var errorCount = 0;
                string? error = null;

                try
                {
                    using var response = await client.PostAsJsonAsync("/api/route", order);
                    statusCode = response.StatusCode;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var route = await response.Content.ReadFromJsonAsync<RouteOrderResponse>();
                        feasible = route?.Feasible ?? false;
                        shipmentCount = route?.Routing?.Count ?? 0;
                        errorCount = route?.Errors?.Count ?? 0;
                    }
                    else
                    {
                        error = await response.Content.ReadAsStringAsync();
                    }
                }
                catch (Exception exception)
                {
                    statusCode = 0;
                    errorCount = 1;
                    error = exception.GetType().Name + ": " + exception.Message;
                }

                var elapsed = Stopwatch.GetElapsedTime(started);
                lock (results)
                {
                    results.Add(new StressResult(index, statusCode, feasible, shipmentCount, errorCount, elapsed.TotalMilliseconds, error));
                }
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(tasks);
        stopwatch.Stop();

        var orderedLatencies = results.Select(result => result.ElapsedMs).OrderBy(value => value).ToArray();
        var summary = new StressSummary(
            TotalOrders: orders.Count,
            CompletedRequests: results.Count,
            Concurrency: concurrency,
            TotalElapsedMs: stopwatch.Elapsed.TotalMilliseconds,
            RequestsPerSecond: results.Count / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001),
            Http200Count: results.Count(result => result.StatusCode == HttpStatusCode.OK),
            FeasibleCount: results.Count(result => result.Feasible),
            InfeasibleCount: results.Count(result => result.StatusCode == HttpStatusCode.OK && !result.Feasible),
            FailedRequestCount: results.Count(result => result.StatusCode != HttpStatusCode.OK),
            P50Ms: Percentile(orderedLatencies, 0.50),
            P95Ms: Percentile(orderedLatencies, 0.95),
            P99Ms: Percentile(orderedLatencies, 0.99),
            MaxMs: orderedLatencies.LastOrDefault());

        Console.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));

        var failures = results.Where(result => result.StatusCode != HttpStatusCode.OK || result.Error is not null).Take(5).ToArray();
        if (failures.Length > 0)
        {
            Console.WriteLine("Sample failures:");
            foreach (var failure in failures)
            {
                Console.WriteLine($"  orderIndex={failure.OrderIndex} status={(int)failure.StatusCode} error={failure.Error}");
            }
        }

        return summary.FailedRequestCount == 0 ? 0 : 1;
    }
    finally
    {
        StopApi(process);
    }
}

static int RunDataAudit()
{
    var root = FindRepoRoot();
    var productsPath = Path.Combine(root, "service_data", "products.csv");
    var suppliersPath = Path.Combine(root, "service_data", "suppliers.csv");
    var logger = LoggerFactory.Create(_ => { }).CreateLogger("audit");

    var productCsvRows = CsvTableReader.CountDataRows(productsPath);
    var supplierCsvRows = CsvTableReader.CountDataRows(suppliersPath);
    var products = CsvProductRepository.Load(productsPath, logger);
    var suppliers = CsvSupplierRepository.Load(suppliersPath);
    var router = new OrderRouter(suppliers, Config(("Routing:LocalRatingSimilarityDelta", "0.5")));

    var productsByCategory = products.All
        .GroupBy(product => product.Category, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
    var productCategories = productsByCategory.Keys.ToHashSet(StringComparer.Ordinal);
    var supplierCategories = suppliers.All.SelectMany(supplier => supplier.ProductCategories).ToHashSet(StringComparer.Ordinal);

    var categoriesWithoutSuppliers = productCategories.Except(supplierCategories, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    var supplierCategoriesWithoutProducts = supplierCategories.Except(productCategories, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    var categoryCoverage = productCategories
        .Union(supplierCategories, StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .Select(category => new CategoryCoverageSummary(
            Category: category,
            ProductCount: productsByCategory.TryGetValue(category, out var categoryProducts) ? categoryProducts.Length : 0,
            SupplierCount: suppliers.All.Count(supplier => supplier.ProductCategories.Contains(category)),
            MailOrderSupplierCount: suppliers.All.Count(supplier => supplier.CanMailOrder && supplier.ProductCategories.Contains(category))))
        .ToArray();

    var productRouteFailures = new List<string>();
    foreach (var product in products.All.OrderBy(product => product.ProductCode, StringComparer.OrdinalIgnoreCase))
    {
        var order = new ValidatedOrder(
            $"AUDIT-PRODUCT-{product.ProductCode}",
            "10015",
            MailOrder: true,
            OrderPriority.Standard,
            [new ValidatedOrderItem(0, product.ProductCode, 1, product)]);

        var response = router.Route(order);
        if (!response.Feasible)
        {
            productRouteFailures.Add($"{product.ProductCode} ({product.Category}): {string.Join("; ", response.Errors ?? [])}");
        }
    }

    var firstZipBySupplier = CsvTableReader.ReadRows(suppliersPath).ToDictionary(
        row => row.Required("supplier_id"),
        row => FirstZipFromServiceZips(row.Required("service_zips")),
        StringComparer.Ordinal);

    var supplierValidationFailures = new List<string>();
    foreach (var supplier in suppliers.All.OrderBy(supplier => supplier.SupplierId, StringComparer.Ordinal))
    {
        var knownCategory = supplier.ProductCategories.FirstOrDefault(productCategories.Contains);
        if (knownCategory is null)
        {
            supplierValidationFailures.Add($"{supplier.SupplierId}: no supported product category maps to a product.");
            continue;
        }

        if (!productsByCategory.TryGetValue(knownCategory, out var categoryProducts) || categoryProducts.Length == 0)
        {
            supplierValidationFailures.Add($"{supplier.SupplierId}: no product exists for category {knownCategory}.");
            continue;
        }

        if (!firstZipBySupplier.TryGetValue(supplier.SupplierId, out var firstZip))
        {
            supplierValidationFailures.Add($"{supplier.SupplierId}: no service ZIP could be read.");
            continue;
        }

        var product = categoryProducts[0];
        var candidate = SupplierEligibility.GetCandidate(supplier, product, firstZip, mailOrderAllowed: false);
        if (candidate is null)
        {
            supplierValidationFailures.Add($"{supplier.SupplierId}: not locally eligible for category {knownCategory} at its own ZIP {firstZip}.");
        }
    }

    var regionalCoverage = BuildRegionalCoverage(productsByCategory, suppliers);

    var summary = new DataAuditSummary(
        ProductCsvRows: productCsvRows,
        UniqueProductsLoaded: products.Count,
        DuplicateProductRowsIgnored: productCsvRows - products.Count,
        ProductCategoryCount: productCategories.Count,
        SupplierCsvRows: supplierCsvRows,
        SuppliersLoaded: suppliers.Count,
        SupplierCategoryCount: supplierCategories.Count,
        RatedSupplierCount: suppliers.All.Count(supplier => supplier.CustomerSatisfactionScore is not null),
        UnratedSupplierCount: suppliers.All.Count(supplier => supplier.CustomerSatisfactionScore is null),
        MailOrderSupplierCount: suppliers.All.Count(supplier => supplier.CanMailOrder),
        CategoriesWithoutSuppliers: categoriesWithoutSuppliers,
        SupplierCategoriesWithoutProducts: supplierCategoriesWithoutProducts,
        ProductRouteFailureCount: productRouteFailures.Count,
        SupplierValidationFailureCount: supplierValidationFailures.Count,
        CategoryCoverage: categoryCoverage,
        RegionalLocalCoverage: regionalCoverage,
        SampleProductRouteFailures: productRouteFailures.Take(10).ToArray(),
        SampleSupplierValidationFailures: supplierValidationFailures.Take(10).ToArray());

    Console.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
    return productRouteFailures.Count == 0 && supplierValidationFailures.Count == 0 && categoriesWithoutSuppliers.Length == 0
        ? 0
        : 1;
}

static Process StartApi(string root, int port)
{
    var process = new Process();
    process.StartInfo = new ProcessStartInfo("dotnet", $"run --no-build --no-launch-profile --project \"{Path.Combine(root, "src", "OrderRouting.Api", "OrderRouting.Api.csproj")}\"")
    {
        WorkingDirectory = root,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    process.StartInfo.Environment["PORT"] = port.ToString();
    process.OutputDataReceived += (_, _) => { };
    process.ErrorDataReceived += (_, _) => { };
    process.Start();
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    return process;
}

static void StopApi(Process process)
{
    if (!process.HasExited)
    {
        process.Kill(entireProcessTree: true);
    }

    process.Dispose();
}

static IReadOnlyList<RegionalCoverageSummary> BuildRegionalCoverage(
    IReadOnlyDictionary<string, Product[]> productsByCategory,
    CsvSupplierRepository suppliers)
{
    var majorRegions = new (string Name, string Zip)[]
    {
        ("New York", "10015"),
        ("Brooklyn", "11221"),
        ("Boston", "02130"),
        ("Chicago", "60610"),
        ("Houston", "77059"),
        ("Los Angeles", "90020"),
        ("Philadelphia", "19131")
    };

    var summaries = new List<RegionalCoverageSummary>();
    foreach (var region in majorRegions)
    {
        var coveredCategories = 0;
        var uncovered = new List<string>();

        foreach (var category in productsByCategory.Keys.Order(StringComparer.Ordinal))
        {
            var product = productsByCategory[category][0];
            var covered = suppliers.All.Any(supplier => SupplierEligibility.GetCandidate(supplier, product, region.Zip, mailOrderAllowed: false) is not null);
            if (covered)
            {
                coveredCategories++;
            }
            else
            {
                uncovered.Add(category);
            }
        }

        summaries.Add(new RegionalCoverageSummary(
            RegionName: region.Name,
            Zip: region.Zip,
            CoveredCategoryCount: coveredCategories,
            TotalCategoryCount: productsByCategory.Count,
            UncoveredCategories: uncovered));
    }

    return summaries;
}

static async Task WaitForHealth(HttpClient client)
{
    var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
    while (DateTimeOffset.UtcNow < deadline)
    {
        try
        {
            using var response = await client.GetAsync("/health");
            if (response.IsSuccessStatusCode)
            {
                return;
            }
        }
        catch
        {
            await Task.Delay(250);
        }
    }

    throw new TimeoutException("API did not become healthy.");
}

static string FirstZipFromServiceZips(string serviceZips)
{
    var token = serviceZips.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
    var zip = token.Contains('-', StringComparison.Ordinal)
        ? token.Split('-', StringSplitOptions.TrimEntries)[0]
        : token;
    return ZipCoverage.NormalizeZip(zip);
}

static string? GetOption(string[] args, string name)
{
    for (var index = 0; index < args.Length - 1; index++)
    {
        if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[index + 1];
        }
    }

    return null;
}

static bool TryGetPositiveInt(string[] args, string name, int defaultValue, out int value, out string? error)
{
    var raw = GetOption(args, name);
    if (raw is null)
    {
        value = defaultValue;
        error = null;
        return true;
    }

    if (int.TryParse(raw, out value) && value > 0)
    {
        error = null;
        return true;
    }

    error = $"{name} must be a positive integer.";
    return false;
}

static bool TryGetPort(string[] args, string name, int defaultValue, out int value, out string? error)
{
    if (!TryGetPositiveInt(args, name, defaultValue, out value, out error))
    {
        return false;
    }

    if (value <= 65_535)
    {
        return true;
    }

    error = $"{name} must be between 1 and 65535.";
    return false;
}

static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
{
    if (sortedValues.Count == 0)
    {
        return 0;
    }

    var index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
    return sortedValues[Math.Clamp(index, 0, sortedValues.Count - 1)];
}

static IConfiguration Config(params (string Key, string Value)[] values)
{
    return new ConfigurationBuilder().AddInMemoryCollection(values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value))).Build();
}

static string FindRepoRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (Directory.Exists(Path.Combine(directory.FullName, "service_data")) && File.Exists(Path.Combine(directory.FullName, "OrderRouting.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Could not find repository root.");
}

internal sealed record StressResult(
    int OrderIndex,
    HttpStatusCode StatusCode,
    bool Feasible,
    int ShipmentCount,
    int ErrorCount,
    double ElapsedMs,
    string? Error);

internal sealed record StressSummary(
    int TotalOrders,
    int CompletedRequests,
    int Concurrency,
    double TotalElapsedMs,
    double RequestsPerSecond,
    int Http200Count,
    int FeasibleCount,
    int InfeasibleCount,
    int FailedRequestCount,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double MaxMs);

internal sealed record DataAuditSummary(
    int ProductCsvRows,
    int UniqueProductsLoaded,
    int DuplicateProductRowsIgnored,
    int ProductCategoryCount,
    int SupplierCsvRows,
    int SuppliersLoaded,
    int SupplierCategoryCount,
    int RatedSupplierCount,
    int UnratedSupplierCount,
    int MailOrderSupplierCount,
    IReadOnlyList<string> CategoriesWithoutSuppliers,
    IReadOnlyList<string> SupplierCategoriesWithoutProducts,
    int ProductRouteFailureCount,
    int SupplierValidationFailureCount,
    IReadOnlyList<CategoryCoverageSummary> CategoryCoverage,
    IReadOnlyList<RegionalCoverageSummary> RegionalLocalCoverage,
    IReadOnlyList<string> SampleProductRouteFailures,
    IReadOnlyList<string> SampleSupplierValidationFailures);

internal sealed record CategoryCoverageSummary(
    string Category,
    int ProductCount,
    int SupplierCount,
    int MailOrderSupplierCount);

internal sealed record RegionalCoverageSummary(
    string RegionName,
    string Zip,
    int CoveredCategoryCount,
    int TotalCategoryCount,
    IReadOnlyList<string> UncoveredCategories);
