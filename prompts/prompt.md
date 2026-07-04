# Implementation Prompt

## 1. Role

- Act as a senior implementation agent for this repository.

## 2. Goal

- Design a production-minded order routing service.
- Deliver the design in a design.md file.
- After user review and consent build the order routing service based upon the provided design.
- Deliver a service written in C# that can be run locally and in Docker without any external dependencies.
- Consider security concerns and provide an overview of any concerns to be reviewed.
- Prefer simple, readable implementation choices over broad architectural abstractions.
- Keep the API behavior aligned with `prompts/requirements.md`.
- Prefer asking for clarifications and follow up questions verses implementing without any input from the user.

## 3. Repository Context To Read First

- Read `prompts/requirements.md` for service behavior and response shape.
- Read `prompts/testdata.md` for data format details and edge cases.
- Inspect `docs/products.csv`, `docs/suppliers.csv`, and `docs/sample_orders.json` before implementing parsing or routing logic.

## 4. Functional Requirements

- Implement `POST /api/route`.
- Accept orders with:
  - `order_id`
  - `customer_zip`
  - `mail_order`
  - `items`
- Load product data from `docs/products.csv`.
- Load supplier data from `docs/suppliers.csv`.
- Return HTTP 200 for both feasible and infeasible routing results.
- Use `feasible: true` with a `routing` array when the order can be routed.
- Use `feasible: false` with an `errors` array when validation or routing fails.
- Include supplier details and routed item details in successful responses.

## 5. Data Parsing Instructions

- Parse CSV files with a real CSV parser, not manual string splitting.
- Treat supplier data as messy input:
  - `service_zips` can contain explicit ZIPs and ZIP ranges.
  - `product_categories` can contain multiple comma-separated categories.
  - `customer_satisfaction_score` can be numeric or `no ratings yet`.
  - `can_mail_order?` is represented as `y` or `n`.
- Normalize category, ZIP, rating, and mail-order values at application startup.
- Fail clearly if required data files are missing or malformed.

## 6. Validation Instructions

- Validate request shape before routing.
- Reject orders with missing or invalid `customer_zip`.
- Reject orders with no line items.
- Reject line items with missing `product_code`, unknown products, or invalid quantities.
- Return all meaningful validation errors together when practical.
- Do not use non-200 status codes for business infeasibility unless the requirements are changed.

## 7. Routing Logic Instructions

- Enforce feasibility first:
  - A supplier must support the product category.
  - If `mail_order` is false, a supplier must serve the customer ZIP locally.
  - If `mail_order` is true, suppliers with `can_mail_order` may be considered regardless of ZIP.
- Optimize in this priority order:
  - Feasibility.
  - Fewer shipments.
  - Higher customer satisfaction score.
  - Local fulfillment over mail order when ratings are similar.
- Prefer one supplier for multiple items when feasible.
- Split shipments only when consolidation is not feasible or is clearly worse according to the priority rules.
- Make tie-breaking deterministic, such as by supplier score and then supplier ID.
- Explain infeasible routing with useful errors.

## 8. API And Runtime Instructions

- Use a lightweight web framework appropriate for the existing project.
- Expose the service on a configurable port, defaulting to `8080`.
- Add a basic health endpoint if useful for Docker verification, such as `GET /health`.
- Keep startup deterministic and fail fast on configuration or data-loading problems.
- Log startup and request failures with enough detail to debug locally.
- Do not require external services unless explicitly necessary.

## 9. Docker Instructions

- Add a `Dockerfile` that builds and runs the service.
- Use a slim, stable base image for the chosen runtime.
- Copy only required source, dependency, and data files into the image.
- Install dependencies reproducibly using the project lockfile when present.
- Run the app as a non-root user when the runtime makes that practical.
- Expose the service port.
- Set a clear container start command.
- Add a `.dockerignore` to keep build context small.
- Include data files needed at runtime inside the image.

## 10. Local Development Instructions

- Add or update README instructions for:
  - Installing dependencies.
  - Running the service locally.
  - Running tests.
  - Building the Docker image.
  - Running the Docker container.
  - Sending a sample `POST /api/route` request.
- Prefer commands that work from the repository root.
- Keep environment variables documented with defaults.

## 11. Testing Instructions

- Add focused automated tests for:
  - Request validation.
  - Product lookup.
  - ZIP list and ZIP range parsing.
  - Mail-order eligibility.
  - Local supplier eligibility.
  - Consolidation into fewer shipments.
  - Rating-based supplier preference.
  - Infeasible orders.
- Include at least one integration-style test for `POST /api/route`.
- Use `docs/sample_orders.json` as smoke-test input where appropriate.
- Keep tests deterministic and independent of execution order.

## 12. Verification Instructions

- After implementation, run the test suite.
- Start the service locally and verify `POST /api/route` with at least one sample order.
- Build the Docker image.
- Run the Docker container and verify the API from outside the container.
- Report any commands that could not be run and why.

## 13. Completion Criteria

- The service implements the required routing behavior.
- The API returns the documented response shapes.
- Unit tests and integration tests pass locally.
- The Docker image builds successfully.
- The container starts and serves requests.
- README instructions are sufficient for another engineer to reproduce the run and verification steps.
- AGENTS instructions are sufficient for another agent to be able to understand and work in this repository, this should be Codex agnostic.