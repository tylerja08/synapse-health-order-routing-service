# Repository Guide

This repository contains a C# ASP.NET Core Minimal API order routing service.

## Project Layout

- `src/OrderRouting.Api`: service implementation.
- `tests/OrderRouting.Tests`: no-dependency console test runner.
- `service_data/products.csv`: product code to category data.
- `service_data/suppliers.csv`: supplier capabilities, ZIP coverage, ratings, and mail-order flags.
- `test_data/sample_orders.json`: sample route requests.
- `design.md`: implementation design and resolved decisions.

## Build And Test

Run from the repository root:

```powershell
dotnet build OrderRouting.sln
dotnet run --project tests\OrderRouting.Tests\OrderRouting.Tests.csproj
```

## Run The Service

```powershell
dotnet run --project src\OrderRouting.Api\OrderRouting.Api.csproj
```

Default port is `8080`. Use `PORT` to override when `ASPNETCORE_URLS` is not set.

## Data Loading

The service loads `service_data/products.csv` and `service_data/suppliers.csv` at startup and fails fast on missing or malformed required data. Supplier CSV input is intentionally messy:

- `suplier_name` is the source header spelling and must be supported.
- `service_zips` may contain exact ZIPs and inclusive ranges.
- Short ZIP values are left-padded to five digits.
- `product_categories` contains comma-separated values inside a CSV field.
- `customer_satisfaction_score` is numeric or `no ratings yet`.
- `can_mail_order?` is `y` or `n`.

## API Behavior

- `GET /health` returns a basic health response.
- `POST /api/route` returns HTTP 200 for parsed business requests.
- Validation or routing failures use `{ "feasible": false, "errors": [...] }`.
- Malformed JSON returns HTTP 400.

## Routing Rules

Supplier eligibility requires category support plus local ZIP coverage or allowed mail order. Routing optimization order:

1. Feasibility.
2. Fewer shipments.
3. Higher quantity-weighted customer satisfaction score.
4. Local fulfillment over mail order when ratings are similar.
5. Deterministic supplier ID tie-break.

Supported priorities are `rush` and `standard`. Omitted or blank priority defaults to `standard`; unknown values fail validation and list the allowed values.

## Docker

```powershell
docker build -t order-routing-service .
docker run --rm -p 8080:8080 order-routing-service
```

The image includes the required `docs` data files and runs as the non-root `app` user from the .NET runtime image.
