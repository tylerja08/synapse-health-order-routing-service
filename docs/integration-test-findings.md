# Integration Test Findings

## Purpose

Capture integration-test coverage added for the order routing API contract and document findings from the test run.

## Command

```powershell
dotnet test OrderRouting.sln --no-build
```

## Current Result

- Total tests: 24
- Passing tests: 24
- Failing tests: 0

## Integration Coverage Added

The API smoke test now starts the service locally with an ephemeral port and verifies:

- `GET /health` returns success.
- `GET /swagger` returns the interactive API page.
- `GET /openapi.json` returns an OpenAPI 3.0.3 document.
- `POST /api/route` succeeds for a valid sample order.
- Malformed JSON returns HTTP 400.
- Business validation failures return HTTP 200 with `feasible:false`.
- Unknown product codes return HTTP 200 with `feasible:false`.
- Unknown priority values return HTTP 200 with `feasible:false` and list `rush` and `standard`.
- `mail_order:false` uses local fulfillment where required.
- `mail_order:true` can select mail-order fulfillment when the routing score supports it.
- Oversized request bodies return HTTP 413.
- Missing startup product data causes the API process to fail startup with a non-zero exit.
- Missing startup supplier data causes the API process to fail startup with a non-zero exit.
- Malformed startup supplier data causes the API process to fail startup with a non-zero exit.
- `GET /openapi.json` includes `/health`, `/api/route`, required request fields, priority enum values, item required fields, and success/infeasible examples.

## Unit Coverage Added

The suite now also includes focused tests for:

- Identical duplicate product rows being tolerated.
- Conflicting duplicate product rows failing.
- Quoted CSV fields with commas and escaped quotes.
- CRLF/LF CSV parsing and empty trailing fields.
- Unterminated quoted CSV input failing.
- Malformed product/supplier CSV loader errors.
- Quantity-weighted rating behavior.
- Deterministic route response ordering.

## Findings

- The integration runner needed `--no-launch-profile` because local `launchSettings.json` can otherwise override the test port.
- The API integration test now uses an ephemeral port to avoid collisions with local development processes.
- The scheduler priority test was clarified to match the intended non-preemptive behavior: a request already being routed is not interrupted, while waiting `rush` requests are processed before waiting `standard` requests.
- The full supplier data set has multiple high-quality mail-order wheelchair suppliers, so mail-order integration assertions should verify fulfillment behavior instead of pinning to one sentinel supplier ID.
- The startup-failure test confirms missing product data fails fast before serving traffic.
- The startup-failure coverage now also confirms missing supplier data and malformed supplier ratings fail fast before serving traffic.
- The request body limit is now asserted as part of the API contract with HTTP 413 for oversized JSON payloads.
- The suite now uses xUnit with the .NET test SDK and Visual Studio test adapter, so tests are discoverable from `dotnet test`, VS Code, and Visual Studio Test Explorer.

## Suggested Future Additions

- Add deeper OpenAPI response-schema assertions if generated clients will consume the API document.
- Add a startup-failure test for malformed product data if startup diagnostics become a formal deliverable.
- Add configuration matrix tests for alternate data paths if deployments will override the default `service_data` location.
