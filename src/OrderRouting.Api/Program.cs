using Microsoft.AspNetCore.Http.Json;
using OrderRouting.Api;
using OrderRouting.Api.Data;
using OrderRouting.Api.Models;
using OrderRouting.Api.Routing;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
});

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    var port = Environment.GetEnvironmentVariable("PORT");
    builder.WebHost.UseUrls($"http://0.0.0.0:{(string.IsNullOrWhiteSpace(port) ? "8080" : port)}");
}

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 1_048_576;
});

builder.Services.AddSingleton(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<CsvProductRepository>();
    var path = ResolveDataPath(configuration.GetValue<string>("Data:ProductsPath") ?? "service_data/products.csv");
    var repository = CsvProductRepository.Load(path, logger);
    logger.LogInformation("Loaded {ProductCount} products from {Path}.", repository.Count, path);
    return repository;
});

builder.Services.AddSingleton(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<CsvSupplierRepository>();
    var path = ResolveDataPath(configuration.GetValue<string>("Data:SuppliersPath") ?? "service_data/suppliers.csv");
    var repository = CsvSupplierRepository.Load(path);
    logger.LogInformation("Loaded {SupplierCount} suppliers from {Path}.", repository.Count, path);
    return repository;
});

builder.Services.AddSingleton<OrderValidator>();
builder.Services.AddSingleton<OrderRouter>();
builder.Services.AddSingleton<RouteRequestScheduler>();

var app = builder.Build();

try
{
    _ = app.Services.GetRequiredService<CsvProductRepository>();
    _ = app.Services.GetRequiredService<CsvSupplierRepository>();
}
catch (DataLoadException exception)
{
    app.Logger.LogCritical(exception, "Failed to load startup data.");
    Environment.ExitCode = 1;
    return;
}

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exceptionFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        if (exceptionFeature?.Error is not null)
        {
            app.Logger.LogError(exceptionFeature.Error, "Unhandled request exception.");
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await Results.Problem(
            title: "An unexpected error occurred.",
            detail: "The request could not be completed.",
            statusCode: StatusCodes.Status500InternalServerError)
            .ExecuteAsync(context);
    });
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/openapi.json", ApiDocumentation.OpenApiJson);
app.MapGet("/swagger", ApiDocumentation.SwaggerPage);

app.MapPost("/api/route", async (
    RouteOrderRequest request,
    OrderValidator validator,
    OrderRouter router,
    RouteRequestScheduler scheduler,
    ILogger<Program> logger,
    HttpContext httpContext) =>
{
    var validation = validator.Validate(request);
    if (!validation.IsValid)
    {
        logger.LogWarning("Route request {OrderId} failed validation with {ErrorCount} errors.", request.OrderId, validation.Errors.Count);
        return Results.Ok(RouteOrderResponse.Failure(validation.Errors));
    }

    var order = validation.Order!;
    logger.LogInformation("Route request {OrderId} accepted with priority {Priority}.", order.OrderId, order.Priority);
    var queuedAt = TimeProvider.System.GetTimestamp();

    var response = await scheduler.EnqueueAsync(
        order.Priority,
        _ =>
        {
            var elapsed = TimeProvider.System.GetElapsedTime(queuedAt);
            logger.LogInformation("Routing request {OrderId} after queue wait {QueueWaitMs}ms.", order.OrderId, elapsed.TotalMilliseconds);
            return Task.FromResult(router.Route(order));
        },
        httpContext.RequestAborted);

    if (!response.Feasible)
    {
        logger.LogInformation("Route request {OrderId} completed infeasible with {ErrorCount} errors.", order.OrderId, response.Errors?.Count ?? 0);
    }

    return Results.Ok(response);
});

app.Run();

static string ResolveDataPath(string path)
{
    if (Path.IsPathRooted(path))
    {
        return path;
    }

    var currentDirectoryCandidate = Path.GetFullPath(path, Directory.GetCurrentDirectory());
    if (File.Exists(currentDirectoryCandidate))
    {
        return currentDirectoryCandidate;
    }

    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory is not null)
    {
        var candidate = Path.GetFullPath(path, directory.FullName);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        directory = directory.Parent;
    }

    return currentDirectoryCandidate;
}

public partial class Program
{
}
