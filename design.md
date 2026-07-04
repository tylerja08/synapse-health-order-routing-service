# Order Routing Service Implementation Design

## Purpose

Build a production-minded, containerized C# order routing service that runs locally and in Docker without external services. The first implementation phase will deliver the service behavior described in `prompts/requirements.md`, backed by product and supplier data in `service_data/`.

The service exposes:

- `POST /api/route` for routing decisions.
- `GET /health` for local and Docker smoke checks.
- `GET /swagger` for local interactive API execution.
- `GET /openapi.json` for the API document used by the interactive docs.

All business validation and routing failures return HTTP 200 with `feasible: false`, per the requirements. When multiple route requests are waiting at the same time, higher priority orders are routed before lower priority orders.

## Technology Choices

- Runtime: .NET 8 ASP.NET Core Minimal API.
- Language: C#.
- CSV parsing: CsvHelper, configured for RFC-style CSV handling, quoted fields, trimming, and clear parse errors.
- JSON: built-in `System.Text.Json` with snake_case request and response names.
- Tests: no-dependency console test runner with focused assertions and an integration-style smoke test that starts the local API and calls `POST /api/route`.
- Container: multi-stage Dockerfile using .NET SDK for build and ASP.NET runtime for execution.

The service will not require databases, queues, caches, or networked dependencies.

## Project Layout

```text
src/
  OrderRouting.Api/
    Program.cs
    appsettings.json
    Models/
      RouteOrderRequest.cs
      RouteOrderResponse.cs
      Product.cs
      Supplier.cs
      RoutingModels.cs
    Data/
      CsvProductRepository.cs
      CsvSupplierRepository.cs
      DataLoadException.cs
    Routing/
      OrderValidator.cs
      OrderRouter.cs
      RouteRequestScheduler.cs
      ZipCoverage.cs
      SupplierEligibility.cs
      CategoryNormalizer.cs
tests/
  OrderRouting.Tests/
    OrderValidatorTests.cs
    CsvRepositoryTests.cs
    ZipCoverageTests.cs
    SupplierEligibilityTests.cs
    OrderRouterTests.cs
    RouteApiTests.cs
service_data/
  products.csv
  suppliers.csv
test_data/
  sample_orders.json
Dockerfile
.dockerignore
README.md
AGENTS.md
```

This keeps the API thin and puts parsing, validation, and routing behind small, testable classes.

## Configuration

Configuration values:

| Name | Default | Purpose |
| --- | --- | --- |
| `PORT` | `8080` | HTTP listen port. |
| `DATA__PRODUCTS_PATH` | `service_data/products.csv` | Product CSV path when running from repo root. |
| `DATA__SUPPLIERS_PATH` | `service_data/suppliers.csv` | Supplier CSV path when running from repo root. |
| `ROUTING__LOCAL_RATING_SIMILARITY_DELTA` | `0.5` | Rating difference within which local fulfillment wins over mail order. |
| `ROUTING__MAX_QUEUED_REQUESTS` | `100` | Maximum in-process route requests waiting for priority scheduling. |

At startup the app loads and normalizes all product and supplier data. Missing or malformed data fails startup with a clear log message and non-zero process exit.

## Data Loading And Normalization

### Products

`service_data/products.csv` fields:

- `product_code`
- `product_name`
- `category`

Normalization:

- Trim all fields.
- Product code lookup is case-insensitive, but responses preserve the submitted product code casing if known.
- Category is normalized with trim plus invariant lower-case. This makes `CPAP`, `cpap`, and `Cpap` equivalent.
- Duplicate product codes are tolerated only when the duplicate rows are identical for routing purposes. Exact duplicates are logged and ignored after the first record. Conflicting duplicates are treated as malformed data because the service cannot safely choose between different product definitions.

### Suppliers

`service_data/suppliers.csv` fields:

- `supplier_id`
- `suplier_name`, intentionally misspelled in source data. The loader will support this header and optionally `supplier_name` for resilience.
- `service_zips`
- `product_categories`
- `customer_satisfaction_score`
- `can_mail_order?`

Normalization:

- Trim all fields.
- Supplier IDs remain strings and are used for deterministic tie-breaking.
- Product categories are parsed with the CSV parser first, then the category field is split on commas, trimmed, lower-cased, and stored in a set.
- `service_zips` is parsed as comma-separated ZIP tokens. Each token is either an exact ZIP or an inclusive range like `10001-10100`.
- ZIP matching preserves leading zeros. Internally ZIPs are validated as 5 digits for orders. Source ZIP tokens with fewer than 5 digits, such as `2110`, are left-padded to 5 digits for matching `02110`.
- `customer_satisfaction_score` is parsed as decimal 1.0 through 10.0. `no ratings yet` becomes unrated.
- Unrated suppliers are treated as unknown quality and rank after all rated suppliers. The implementation can represent this internally as `0.0` because valid ratings are 1.0 through 10.0. The response does not need to expose ratings unless requirements are expanded.
- `can_mail_order?` accepts `y` and `n` only after trim and lower-case.

Malformed required fields, invalid ranges, invalid ratings, or invalid mail-order values fail startup.

## Request Contract

`POST /api/route` accepts:

```json
{
  "order_id": "ORD-EXAMPLE",
  "customer_zip": "10015",
  "mail_order": false,
  "priority": "standard",
  "items": [
    { "product_code": "WC-STD-001", "quantity": 1 },
    { "product_code": "OX-PORT-024", "quantity": 1 }
  ]
}
```

`priority` is part of the request contract. Extra JSON fields, such as `notes` from `test_data/sample_orders.json`, are ignored.

Supported priorities:

- `rush`: highest priority.
- `standard`: default priority when omitted.

The service only gives special scheduling treatment to `rush`. `standard` is the default priority when the field is omitted or blank. Unknown priority values are validation errors so callers receive clear feedback about the supported values.

Validation rules:

- `customer_zip` is required and must be exactly 5 digits after trim.
- `priority` is optional. `rush` is treated as high priority; omitted or blank values are treated as `standard`. Any other value is invalid and returns an error such as `"priority must be one of: rush, standard."`
- `items` is required and must contain at least one item.
- Each line item must have a non-empty `product_code`.
- Each product code must exist in `products.csv`.
- Each quantity must be a positive integer.
- Validation returns all practical errors together.

Validation failure response:

```json
{
  "feasible": false,
  "errors": [
    "Order must include at least one line item.",
    "Order must include a valid customer_zip."
  ]
}
```

Malformed JSON returns HTTP 400 because the endpoint cannot safely construct the documented business response. All successfully parsed requests return HTTP 200.

## Priority Scheduling

`POST /api/route` remains a synchronous endpoint from the caller's perspective, but requests pass through a small in-process priority scheduler before routing. This gives concrete behavior to the requirement that higher priority orders route before lower priority orders without requiring an external queue service.

Scheduling rules:

- Requests are validated for basic shape and priority is normalized before entering the scheduler.
- Waiting requests are ordered by priority first, then by arrival sequence.
- `rush` requests are processed before `standard` requests already waiting in the scheduler.
- Requests with the same priority are processed FIFO.
- A request that is already actively routing is not preempted.
- If the waiting queue is full, the endpoint returns HTTP 200 with `feasible: false` and an error such as `"Routing service is temporarily at capacity. Please retry."`

This design gives deterministic behavior under concurrent load while preserving simple local execution. In a later production deployment, this component can be replaced by a durable priority queue without changing the routing engine.

## Response Contract

Successful response:

```json
{
  "feasible": true,
  "routing": [
    {
      "supplier_id": "SUP-005",
      "supplier_name": "Respiratory Care Co Co",
      "items": [
        {
          "product_code": "WC-STD-001",
          "quantity": 1,
          "category": "wheelchair",
          "fulfillment_mode": "local"
        }
      ]
    }
  ]
}
```

Unsuccessful routing response:

```json
{
  "feasible": false,
  "errors": [
    "No eligible supplier can fulfill product OX-PORT-024 for category oxygen at ZIP 10015."
  ]
}
```

Response ordering is deterministic:

- Route groups sorted by supplier ID.
- Items within each group preserve request order.
- Errors sorted by request item order, then by message text.

## Supplier Eligibility

For each line item, a supplier is eligible when:

1. The supplier supports the product category.
2. The supplier can fulfill through at least one allowed mode:
   - Local: supplier service ZIPs include `customer_zip`.
   - Mail order: request `mail_order` is true and supplier has `can_mail_order = true`.

When both modes are possible, the routed item uses `fulfillment_mode: "local"` because local is preferred and more specific.

When `mail_order` is false, only local suppliers are eligible.

## Routing Algorithm

The router optimizes in this priority order:

1. Feasibility.
2. Fewer shipments.
3. Higher customer satisfaction score.
4. Local fulfillment over mail order when ratings are similar.
5. Deterministic tie-break by supplier ID.

Implementation approach:

1. Validate the request and resolve product categories.
2. Build eligible supplier candidates for each request item.
3. If any item has no candidates, return `feasible: false` with item-specific errors.
4. Sort items by fewest candidates to reduce search work.
5. Use branch-and-bound assignment search:
   - Try assigning each item to an eligible supplier.
   - Track the current set of suppliers used as shipments.
   - Prune branches that already use more suppliers than the best plan found.
   - Prefer candidate ordering by local mode, rating descending, then supplier ID.
6. Compare complete plans with a scoring tuple:
   - Shipment count ascending.
   - Weighted average supplier rating descending, weighted by item quantity.
   - Local item count descending when rating difference is within `LOCAL_RATING_SIMILARITY_DELTA`.
   - Supplier ID list ascending.

This keeps consolidation ahead of rating, while still letting high-rated suppliers win among plans with the same shipment count.

For extremely large orders, the implementation can cap exhaustive search and fall back to a deterministic greedy set-cover strategy. The initial service can avoid the fallback unless tests or sample data demonstrate a need, because the expected order sizes are small.

## Routing Design Decisions

### Quantity-Weighted Ratings

When comparing complete routing plans with the same shipment count, supplier quality is scored with a quantity-weighted average. Each routed item contributes `supplier_rating * quantity`, divided by total routed quantity. Unrated suppliers use the unknown-quality value described above, so known ratings win over unknown ratings.

Pros:

- Reflects the customer impact of larger line quantities better than a simple per-supplier average.
- Avoids over-weighting a supplier that handles a single low-quantity item.
- Keeps the scoring deterministic and easy to explain in tests.

Cons:

- Quantity is only a proxy for customer impact; a single critical item may matter more than several routine items.
- Large quantities can dominate the rating score even when the order contains clinically important lower-quantity items.
- It does not account for supplier capacity or item-specific fulfillment complexity, because those fields are not present in the input data.

This is acceptable for the initial implementation because the available inputs include quantity but not clinical criticality, supplier inventory, or capacity.

### Duplicate Product Rows

Identical duplicate product rows are tolerated because the provided product data may contain repeated rows that do not change routing behavior. The loader keeps the first row and ignores later identical duplicates after logging them.

This avoids failing startup for harmless data repetition while still protecting correctness: if the same `product_code` appears with a different name or category, startup fails as malformed data because routing would otherwise become ambiguous.

## Infeasible Routing

The router should distinguish validation errors from routing errors.

Examples:

- Unknown product: validation error.
- Product category exists, but no supplier supports that category: routing error.
- Supplier supports the category, but none serve the ZIP and mail order is not allowed: routing error.
- Mail order is allowed, but no supplier for the category has mail-order eligibility or local coverage: routing error.

The response remains HTTP 200 with `feasible: false`.

## Logging And Observability

Startup logs:

- Product file path and loaded product count.
- Supplier file path and loaded supplier count.
- Data load failures with file, row, and field where possible.

Request logs:

- Route request received with `order_id` when present.
- Priority assigned to the route request.
- Queue wait time before routing begins.
- Validation failure count.
- Routing infeasibility count.
- Unexpected exceptions through ASP.NET Core exception handling.

No protected health information should be logged. The provided payload only contains product and ZIP data, but logs should still avoid full request bodies by default.

## Security Considerations

- Bind to the configured port only, with no external service credentials.
- Do not log raw request bodies.
- Validate request size with ASP.NET Core defaults or a modest explicit body limit.
- Treat CSV files as trusted deployment artifacts, but fail fast on malformed content.
- Run the Docker container as a non-root user.
- Keep image contents minimal and copy only compiled app output plus required data files.
- Return generic unexpected error responses while logging internal exception details locally.

## Docker Design

Dockerfile:

- Stage 1: build with `mcr.microsoft.com/dotnet/sdk:8.0`.
- Stage 2: run with `mcr.microsoft.com/dotnet/aspnet:8.0`.
- Copy published output.
- Copy `service_data/products.csv` and `service_data/suppliers.csv`.
- Set `ASPNETCORE_URLS=http://+:8080`.
- Set default data paths inside the image.
- Expose `8080`.
- Run as a non-root user supported by the .NET container image.

`.dockerignore` excludes:

- `.git`
- `bin/`
- `obj/`
- test results
- IDE files
- local environment files

## Testing Plan

Focused automated tests:

- Request validation:
  - Missing ZIP.
  - Invalid ZIP.
  - Invalid priority with an error listing `rush` and `standard`.
  - Missing items.
  - Unknown product code.
  - Invalid quantity.
- Product lookup:
  - Case-insensitive product code lookup.
  - Category normalization for `CPAP`.
- ZIP parsing:
  - Exact ZIP.
  - Inclusive range.
  - Leading-zero normalization for shorter source ZIPs.
  - Invalid malformed range.
- Supplier eligibility:
  - Local eligibility.
  - Mail-order eligibility.
  - Mail order denied when request does not allow it.
  - Local wins when both modes are possible.
- Routing:
  - Consolidates into fewer shipments when one supplier can handle all items.
  - Chooses higher-rated supplier when shipment count is equal.
  - Prefers local over mail order when ratings are within similarity delta.
  - Uses supplier ID tie-break for equal scores.
  - Returns useful infeasible errors.
- Priority scheduling:
  - Defaults omitted priority to `standard`.
  - Normalizes blank priority to `standard`.
  - Processes waiting `rush` requests before waiting `standard` requests.
  - Preserves FIFO ordering within the same priority.
  - Returns a clear capacity error when the in-process queue is full.
- Integration:
  - `POST /api/route` against in-memory test host returns the documented shape.
  - At least one smoke test uses an order from `test_data/sample_orders.json`.

## README Updates

README should include:

- Prerequisites: .NET 8 SDK and Docker.
- Restore/build/test commands from repo root.
- Local run command.
- Environment variables and defaults.
- Sample `curl` for `GET /health`.
- Sample `curl` for `POST /api/route`.
- Docker build command.
- Docker run command.
- Docker verification command.

## AGENTS Updates

`AGENTS.md` should describe:

- The service purpose.
- Project layout.
- How to build and test.
- How data files are loaded.
- The HTTP 200 business-response convention.
- The priority scheduling convention.
- The routing priority order.
- Docker verification steps.

The file should be tool-agnostic so any implementation agent can continue the work.

## Resolved Design Decisions

- Malformed JSON returns HTTP 400.
- Unrated suppliers rank after all rated suppliers.
- Supplier quality comparison uses quantity-weighted average rating.
- Identical duplicate product rows are tolerated; conflicting duplicates fail startup.
- `rush` and `standard` are the only supported priority levels; omitted or blank priorities are treated as `standard`, and unknown priorities fail validation with the available values.
