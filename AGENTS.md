# Repository Guide

This repository contains a containerized C# ASP.NET Core Minimal API order routing service.

## Project Layout

- `src/OrderRouting.Api`: service implementation.
- `tests/OrderRouting.UnitTests`: fast xUnit tests for validation, CSV parsing/loading, ZIP coverage, supplier eligibility, routing decisions, and scheduling.
- `tests/OrderRouting.IntegrationTests`: xUnit API/process tests for startup failure behavior, Swagger/OpenAPI, headers, body limits, and route smoke coverage.
- `tools/OrderRouting.Diagnostics`: operational diagnostics console for stress testing and service-data audits.
- `service_data/products.csv`: product code to category data.
- `service_data/suppliers.csv`: supplier capabilities, ZIP coverage, ratings, and mail-order flags.
- `test_data/sample_orders.json`: sample route requests.
- `test_data/performance_orders.json`: expanded stress-test orders.
- `design.md`: implementation design and resolved decisions.

## Build And Test

Run from the repository root:

```powershell
dotnet build OrderRouting.sln
dotnet test tests\OrderRouting.UnitTests\OrderRouting.UnitTests.csproj --no-build
dotnet test tests\OrderRouting.IntegrationTests\OrderRouting.IntegrationTests.csproj --no-build
```

Solution-level test discovery should also work:

```powershell
dotnet test OrderRouting.sln --no-build
```

## Diagnostics

Run the performance stress test:

```powershell
dotnet run --project tools\OrderRouting.Diagnostics\OrderRouting.Diagnostics.csproj -- stress --orders test_data\performance_orders.json --concurrency 25
```

Run the exhaustive service-data audit:

```powershell
dotnet run --project tools\OrderRouting.Diagnostics\OrderRouting.Diagnostics.csproj -- data-audit
```

The audit reports product/supplier category coverage, per-category supplier counts, local regional coverage samples, every-product routing results, and every-supplier local eligibility validation.

## Run The Service

Docker is the primary run path:

```powershell
docker build -t order-routing-service .
docker run --rm -p 8080:8080 order-routing-service
```

For local development without Docker:

```powershell
dotnet run --project src\OrderRouting.Api\OrderRouting.Api.csproj
```

Default port is `8080`. Use `PORT` to override when `ASPNETCORE_URLS` is not set.

## Data Loading

The service loads `service_data/products.csv` and `service_data/suppliers.csv` at startup and exits non-zero on missing or malformed required data. Supplier CSV input is intentionally messy:

- `suplier_name` is the source header spelling and must be supported, along with corrected `supplier_name`.
- `service_zips` may contain exact ZIPs and inclusive ranges.
- Short ZIP values are left-padded to five digits.
- `product_categories` contains comma-separated values inside a CSV field.
- `customer_satisfaction_score` is numeric or `no ratings yet`.
- `can_mail_order?` is `y` or `n`.
- Required CSV values that begin with spreadsheet formula prefixes are rejected to reduce formula-injection risk.

CSV loading streams rows to avoid full-file transient allocations, but normalized product and supplier data are intentionally kept in memory for fast local routing.

## API Behavior

- `GET /health` returns a basic health response.
- `GET /swagger` returns local interactive API documentation.
- `GET /openapi.json` returns the OpenAPI document used by the interactive docs.
- `POST /api/route` returns HTTP 200 for parsed business requests.
- Validation or routing failures use `{ "feasible": false, "errors": [...] }`.
- Malformed JSON returns HTTP 400.
- Unexpected request exceptions are logged and return a generic HTTP 500 problem response.

## Routing Rules

Supplier eligibility requires category support plus local ZIP coverage or allowed mail order. Routing optimization order:

1. Feasibility.
2. Fewer shipments.
3. Higher quantity-weighted customer satisfaction score.
4. Local fulfillment over mail order when ratings are similar.
5. Deterministic supplier ID tie-break.

Supported priorities are `rush` and `standard`. Omitted or blank priority defaults to `standard`; unknown values fail validation and list the allowed values.

Routing uses a bounded exact search for normal orders and a deterministic greedy fallback if the configured search node budget is exceeded.

## Docker

The image includes the required `service_data` files and runs as the non-root `app` user from the .NET runtime image.
