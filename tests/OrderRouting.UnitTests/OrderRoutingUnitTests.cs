using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OrderRouting.Api.Data;
using OrderRouting.Api.Models;
using OrderRouting.Api.Routing;
using Xunit;

namespace OrderRouting.UnitTests;

public sealed class OrderRoutingUnitTests
{
    [Fact]
    public Task RequestValidationReturnsMultipleErrors()
    {
        var validator = new OrderValidator(ProductRepository(("P1", "Product 1", "cpap")));
        var result = validator.Validate(new RouteOrderRequest("ORD", "abc", false, null, []));
        AssertFalse(result.IsValid);
        AssertContains("Order must include a valid customer_zip.", result.Errors);
        AssertContains("Order must include at least one line item.", result.Errors);
        return Task.CompletedTask;
    }

    [Fact]
    public Task InvalidPriorityListsAllowedValues()
    {
        var validator = new OrderValidator(ProductRepository(("P1", "Product 1", "cpap")));
        var result = validator.Validate(new RouteOrderRequest("ORD", "10001", false, "urgent", [new("P1", 1)]));
        AssertFalse(result.IsValid);
        AssertContains("priority must be one of: rush, standard.", result.Errors);
        return Task.CompletedTask;
    }

    [Fact]
    public Task ProductLookupIsCaseInsensitive()
    {
        var repository = ProductRepository(("CP-STD-031", "CPAP Machine", "CPAP"));
        var product = repository.Find("cp-std-031");
        AssertNotNull(product);
        AssertEqual("cpap", product!.Category);
        return Task.CompletedTask;
    }

    [Fact]
    public Task DuplicateProductRowsAreHandledCorrectly()
    {
        var duplicatePath = TempFile("products", "product_code,product_name,category\r\nP1,Product 1,CPAP\r\nP1,Product 1,CPAP");
        var repository = CsvProductRepository.Load(duplicatePath, LoggerFactory.Create(_ => { }).CreateLogger("test"));
        AssertEqual(1, repository.Count);

        var conflictingPath = TempFile("products", "product_code,product_name,category\r\nP1,Product 1,CPAP\r\nP1,Product 1,wheelchair");
        AssertThrows<DataLoadException>(() => CsvProductRepository.Load(conflictingPath, LoggerFactory.Create(_ => { }).CreateLogger("test")));
        return Task.CompletedTask;
    }

    [Fact]
    public Task CsvParserHandlesQuotedFieldsAndLineEndings()
    {
        var records = CsvTableReader.Parse("a,b,c\r\n\"one, two\",\"say \"\"hi\"\"\",three\nlast,,");
        AssertEqual(3, records.Count);
        AssertEqual("one, two", records[1][0]);
        AssertEqual("say \"hi\"", records[1][1]);
        AssertEqual("", records[2][1]);
        AssertEqual("", records[2][2]);
        return Task.CompletedTask;
    }

    [Fact]
    public Task CsvParserRejectsUnterminatedQuotes()
    {
        AssertThrows<DataLoadException>(() => CsvTableReader.Parse("a,b\r\n\"unterminated,b"));
        return Task.CompletedTask;
    }

    [Fact]
    public Task CsvLoadersRejectMalformedData()
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

    [Fact]
    public Task CsvLoadersRejectSpreadsheetFormulaInjectionPrefixes()
    {
        AssertThrows<DataLoadException>(() => CsvProductRepository.Load(
            TempFile("products", "product_code,product_name,category\r\n=CMD,Injected Product,wheelchair"),
            LoggerFactory.Create(_ => { }).CreateLogger("test")));

        AssertThrows<DataLoadException>(() => CsvSupplierRepository.Load(
            TempFile("suppliers", "supplier_id,suplier_name,service_zips,product_categories,customer_satisfaction_score,can_mail_order?\r\nSUP-FORMULA,@Injected Supplier,10001,wheelchair,8,y")));

        AssertThrows<DataLoadException>(() => CsvSupplierRepository.Load(
            TempFile("suppliers", "supplier_id,suplier_name,service_zips,product_categories,customer_satisfaction_score,can_mail_order?\r\nSUP-FORMULA,Supplier,10001,+wheelchair,8,y")));

        return Task.CompletedTask;
    }

    [Fact]
    public Task ZipCoverageParsesValuesAndRanges()
    {
        var coverage = ZipCoverage.Parse("10001, 10005-10007", "test");
        AssertTrue(coverage.Contains("10001"));
        AssertTrue(coverage.Contains("10006"));
        AssertFalse(coverage.Contains("10008"));
        return Task.CompletedTask;
    }

    [Fact]
    public Task ZipCoverageLeftPadsShortZips()
    {
        var coverage = ZipCoverage.Parse("2110-2112", "test");
        AssertTrue(coverage.Contains("02111"));
        AssertFalse(coverage.Contains("02113"));
        return Task.CompletedTask;
    }

    [Fact]
    public Task SupplierEligibilityHandlesModes()
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

    [Fact]
    public Task RouterConsolidatesShipments()
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

    [Fact]
    public Task RouterPrefersHigherRating()
    {
        var products = ProductRepository(("WC", "Wheelchair", "wheelchair"));
        var suppliers = SupplierRepository(
            Supplier("SUP-LOW", "Low", "10001", "wheelchair", 6m, false),
            Supplier("SUP-HIGH", "High", "10001", "wheelchair", 9m, false));
        var response = Route(products, suppliers, new("ORD", "10001", false, "standard", [new("WC", 1)]));
        AssertEqual("SUP-HIGH", response.Routing![0].SupplierId);
        return Task.CompletedTask;
    }

    [Fact]
    public Task RouterUsesQuantityWeightedRating()
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

    [Fact]
    public Task RouterPrefersLocalWhenRatingsAreSimilar()
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

    [Fact]
    public Task RouterUsesSupplierIdTiebreak()
    {
        var products = ProductRepository(("CN", "Cane", "cane"));
        var suppliers = SupplierRepository(
            Supplier("SUP-B", "B", "10001", "cane", 8m, false),
            Supplier("SUP-A", "A", "10001", "cane", 8m, false));
        var response = Route(products, suppliers, new("ORD", "10001", false, "standard", [new("CN", 1)]));
        AssertEqual("SUP-A", response.Routing![0].SupplierId);
        return Task.CompletedTask;
    }

    [Fact]
    public Task RouterResponseOrderIsDeterministic()
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

    [Fact]
    public Task RouterReportsInfeasibleOrders()
    {
        var products = ProductRepository(("WC", "Wheelchair", "wheelchair"));
        var suppliers = SupplierRepository(Supplier("SUP-C", "Cane", "10001", "cane", 8m, false));
        var response = Route(products, suppliers, new("ORD", "10001", false, "standard", [new("WC", 1)]));
        AssertFalse(response.Feasible);
        AssertContains("No eligible supplier can fulfill product WC for category wheelchair at ZIP 10001.", response.Errors!);
        return Task.CompletedTask;
    }

    [Fact]
    public Task RouterFallsBackWhenExactSearchBudgetIsExceeded()
    {
        var products = ProductRepository(("A", "Product A", "category-a"), ("B", "Product B", "category-b"), ("C", "Product C", "category-c"));
        var suppliers = SupplierRepository(
            Supplier("SUP-G", "Generalist", "10001", "category-a, category-b, category-c", 7m, false),
            Supplier("SUP-A", "A", "10001", "category-a", 10m, false),
            Supplier("SUP-B", "B", "10001", "category-b", 10m, false),
            Supplier("SUP-C", "C", "10001", "category-c", 10m, false));

        var validation = new OrderValidator(products).Validate(new("ORD", "10001", false, "standard", [new("A", 1), new("B", 1), new("C", 1)]));
        AssertTrue(validation.IsValid);
        var response = new OrderRouter(suppliers, Config(("Routing:MaxSearchNodes", "1"))).Route(validation.Order!);

        AssertTrue(response.Feasible);
        AssertEqual(1, response.Routing!.Count);
        AssertEqual("SUP-G", response.Routing[0].SupplierId);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SchedulerProcessesRushFirst()
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

    [Fact]
    public async Task SchedulerPreservesFifo()
    {
        using var scheduler = new RouteRequestScheduler(Config(("Routing:MaxQueuedRequests", "10")));
        var completed = new List<string>();
        await Task.WhenAll(
            scheduler.EnqueueAsync(OrderPriority.Standard, _ => { completed.Add("a"); return Task.FromResult(RouteOrderResponse.Failure(["a"])); }, CancellationToken.None),
            scheduler.EnqueueAsync(OrderPriority.Standard, _ => { completed.Add("b"); return Task.FromResult(RouteOrderResponse.Failure(["b"])); }, CancellationToken.None));
        AssertSequence(["a", "b"], completed);
    }

    [Fact]
    public async Task SchedulerReportsCapacity()
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

    private static RouteOrderResponse Route(CsvProductRepository products, CsvSupplierRepository suppliers, RouteOrderRequest request)
    {
        var validation = new OrderValidator(products).Validate(request);
        AssertTrue(validation.IsValid);
        return new OrderRouter(suppliers, Config(("Routing:LocalRatingSimilarityDelta", "0.5"))).Route(validation.Order!);
    }

    private static CsvProductRepository ProductRepository(params (string Code, string Name, string Category)[] products)
    {
        var path = TempFile("products", "product_code,product_name,category\r\n" + string.Join("\r\n", products.Select(product => $"{product.Code},{product.Name},{product.Category}")));
        return CsvProductRepository.Load(path, LoggerFactory.Create(_ => { }).CreateLogger("test"));
    }

    private static string TempFile(string prefix, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, content);
        return path;
    }

    private static CsvSupplierRepository SupplierRepository(params Supplier[] suppliers)
    {
        return new CsvSupplierRepository(suppliers);
    }

    private static Supplier Supplier(string id, string name, string zips, string categories, decimal? rating, bool canMail)
    {
        return new Supplier(
            id,
            name,
            ZipCoverage.Parse(zips, "test"),
            categories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(CategoryNormalizer.Normalize).ToHashSet(StringComparer.Ordinal),
            rating,
            canMail);
    }

    private static IConfiguration Config(params (string Key, string Value)[] values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value))).Build();
    }

    private static void AssertTrue(bool value)
    {
        if (!value)
        {
            throw new InvalidOperationException("Expected true.");
        }
    }

    private static void AssertFalse(bool value)
    {
        if (value)
        {
            throw new InvalidOperationException("Expected false.");
        }
    }

    private static void AssertNotNull(object? value)
    {
        if (value is null)
        {
            throw new InvalidOperationException("Expected non-null value.");
        }
    }

    private static void AssertNull(object? value)
    {
        if (value is not null)
        {
            throw new InvalidOperationException("Expected null value.");
        }
    }

    private static void AssertEqual<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }

    private static void AssertContains(string expected, IReadOnlyList<string> values)
    {
        if (!values.Contains(expected))
        {
            throw new InvalidOperationException($"Expected list to contain '{expected}'. Actual: {string.Join(" | ", values)}");
        }
    }

    private static void AssertSequence(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException($"Expected sequence '{string.Join(", ", expected)}', got '{string.Join(", ", actual)}'.");
        }
    }

    private static void AssertThrows<TException>(Action action)
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
}
