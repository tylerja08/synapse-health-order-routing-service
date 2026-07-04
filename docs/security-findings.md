# Security Review Findings

## Purpose

Review the order routing service for practical production hardening, with special attention to CSV-input injection risks.

## Enhancements Made

- Added CSV formula-injection protection for required CSV fields. Values that trim to a leading `=`, `+`, `-`, or `@` now fail startup with `DataLoadException`.
- Added unit coverage for formula-style payloads in product codes, supplier names, and product categories.
- Added HTTP response hardening headers:
  - `X-Content-Type-Options: nosniff`
  - `X-Frame-Options: DENY`
  - `Referrer-Policy: no-referrer`
- Added integration coverage for the security headers.
- Preserved generic request exception handling so unexpected exceptions are logged server-side and callers receive a generic HTTP 500 problem response.
- Fixed the Dockerfile restore stage to reference the current unit, integration, and diagnostics projects instead of the removed combined test project.

## CSV Injection Review

The service ingests product and supplier CSV files at startup, then returns supplier names, product codes, product categories, and fulfillment details in JSON responses. Even though the API does not export spreadsheets directly, CSV fields can still become dangerous if downstream users copy API output into Excel, Google Sheets, or BI tooling. Formula prefixes such as `=`, `+`, `-`, and `@` can trigger spreadsheet formula execution in those environments.

Current mitigation rejects formula-prefixed required CSV values during startup. This favors fail-fast data hygiene over attempting to escape dangerous data at every downstream output point.

Tradeoffs:

- Pro: Prevents compromised product/supplier data from entering the routing index.
- Pro: Keeps API responses and logs from propagating spreadsheet-active values.
- Con: Legitimate values that intentionally start with these characters are not accepted without a future explicit allowlist or escaping policy.

## Remaining Production Recommendations

- Add authentication and authorization before exposing route execution outside a trusted internal network.
- Gate Swagger/OpenAPI UI by environment or authentication. Keeping `/openapi.json` public may be acceptable internally, but the interactive UI should not be broadly exposed by default in production.
- Add rate limiting and request throttling at the edge. The in-process queue protects routing capacity, but it is not a full abuse-control layer.
- Terminate TLS at a trusted proxy or configure HTTPS directly for non-container local deployments.
- Add centralized structured logging, correlation IDs, and log-retention controls. Avoid logging full request bodies because order payloads may become sensitive in a real healthcare workflow.
- Pin container base images by digest and run image/dependency vulnerability scanning in CI.
- Add CSV field length limits and stricter allowlists for identifiers/categories once production data contracts are finalized.
- Treat configured CSV file paths as privileged deployment configuration. Do not allow untrusted users to control `Data__ProductsPath` or `Data__SuppliersPath`.
- Add CORS policy explicitly if browser clients are introduced. The current API does not configure CORS.
- Consider adding health checks that distinguish liveness from readiness if the service gains external dependencies.

## Verification

```powershell
dotnet build OrderRouting.sln
dotnet test tests\OrderRouting.UnitTests\OrderRouting.UnitTests.csproj --no-build
dotnet test tests\OrderRouting.IntegrationTests\OrderRouting.IntegrationTests.csproj --no-build
```
