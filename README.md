# Order Routing Service

Containerized C# service for routing durable medical equipment orders to eligible suppliers.

## Prerequisites

- Docker
- .NET 10 SDK, only for local development, tests, and diagnostics

This project targets `net10.0` because the available local SDK/runtime in this workspace is .NET 10.

## Run With Docker

Build the image:

```powershell
docker build -t order-routing-service .
```

Run the container:

```powershell
docker run --rm -p 8080:8080 order-routing-service
```

The API listens on port `8080` by default. Verify from another shell:

```powershell
curl http://localhost:8080/health
```

To override runtime settings, pass environment variables into the container:

```powershell
docker run --rm -p 8080:8080 `
  -e Routing__MaxQueuedRequests=200 `
  order-routing-service
```

## Developer Local Run

For local development without Docker, run the API from the .NET CLI.

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

The test suite is split into unit and integration projects so fast unit tests can run in CI while API/process integration tests can run in a CD or gated rollout stage.

```powershell
dotnet test tests\OrderRouting.UnitTests\OrderRouting.UnitTests.csproj
dotnet test tests\OrderRouting.IntegrationTests\OrderRouting.IntegrationTests.csproj
```

Unit tests cover validation, product lookup, ZIP parsing, supplier eligibility, routing decisions, and priority scheduling. Integration tests start the API locally and cover startup failures, OpenAPI/Swagger, request validation, body limits, and a `POST /api/route` smoke test.

## Stress Test

Run the reusable stress test against the generated 500-order data set:

```powershell
dotnet run --project tools\OrderRouting.Diagnostics\OrderRouting.Diagnostics.csproj -- stress --orders test_data\performance_orders.json --concurrency 25
```

The diagnostics runner starts the API locally, sends requests concurrently, and prints latency/throughput metrics. Current findings are documented in `docs/performance-findings.md`.

## Data Audit

Run the exhaustive data audit:

```powershell
dotnet run --project tools\OrderRouting.Diagnostics\OrderRouting.Diagnostics.csproj -- data-audit
```

The audit loads all service data, checks product/supplier category coverage, routes every unique product, validates every supplier against at least one known product/category/local ZIP, reports per-category coverage counts, and samples local coverage across major ZIP regions. Current findings are documented in `docs/data-audit-findings.md`.

## Security

Security review findings and production hardening notes are documented in `docs/security-findings.md`.

## Routing Notes

- Supported priorities are `rush` and `standard`.
- Omitted or blank priority defaults to `standard`.
- Unknown priority values are validation errors that list the available values.
- The router prioritizes feasibility, fewer shipments, higher quantity-weighted supplier rating, local fulfillment when ratings are similar, then supplier ID tie-breaking.
- Unrated suppliers rank after all rated suppliers.
- Identical duplicate product rows are tolerated; conflicting duplicates fail startup.
- CSV values that begin with spreadsheet formula prefixes are rejected at startup to reduce CSV/formula injection risk.

## Data Size Notes

The service intentionally keeps normalized product and supplier data in memory after startup so routing does not depend on external services or repeated disk reads. CSV loading is streamed row-by-row to avoid reading entire large files into memory before building those indexes. If the data grows far beyond this assessment scale, monitor process memory and consider moving supplier/product lookup to a database or adding category-specific indexes based on measured bottlenecks.
