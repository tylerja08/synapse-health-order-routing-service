using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OrderRouting.Api.Data;
using OrderRouting.Api.Models;
using OrderRouting.Api.Routing;

var tests = new (string Name, Func<Task> Run)[]
{
    ("request validation returns multiple errors", RequestValidationReturnsMultipleErrors),
    ("invalid priority lists allowed values", InvalidPriorityListsAllowedValues),
    ("product lookup is case-insensitive and normalizes category", ProductLookupIsCaseInsensitive),
    ("zip coverage parses exact values and ranges", ZipCoverageParsesValuesAndRanges),
    ("zip coverage left-pads short source zips", ZipCoverageLeftPadsShortZips),
    ("supplier eligibility handles local and mail order", SupplierEligibilityHandlesModes),
    ("router consolidates shipments", RouterConsolidatesShipments),
    ("router prefers higher rating at same shipment count", RouterPrefersHigherRating),
    ("router prefers local when ratings are similar", RouterPrefersLocalWhenRatingsAreSimilar),
    ("router uses supplier id tiebreak", RouterUsesSupplierIdTiebreak),
    ("router reports infeasible orders", RouterReportsInfeasibleOrders),
    ("scheduler processes rush before waiting standard", SchedulerProcessesRushFirst),
    ("scheduler preserves fifo within priority", SchedulerPreservesFifo),
    ("scheduler reports capacity", SchedulerReportsCapacity),
    ("api post route smoke test", ApiPostRouteSmokeTest)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.WriteLine($"FAIL {test.Name}");
        Console.WriteLine(exception);
    }
}

if (failed > 0)
{
    Console.WriteLine($"{failed} test(s) failed.");
    return 1;
}

Console.WriteLine($"{tests.Length} test(s) passed.");
return 0;

static Task RequestValidationReturnsMultipleErrors()
{
    var validator = new OrderValidator(ProductRepository(("P1", "Product 1", "cpap")));
    var result = validator.Validate(new RouteOrderRequest("ORD", "abc", false, null, []));
    AssertFalse(result.IsValid);
    AssertContains("Order must include a valid customer_zip.", result.Errors);
    AssertContains("Order must include at least one line item.", result.Errors);
    return Task.CompletedTask;
}

static Task InvalidPriorityListsAllowedValues()
{
    var validator = new OrderValidator(ProductRepository(("P1", "Product 1", "cpap")));
    var result = validator.Validate(new RouteOrderRequest("ORD", "10001", false, "urgent", [new("P1", 1)]));
    AssertFalse(result.IsValid);
    AssertContains("priority must be one of: rush, standard.", result.Errors);
    return Task.CompletedTask;
}

static Task ProductLookupIsCaseInsensitive()
{
    var repository = ProductRepository(("CP-STD-031", "CPAP Machine", "CPAP"));
    var product = repository.Find("cp-std-031");
    AssertNotNull(product);
    AssertEqual("cpap", product!.Category);
    return Task.CompletedTask;
}

static Task ZipCoverageParsesValuesAndRanges()
{
    var coverage = ZipCoverage.Parse("10001, 10005-10007", "test");
    AssertTrue(coverage.Contains("10001"));
    AssertTrue(coverage.Contains("10006"));
    AssertFalse(coverage.Contains("10008"));
    return Task.CompletedTask;
}

static Task ZipCoverageLeftPadsShortZips()
{
    var coverage = ZipCoverage.Parse("2110-2112", "test");
    AssertTrue(coverage.Contains("02111"));
    AssertFalse(coverage.Contains("02113"));
    return Task.CompletedTask;
}

static Task SupplierEligibilityHandlesModes()
{
    var product = new Product("P1", "Product 1", "wheelchair");
    var localSupplier = Supplier("SUP-001", "Local", "10001", "wheelchair", 8m, false);
    var mailSupplier = Supplier("SUP-002", "Mail", "99999", "wheelchair", 9m, true);

    AssertNotNull(SupplierEligibility.GetCandidate(localSupplier, product, "10001", false));
    AssertNull(SupplierEligibility.GetCandidate(mailSupplier, product, "10001", false));
    AssertNotNull(SupplierEligibility.GetCandidate(mailSupplier, product, "10001", true));
    AssertEqual("local", SupplierEligibility.GetCandidate(localSupplier, product, "10001", true)!.FulfillmentMode);
    return Task.CompletedTask;
}

static Task RouterConsolidatesShipments()
{
    var products = ProductRepository(("WC", "Wheelchair", "wheelchair"), ("CN", "Cane", "cane"));
    var suppliers = SupplierRepository(
        Supplier("SUP-G", "Generalist", "10001", "wheelchair, cane", 6m, false),
        Supplier("SUP-W", "Wheelchair", "10001", "wheelchair", 10m, false),
        Supplier("SUP-C", "Cane", "10001", "cane", 10m, false));
    var response = Route(products, suppliers, new("ORD", "10001", false, "standard", [new("WC", 1), new("CN", 1)]));
    AssertTrue(response.Feasible);
    AssertEqual(1, response.Routing!.Count);
    AssertEqual("SUP-G", response.Routing[0].SupplierId);
    return Task.CompletedTask;
}

static Task RouterPrefersHigherRating()
{
    var products = ProductRepository(("WC", "Wheelchair", "wheelchair"));
    var suppliers = SupplierRepository(
        Supplier("SUP-LOW", "Low", "10001", "wheelchair", 6m, false),
        Supplier("SUP-HIGH", "High", "10001", "wheelchair", 9m, false));
    var response = Route(products, suppliers, new("ORD", "10001", false, "standard", [new("WC", 1)]));
    AssertEqual("SUP-HIGH", response.Routing![0].SupplierId);
    return Task.CompletedTask;
}

static Task RouterPrefersLocalWhenRatingsAreSimilar()
{
    var products = ProductRepository(("WC", "Wheelchair", "wheelchair"));
    var suppliers = SupplierRepository(
        Supplier("SUP-LOCAL", "Local", "10001", "wheelchair", 9.5m, false),
        Supplier("SUP-MAIL", "Mail", "99999", "wheelchair", 10m, true));
    var response = Route(products, suppliers, new("ORD", "10001", true, "standard", [new("WC", 1)]));
    AssertEqual("SUP-LOCAL", response.Routing![0].SupplierId);
    AssertEqual("local", response.Routing[0].Items[0].FulfillmentMode);
    return Task.CompletedTask;
}

static Task RouterUsesSupplierIdTiebreak()
{
    var products = ProductRepository(("CN", "Cane", "cane"));
    var suppliers = SupplierRepository(
        Supplier("SUP-B", "B", "10001", "cane", 8m, false),
        Supplier("SUP-A", "A", "10001", "cane", 8m, false));
    var response = Route(products, suppliers, new("ORD", "10001", false, "standard", [new("CN", 1)]));
    AssertEqual("SUP-A", response.Routing![0].SupplierId);
    return Task.CompletedTask;
}

static Task RouterReportsInfeasibleOrders()
{
    var products = ProductRepository(("WC", "Wheelchair", "wheelchair"));
    var suppliers = SupplierRepository(Supplier("SUP-C", "Cane", "10001", "cane", 8m, false));
    var response = Route(products, suppliers, new("ORD", "10001", false, "standard", [new("WC", 1)]));
    AssertFalse(response.Feasible);
    AssertContains("No eligible supplier can fulfill product WC for category wheelchair at ZIP 10001.", response.Errors!);
    return Task.CompletedTask;
}

static async Task SchedulerProcessesRushFirst()
{
    using var scheduler = new RouteRequestScheduler(Config(("Routing:MaxQueuedRequests", "10")));
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var completed = new List<string>();
    var first = scheduler.EnqueueAsync(OrderPriority.Standard, async _ =>
    {
        await gate.Task;
        completed.Add("first");
        return RouteOrderResponse.Failure(["first"]);
    }, CancellationToken.None);

    var standard = scheduler.EnqueueAsync(OrderPriority.Standard, _ =>
    {
        completed.Add("standard");
        return Task.FromResult(RouteOrderResponse.Failure(["standard"]));
    }, CancellationToken.None);

    var rush = scheduler.EnqueueAsync(OrderPriority.Rush, _ =>
    {
        completed.Add("rush");
        return Task.FromResult(RouteOrderResponse.Failure(["rush"]));
    }, CancellationToken.None);

    gate.SetResult();
    await Task.WhenAll(first, standard, rush);
    AssertSequence(["rush", "first", "standard"], completed);
}

static async Task SchedulerPreservesFifo()
{
    using var scheduler = new RouteRequestScheduler(Config(("Routing:MaxQueuedRequests", "10")));
    var completed = new List<string>();
    await Task.WhenAll(
        scheduler.EnqueueAsync(OrderPriority.Standard, _ => { completed.Add("a"); return Task.FromResult(RouteOrderResponse.Failure(["a"])); }, CancellationToken.None),
        scheduler.EnqueueAsync(OrderPriority.Standard, _ => { completed.Add("b"); return Task.FromResult(RouteOrderResponse.Failure(["b"])); }, CancellationToken.None));
    AssertSequence(["a", "b"], completed);
}

static async Task SchedulerReportsCapacity()
{
    using var scheduler = new RouteRequestScheduler(Config(("Routing:MaxQueuedRequests", "1")));
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var first = scheduler.EnqueueAsync(OrderPriority.Standard, async _ =>
    {
        await gate.Task;
        return RouteOrderResponse.Success([]);
    }, CancellationToken.None);
    var second = await scheduler.EnqueueAsync(OrderPriority.Standard, _ => Task.FromResult(RouteOrderResponse.Success([])), CancellationToken.None);
    gate.SetResult();
    await first;
    AssertFalse(second.Feasible);
    AssertContains("Routing service is temporarily at capacity. Please retry.", second.Errors!);
}

static async Task ApiPostRouteSmokeTest()
{
    var root = FindRepoRoot();
    var port = 18080;
    using var process = new Process();
    process.StartInfo = new ProcessStartInfo("dotnet", $"run --no-build --project \"{Path.Combine(root, "src", "OrderRouting.Api", "OrderRouting.Api.csproj")}\"")
    {
        WorkingDirectory = root,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    process.StartInfo.Environment["PORT"] = port.ToString();
    process.Start();

    try
    {
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        await WaitForHealth(client);
        await VerifyApiDocs(client);

        var sample = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "test_data", "sample_orders.json"))).RootElement[0].GetRawText();
        using var content = new StringContent(sample, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/route", content);
        AssertEqual(System.Net.HttpStatusCode.OK, response.StatusCode);

        var route = await response.Content.ReadFromJsonAsync<RouteOrderResponse>();
        AssertNotNull(route);
        AssertTrue(route!.Feasible);
        AssertTrue(route.Routing!.Count > 0);
    }
    finally
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }
}

static async Task VerifyApiDocs(HttpClient client)
{
    using var swagger = await client.GetAsync("/swagger");
    AssertEqual(System.Net.HttpStatusCode.OK, swagger.StatusCode);
    var swaggerHtml = await swagger.Content.ReadAsStringAsync();
    if (!swaggerHtml.Contains("Order Routing Service API", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Swagger page did not contain expected title.");
    }

    using var openApi = await client.GetAsync("/openapi.json");
    AssertEqual(System.Net.HttpStatusCode.OK, openApi.StatusCode);
    using var document = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync());
    AssertEqual("3.0.3", document.RootElement.GetProperty("openapi").GetString());
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

static RouteOrderResponse Route(CsvProductRepository products, CsvSupplierRepository suppliers, RouteOrderRequest request)
{
    var validation = new OrderValidator(products).Validate(request);
    AssertTrue(validation.IsValid);
    return new OrderRouter(suppliers, Config(("Routing:LocalRatingSimilarityDelta", "0.5"))).Route(validation.Order!);
}

static CsvProductRepository ProductRepository(params (string Code, string Name, string Category)[] products)
{
    var path = Path.Combine(Path.GetTempPath(), $"products-{Guid.NewGuid():N}.csv");
    File.WriteAllText(path, "product_code,product_name,category\r\n" + string.Join("\r\n", products.Select(product => $"{product.Code},{product.Name},{product.Category}")));
    return CsvProductRepository.Load(path, LoggerFactory.Create(_ => { }).CreateLogger("test"));
}

static CsvSupplierRepository SupplierRepository(params Supplier[] suppliers)
{
    return new CsvSupplierRepository(suppliers);
}

static Supplier Supplier(string id, string name, string zips, string categories, decimal? rating, bool canMail)
{
    return new Supplier(
        id,
        name,
        ZipCoverage.Parse(zips, "test"),
        categories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(CategoryNormalizer.Normalize).ToHashSet(StringComparer.Ordinal),
        rating,
        canMail);
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

static void AssertTrue(bool value)
{
    if (!value)
    {
        throw new InvalidOperationException("Expected true.");
    }
}

static void AssertFalse(bool value)
{
    if (value)
    {
        throw new InvalidOperationException("Expected false.");
    }
}

static void AssertNotNull(object? value)
{
    if (value is null)
    {
        throw new InvalidOperationException("Expected non-null value.");
    }
}

static void AssertNull(object? value)
{
    if (value is not null)
    {
        throw new InvalidOperationException("Expected null value.");
    }
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}

static void AssertContains(string expected, IReadOnlyList<string> values)
{
    if (!values.Contains(expected))
    {
        throw new InvalidOperationException($"Expected list to contain '{expected}'. Actual: {string.Join(" | ", values)}");
    }
}

static void AssertSequence(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
{
    if (!expected.SequenceEqual(actual))
    {
        throw new InvalidOperationException($"Expected sequence '{string.Join(", ", expected)}', got '{string.Join(", ", actual)}'.");
    }
}
