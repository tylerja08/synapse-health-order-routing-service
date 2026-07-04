using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OrderRouting.Api.Data;
using OrderRouting.Api.Models;
using OrderRouting.Api.Routing;

if (args.Contains("--stress", StringComparer.OrdinalIgnoreCase))
{
    return await RunStressTest(args);
}

if (args.Contains("--data-audit", StringComparer.OrdinalIgnoreCase))
{
    return RunDataAudit();
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("request validation returns multiple errors", RequestValidationReturnsMultipleErrors),
    ("invalid priority lists allowed values", InvalidPriorityListsAllowedValues),
    ("product lookup is case-insensitive and normalizes category", ProductLookupIsCaseInsensitive),
    ("duplicate product rows are handled correctly", DuplicateProductRowsAreHandledCorrectly),
    ("csv parser handles quoted fields and line endings", CsvParserHandlesQuotedFieldsAndLineEndings),
    ("csv parser rejects unterminated quotes", CsvParserRejectsUnterminatedQuotes),
    ("csv loaders reject malformed data", CsvLoadersRejectMalformedData),
    ("zip coverage parses exact values and ranges", ZipCoverageParsesValuesAndRanges),
    ("zip coverage left-pads short source zips", ZipCoverageLeftPadsShortZips),
    ("supplier eligibility handles local and mail order", SupplierEligibilityHandlesModes),
    ("router consolidates shipments", RouterConsolidatesShipments),
    ("router prefers higher rating at same shipment count", RouterPrefersHigherRating),
    ("router uses quantity weighted rating", RouterUsesQuantityWeightedRating),
    ("router prefers local when ratings are similar", RouterPrefersLocalWhenRatingsAreSimilar),
    ("router uses supplier id tiebreak", RouterUsesSupplierIdTiebreak),
    ("router response order is deterministic", RouterResponseOrderIsDeterministic),
    ("router reports infeasible orders", RouterReportsInfeasibleOrders),
    ("scheduler processes rush before waiting standard", SchedulerProcessesRushFirst),
    ("scheduler preserves fifo within priority", SchedulerPreservesFifo),
    ("scheduler reports capacity", SchedulerReportsCapacity),
    ("api startup fails when data file is missing", ApiStartupFailsWhenDataFileIsMissing),
    ("api startup fails when supplier data file is missing", ApiStartupFailsWhenSupplierDataFileIsMissing),
    ("api startup fails when supplier data is malformed", ApiStartupFailsWhenSupplierDataIsMalformed),
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

static Task DuplicateProductRowsAreHandledCorrectly()
{
    var duplicatePath = TempFile("products", "product_code,product_name,category\r\nP1,Product 1,CPAP\r\nP1,Product 1,CPAP");
    var repository = CsvProductRepository.Load(duplicatePath, LoggerFactory.Create(_ => { }).CreateLogger("test"));
    AssertEqual(1, repository.Count);

    var conflictingPath = TempFile("products", "product_code,product_name,category\r\nP1,Product 1,CPAP\r\nP1,Product 1,wheelchair");
    AssertThrows<DataLoadException>(() => CsvProductRepository.Load(conflictingPath, LoggerFactory.Create(_ => { }).CreateLogger("test")));
    return Task.CompletedTask;
}

static Task CsvParserHandlesQuotedFieldsAndLineEndings()
{
    var records = CsvTableReader.Parse("a,b,c\r\n\"one, two\",\"say \"\"hi\"\"\",three\nlast,,");
    AssertEqual(3, records.Count);
    AssertEqual("one, two", records[1][0]);
    AssertEqual("say \"hi\"", records[1][1]);
    AssertEqual("", records[2][1]);
    AssertEqual("", records[2][2]);
    return Task.CompletedTask;
}

static Task CsvParserRejectsUnterminatedQuotes()
{
    AssertThrows<DataLoadException>(() => CsvTableReader.Parse("a,b\r\n\"unterminated,b"));
    return Task.CompletedTask;
}

static Task CsvLoadersRejectMalformedData()
{
    AssertThrows<DataLoadException>(() => CsvProductRepository.Load(
        TempFile("products", "product_code,product_name\r\nP1,Product 1"),
        LoggerFactory.Create(_ => { }).CreateLogger("test")));

    AssertThrows<DataLoadException>(() => CsvSupplierRepository.Load(
        TempFile("suppliers", "supplier_id,suplier_name,service_zips,product_categories,customer_satisfaction_score,can_mail_order?\r\nSUP-1,Supplier,10001,wheelchair,invalid,y")));

    AssertThrows<DataLoadException>(() => CsvSupplierRepository.Load(
        TempFile("suppliers", "supplier_id,suplier_name,service_zips,product_categories,customer_satisfaction_score,can_mail_order?\r\nSUP-1,Supplier,10001,wheelchair,8,maybe")));

    AssertThrows<DataLoadException>(() => CsvSupplierRepository.Load(
        TempFile("suppliers", "supplier_id,suplier_name,service_zips,product_categories,customer_satisfaction_score,can_mail_order?\r\nSUP-1,Supplier,10005-10001,wheelchair,8,y")));

    AssertThrows<DataLoadException>(() => CsvSupplierRepository.Load(
        TempFile("suppliers", "supplier_id,service_zips,product_categories,customer_satisfaction_score,can_mail_order?\r\nSUP-1,10001,wheelchair,8,y")));

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

static Task RouterUsesQuantityWeightedRating()
{
    var products = ProductRepository(("A", "Product A", "category-a"), ("B", "Product B", "category-b"));
    var suppliers = SupplierRepository(
        Supplier("SUP-A-HIGH", "A High", "10001", "category-a", 10m, false),
        Supplier("SUP-A-LOW", "A Low", "10001", "category-a", 1m, false),
        Supplier("SUP-B-HIGH", "B High", "10001", "category-b", 10m, false),
        Supplier("SUP-B-LOW", "B Low", "10001", "category-b", 1m, false));

    var response = Route(products, suppliers, new("ORD", "10001", false, "standard", [new("A", 1), new("B", 10)]));

    AssertTrue(response.Feasible);
    AssertEqual(2, response.Routing!.Count);
    AssertTrue(response.Routing.Any(group => group.SupplierId == "SUP-A-HIGH"));
    AssertTrue(response.Routing.Any(group => group.SupplierId == "SUP-B-HIGH"));
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

static Task RouterResponseOrderIsDeterministic()
{
    var products = ProductRepository(("A", "Product A", "category-a"), ("B", "Product B", "category-b"), ("C", "Product C", "category-a"));
    var suppliers = SupplierRepository(
        Supplier("SUP-B", "B", "10001", "category-b", 10m, false),
        Supplier("SUP-A", "A", "10001", "category-a", 10m, false));
    var response = Route(products, suppliers, new("ORD", "10001", false, "standard", [new("C", 1), new("B", 1), new("A", 1)]));

    AssertTrue(response.Feasible);
    AssertEqual("SUP-A", response.Routing![0].SupplierId);
    AssertEqual("SUP-B", response.Routing[1].SupplierId);
    AssertSequence(["C", "A"], response.Routing[0].Items.Select(item => item.ProductCode).ToArray());
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
    var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var completed = new List<string>();
    var first = scheduler.EnqueueAsync(OrderPriority.Standard, async _ =>
    {
        firstStarted.SetResult();
        await gate.Task;
        completed.Add("first");
        return RouteOrderResponse.Failure(["first"]);
    }, CancellationToken.None);

    await firstStarted.Task;

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
    AssertSequence(["first", "rush", "standard"], completed);
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
    var port = GetFreePort();
    using var process = new Process();
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

        await VerifyApiMalformedJson(client);
        await VerifyApiBusinessValidation(client);
        await VerifyApiUnknownProduct(client);
        await VerifyApiInvalidPriority(client);
        await VerifyApiMailOrderModes(client);
        await VerifyApiRejectsOversizedBody(client);
    }
    finally
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }
}

static Task ApiStartupFailsWhenDataFileIsMissing()
{
    var root = FindRepoRoot();
    var output = RunApiExpectingStartupFailure(
        productsPath: Path.Combine(root, "service_data", "missing-products.csv"),
        suppliersPath: Path.Combine(root, "service_data", "suppliers.csv"));

    if (!output.Contains("missing-products.csv", StringComparison.OrdinalIgnoreCase) &&
        !output.Contains("Failed to load startup data", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Expected startup output to mention missing product data file.");
    }

    return Task.CompletedTask;
}

static Task ApiStartupFailsWhenSupplierDataFileIsMissing()
{
    var root = FindRepoRoot();
    var output = RunApiExpectingStartupFailure(
        productsPath: Path.Combine(root, "service_data", "products.csv"),
        suppliersPath: Path.Combine(root, "service_data", "missing-suppliers.csv"));

    if (!output.Contains("missing-suppliers.csv", StringComparison.OrdinalIgnoreCase) &&
        !output.Contains("Failed to load startup data", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Expected startup output to mention missing supplier data file.");
    }

    return Task.CompletedTask;
}

static Task ApiStartupFailsWhenSupplierDataIsMalformed()
{
    var root = FindRepoRoot();
    var malformedSupplierPath = TempFile(
        "malformed-suppliers",
        "supplier_id,suplier_name,service_zips,product_categories,customer_satisfaction_score,can_mail_order?\r\nSUP-BAD,Bad Supplier,10001,wheelchair,not-a-rating,y");

    var output = RunApiExpectingStartupFailure(
        productsPath: Path.Combine(root, "service_data", "products.csv"),
        suppliersPath: malformedSupplierPath);

    if (!output.Contains("invalid customer_satisfaction_score", StringComparison.OrdinalIgnoreCase) &&
        !output.Contains("Failed to load startup data", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Expected startup output to mention malformed supplier rating.");
    }

    return Task.CompletedTask;
}

static string RunApiExpectingStartupFailure(string productsPath, string suppliersPath)
{
    var root = FindRepoRoot();
    var output = new StringBuilder();
    using var process = new Process();
    process.StartInfo = new ProcessStartInfo("dotnet", $"run --no-build --no-launch-profile --project \"{Path.Combine(root, "src", "OrderRouting.Api", "OrderRouting.Api.csproj")}\"")
    {
        WorkingDirectory = root,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    process.StartInfo.Environment["PORT"] = GetFreePort().ToString();
    process.StartInfo.Environment["Data__ProductsPath"] = productsPath;
    process.StartInfo.Environment["Data__SuppliersPath"] = suppliersPath;
    process.OutputDataReceived += (_, args) =>
    {
        if (args.Data is not null)
        {
            output.AppendLine(args.Data);
        }
    };
    process.ErrorDataReceived += (_, args) =>
    {
        if (args.Data is not null)
        {
            output.AppendLine(args.Data);
        }
    };
    process.Start();
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();

    if (!process.WaitForExit(20_000))
    {
        process.Kill(entireProcessTree: true);
        throw new TimeoutException("API did not exit after missing startup data.");
    }

    process.WaitForExit();
    if (process.ExitCode == 0)
    {
        throw new InvalidOperationException("Expected non-zero exit code when startup data is invalid.");
    }

    return output.ToString();
}

static async Task VerifyApiMalformedJson(HttpClient client)
{
    using var response = await client.PostAsync("/api/route", new StringContent("{ invalid json", Encoding.UTF8, "application/json"));
    AssertEqual(HttpStatusCode.BadRequest, response.StatusCode);
}

static async Task VerifyApiBusinessValidation(HttpClient client)
{
    var route = await PostRoute(client, new
    {
        order_id = "BAD-SHAPE",
        customer_zip = "abc",
        mail_order = false,
        items = Array.Empty<object>()
    });

    AssertFalse(route.Feasible);
    AssertContains("Order must include a valid customer_zip.", route.Errors!);
    AssertContains("Order must include at least one line item.", route.Errors!);
}

static async Task VerifyApiUnknownProduct(HttpClient client)
{
    var route = await PostRoute(client, new
    {
        order_id = "UNKNOWN-PRODUCT",
        customer_zip = "10015",
        mail_order = false,
        priority = "standard",
        items = new[] { new { product_code = "NO-SUCH-PRODUCT", quantity = 1 } }
    });

    AssertFalse(route.Feasible);
    AssertContains("Unknown product_code 'NO-SUCH-PRODUCT' in line item 1.", route.Errors!);
}

static async Task VerifyApiInvalidPriority(HttpClient client)
{
    var route = await PostRoute(client, new
    {
        order_id = "BAD-PRIORITY",
        customer_zip = "10015",
        mail_order = false,
        priority = "urgent",
        items = new[] { new { product_code = "WC-STD-001", quantity = 1 } }
    });

    AssertFalse(route.Feasible);
    AssertContains("priority must be one of: rush, standard.", route.Errors!);
}

static async Task VerifyApiMailOrderModes(HttpClient client)
{
    var localOnly = await PostRoute(client, new
    {
        order_id = "LOCAL-ONLY",
        customer_zip = "00001",
        mail_order = false,
        priority = "standard",
        items = new[] { new { product_code = "WC-SENT-001", quantity = 1 } }
    });

    AssertTrue(localOnly.Feasible);
    AssertEqual("SUP-T001", localOnly.Routing![0].SupplierId);
    AssertEqual("local", localOnly.Routing[0].Items[0].FulfillmentMode);

    var mailAllowed = await PostRoute(client, new
    {
        order_id = "MAIL-ALLOWED",
        customer_zip = "00001",
        mail_order = true,
        priority = "standard",
        items = new[] { new { product_code = "WC-SENT-001", quantity = 1 } }
    });

    AssertTrue(mailAllowed.Feasible);
    AssertEqual("mail_order", mailAllowed.Routing![0].Items[0].FulfillmentMode);
}

static async Task VerifyApiRejectsOversizedBody(HttpClient client)
{
    var request = new
    {
        order_id = "OVERSIZED",
        customer_zip = "10015",
        mail_order = false,
        priority = "standard",
        notes = new string('x', 1_100_000),
        items = new[] { new { product_code = "WC-STD-001", quantity = 1 } }
    };

    using var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
    using var response = await client.PostAsync("/api/route", content);
    AssertEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
}

static async Task<RouteOrderResponse> PostRoute(HttpClient client, object request)
{
    using var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
    using var response = await client.PostAsync("/api/route", content);
    AssertEqual(HttpStatusCode.OK, response.StatusCode);
    var route = await response.Content.ReadFromJsonAsync<RouteOrderResponse>();
    AssertNotNull(route);
    return route!;
}

static async Task<int> RunStressTest(string[] args)
{
    var root = FindRepoRoot();
    var orderPath = GetOption(args, "--orders") ?? Path.Combine(root, "test_data", "performance_orders.json");
    var concurrency = int.Parse(GetOption(args, "--concurrency") ?? "25");
    var port = int.Parse(GetOption(args, "--port") ?? "18090");

    var orders = JsonSerializer.Deserialize<List<JsonElement>>(File.ReadAllText(orderPath), new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    }) ?? [];

    if (orders.Count == 0)
    {
        throw new InvalidOperationException($"No orders found in {orderPath}.");
    }

    using var process = new Process();
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

    try
    {
        using var client = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
            Timeout = TimeSpan.FromSeconds(60)
        };
        await WaitForHealth(client);

        var stopwatch = Stopwatch.StartNew();
        var results = new List<StressResult>();
        var nextIndex = 0;
        var gate = new object();
        var workers = Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
        {
            while (true)
            {
                int index;
                lock (gate)
                {
                    if (nextIndex >= orders.Count)
                    {
                        return;
                    }

                    index = nextIndex++;
                }

                var order = orders[index];
                var started = Stopwatch.GetTimestamp();
                HttpStatusCode statusCode;
                bool feasible;
                int shipmentCount;
                int errorCount;
                string? error = null;

                try
                {
                    using var content = new StringContent(order.GetRawText(), Encoding.UTF8, "application/json");
                    using var response = await client.PostAsync("/api/route", content);
                    statusCode = response.StatusCode;
                    var route = await response.Content.ReadFromJsonAsync<RouteOrderResponse>();
                    feasible = route?.Feasible ?? false;
                    shipmentCount = route?.Routing?.Count ?? 0;
                    errorCount = route?.Errors?.Count ?? 0;
                }
                catch (Exception exception)
                {
                    statusCode = 0;
                    feasible = false;
                    shipmentCount = 0;
                    errorCount = 1;
                    error = exception.GetType().Name + ": " + exception.Message;
                }

                var elapsed = Stopwatch.GetElapsedTime(started);
                lock (results)
                {
                    results.Add(new StressResult(index, statusCode, feasible, shipmentCount, errorCount, elapsed.TotalMilliseconds, error));
                }
            }
        })).ToArray();

        await Task.WhenAll(workers);
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
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
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

static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
{
    if (sortedValues.Count == 0)
    {
        return 0;
    }

    var index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
    return sortedValues[Math.Clamp(index, 0, sortedValues.Count - 1)];
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
    var root = document.RootElement;
    AssertEqual("3.0.3", root.GetProperty("openapi").GetString());

    var paths = root.GetProperty("paths");
    AssertTrue(paths.TryGetProperty("/health", out var healthPath));
    AssertTrue(healthPath.TryGetProperty("get", out _));
    AssertTrue(paths.TryGetProperty("/api/route", out var routePath));
    AssertTrue(routePath.TryGetProperty("post", out var routePost));

    var schema = routePost
        .GetProperty("requestBody")
        .GetProperty("content")
        .GetProperty("application/json")
        .GetProperty("schema");
    AssertJsonArrayContains(schema.GetProperty("required"), "customer_zip");
    AssertJsonArrayContains(schema.GetProperty("required"), "mail_order");
    AssertJsonArrayContains(schema.GetProperty("required"), "items");

    var priority = schema.GetProperty("properties").GetProperty("priority");
    AssertJsonArrayContains(priority.GetProperty("enum"), "rush");
    AssertJsonArrayContains(priority.GetProperty("enum"), "standard");

    var items = schema.GetProperty("properties").GetProperty("items").GetProperty("items");
    AssertJsonArrayContains(items.GetProperty("required"), "product_code");
    AssertJsonArrayContains(items.GetProperty("required"), "quantity");

    var examples = routePost
        .GetProperty("responses")
        .GetProperty("200")
        .GetProperty("content")
        .GetProperty("application/json")
        .GetProperty("examples");
    AssertTrue(examples.TryGetProperty("success", out _));
    AssertTrue(examples.TryGetProperty("infeasible", out _));
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

static int GetFreePort()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
}

static RouteOrderResponse Route(CsvProductRepository products, CsvSupplierRepository suppliers, RouteOrderRequest request)
{
    var validation = new OrderValidator(products).Validate(request);
    AssertTrue(validation.IsValid);
    return new OrderRouter(suppliers, Config(("Routing:LocalRatingSimilarityDelta", "0.5"))).Route(validation.Order!);
}

static CsvProductRepository ProductRepository(params (string Code, string Name, string Category)[] products)
{
    var path = TempFile("products", "product_code,product_name,category\r\n" + string.Join("\r\n", products.Select(product => $"{product.Code},{product.Name},{product.Category}")));
    return CsvProductRepository.Load(path, LoggerFactory.Create(_ => { }).CreateLogger("test"));
}

static string TempFile(string prefix, string content)
{
    var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.csv");
    File.WriteAllText(path, content);
    return path;
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

static void AssertJsonArrayContains(JsonElement array, string expected)
{
    foreach (var item in array.EnumerateArray())
    {
        if (string.Equals(item.GetString(), expected, StringComparison.Ordinal))
        {
            return;
        }
    }

    throw new InvalidOperationException($"Expected JSON array to contain '{expected}'.");
}

static void AssertSequence(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
{
    if (!expected.SequenceEqual(actual))
    {
        throw new InvalidOperationException($"Expected sequence '{string.Join(", ", expected)}', got '{string.Join(", ", actual)}'.");
    }
}

static void AssertThrows<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    catch (Exception exception)
    {
        throw new InvalidOperationException($"Expected {typeof(TException).Name}, got {exception.GetType().Name}.", exception);
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}, but no exception was thrown.");
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
