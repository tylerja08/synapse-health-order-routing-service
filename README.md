# Order Routing Service

Containerized C# service for routing durable medical equipment orders to eligible suppliers.

## Prerequisites

- .NET 10 SDK
- Docker

This project targets `net10.0` because the available local SDK/runtime in this workspace is .NET 10.

## Run Locally

From the repository root:

```powershell
dotnet build OrderRouting.sln
dotnet run --project src\OrderRouting.Api\OrderRouting.Api.csproj
```

The API listens on port `8080` by default.

## Configuration

| Environment variable | Default | Purpose |
| --- | --- | --- |
| `PORT` | `8080` | Local HTTP port when `ASPNETCORE_URLS` is not set. |
| `Data__ProductsPath` | `service_data/products.csv` | Product CSV path. |
| `Data__SuppliersPath` | `service_data/suppliers.csv` | Supplier CSV path. |
| `Routing__LocalRatingSimilarityDelta` | `0.5` | Rating delta where local fulfillment wins over mail order. |
| `Routing__MaxQueuedRequests` | `100` | Maximum queued route requests. |

## Health Check

```powershell
curl http://localhost:8080/health
```

## Interactive API Docs

Open the local interactive API page in a browser:

```text
http://localhost:8080/swagger
```

The OpenAPI document is available at:

```text
http://localhost:8080/openapi.json
```

## Route An Order

```powershell
curl -Method POST http://localhost:8080/api/route `
  -ContentType "application/json" `
  -Body '{
    "order_id": "ORD-EXAMPLE",
    "customer_zip": "10015",
    "mail_order": false,
    "priority": "standard",
    "items": [
      { "product_code": "WC-STD-001", "quantity": 1 },
      { "product_code": "OX-PORT-024", "quantity": 1 }
    ]
  }'
```

Business validation and routing failures return HTTP 200 with `feasible: false`. Malformed JSON returns HTTP 400.

## Tests

The test project uses xUnit and can be run from Visual Studio, VS Code, or the .NET CLI:

```powershell
dotnet test OrderRouting.sln
```

It covers validation, product lookup, ZIP parsing, supplier eligibility, routing decisions, priority scheduling, and an HTTP smoke test for `POST /api/route`.

## Stress Test

Run the reusable stress test against the generated 500-order data set:

```powershell
dotnet run --project tests\OrderRouting.Tests\OrderRouting.Tests.csproj -- --stress --orders test_data\performance_orders.json --concurrency 25
```

The runner starts the API locally, sends requests concurrently, and prints latency/throughput metrics. Current findings are documented in `docs/performance-findings.md`.

## Data Audit

Run the exhaustive data audit:

```powershell
dotnet run --project tests\OrderRouting.Tests\OrderRouting.Tests.csproj -- --data-audit
```

The audit loads all service data, checks product/supplier category coverage, routes every unique product, validates every supplier against at least one known product/category/local ZIP, reports per-category coverage counts, and samples local coverage across major ZIP regions. Current findings are documented in `docs/data-audit-findings.md`.

## Docker

Build the image:

```powershell
docker build -t order-routing-service .
```

Run the container:

```powershell
docker run --rm -p 8080:8080 order-routing-service
```

Verify from another shell:

```powershell
curl http://localhost:8080/health
```

## Routing Notes

- Supported priorities are `rush` and `standard`.
- Omitted or blank priority defaults to `standard`.
- Unknown priority values are validation errors that list the available values.
- The router prioritizes feasibility, fewer shipments, higher quantity-weighted supplier rating, local fulfillment when ratings are similar, then supplier ID tie-breaking.
- Unrated suppliers rank after all rated suppliers.
- Identical duplicate product rows are tolerated; conflicting duplicates fail startup.

## Data Size Notes

The service intentionally keeps normalized product and supplier data in memory after startup so routing does not depend on external services or repeated disk reads. CSV loading is streamed row-by-row to avoid reading entire large files into memory before building those indexes. If the data grows far beyond this assessment scale, monitor process memory and consider moving supplier/product lookup to a database or adding category-specific indexes based on measured bottlenecks.
