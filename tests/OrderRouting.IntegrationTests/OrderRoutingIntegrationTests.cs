using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using OrderRouting.Api.Data;
using OrderRouting.Api.Models;
using Xunit;

public sealed class OrderRoutingIntegrationTests
{
    [Fact]
    public async Task ApiPostRouteSmokeTest()
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
            AssertEqual(HttpStatusCode.OK, response.StatusCode);

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

    [Fact]
    public Task ApiStartupFailsWhenProductDataFileIsMissing()
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

    [Fact]
    public Task ApiStartupFailsWhenSupplierDataFileIsMissing()
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

    [Fact]
    public Task ApiStartupFailsWhenSupplierDataIsMalformed()
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

    private static string RunApiExpectingStartupFailure(string productsPath, string suppliersPath)
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

        var text = output.ToString();
        if (text.Contains("Unhandled exception", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Expected startup failure to be logged without surfacing an unhandled exception.");
        }

        return text;
    }

    private static async Task VerifyApiMalformedJson(HttpClient client)
    {
        using var response = await client.PostAsync("/api/route", new StringContent("{ invalid json", Encoding.UTF8, "application/json"));
        AssertEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task VerifyApiBusinessValidation(HttpClient client)
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

    private static async Task VerifyApiUnknownProduct(HttpClient client)
    {
        var route = await PostRoute(client, new
        {
            order_id = "UNKNOWN-PRODUCT",
            customer_zip = "10001",
            mail_order = true,
            priority = "standard",
            items = new[] { new { product_code = "NO-SUCH-PRODUCT", quantity = 1 } }
        });

        AssertFalse(route.Feasible);
        AssertContains("Unknown product_code 'NO-SUCH-PRODUCT' in line item 1.", route.Errors!);
    }

    private static async Task VerifyApiInvalidPriority(HttpClient client)
    {
        var route = await PostRoute(client, new
        {
            order_id = "BAD-PRIORITY",
            customer_zip = "10001",
            mail_order = true,
            priority = "urgent",
            items = new[] { new { product_code = "WC-STD-001", quantity = 1 } }
        });

        AssertFalse(route.Feasible);
        AssertContains("priority must be one of: rush, standard.", route.Errors!);
    }

    private static async Task VerifyApiMailOrderModes(HttpClient client)
    {
        var localOnly = await PostRoute(client, new
        {
            order_id = "LOCAL-MODE",
            customer_zip = "10001",
            mail_order = false,
            priority = "standard",
            items = new[] { new { product_code = "WC-STD-001", quantity = 1 } }
        });

        AssertTrue(localOnly.Feasible);
        AssertEqual("local", localOnly.Routing![0].Items[0].FulfillmentMode);

        var mailAllowed = await PostRoute(client, new
        {
            order_id = "MAIL-MODE",
            customer_zip = "10001",
            mail_order = true,
            priority = "standard",
            items = new[] { new { product_code = "WC-STD-001", quantity = 1 } }
        });

        AssertTrue(mailAllowed.Feasible);
        AssertNotNull(mailAllowed.Routing);
    }

    private static async Task VerifyApiRejectsOversizedBody(HttpClient client)
    {
        var oversizedItems = Enumerable.Range(0, 90_000)
            .Select(_ => new { product_code = "WC-STD-001", quantity = 1 })
            .ToArray();
        var payload = JsonSerializer.Serialize(new
        {
            order_id = "OVERSIZED",
            customer_zip = "10001",
            mail_order = true,
            priority = "standard",
            items = oversizedItems
        });

        using var response = await client.PostAsync("/api/route", new StringContent(payload, Encoding.UTF8, "application/json"));
        AssertEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    private static async Task<RouteOrderResponse> PostRoute(HttpClient client, object request)
    {
        using var response = await client.PostAsJsonAsync("/api/route", request);
        AssertEqual(HttpStatusCode.OK, response.StatusCode);
        var route = await response.Content.ReadFromJsonAsync<RouteOrderResponse>();
        AssertNotNull(route);
        return route!;
    }

    private static async Task VerifyApiDocs(HttpClient client)
    {
        using var swagger = await client.GetAsync("/swagger");
        AssertEqual(HttpStatusCode.OK, swagger.StatusCode);
        var swaggerHtml = await swagger.Content.ReadAsStringAsync();
        if (!swaggerHtml.Contains("Order Routing Service API", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Swagger page did not contain expected title.");
        }

        using var openApi = await client.GetAsync("/openapi.json");
        AssertEqual(HttpStatusCode.OK, openApi.StatusCode);
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

    private static async Task WaitForHealth(HttpClient client)
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

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string TempFile(string prefix, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, content);
        return path;
    }

    private static string FindRepoRoot()
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

    private static void AssertJsonArrayContains(JsonElement array, string expected)
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
}
