# AI Prompt Log

This file records the significant user prompts and decisions that shaped the order routing service implementation.

## Session Prompts

1. Create an implementation design for an order routing service using `prompts/prompt.md`. The service should be containerized and written in C#.

2. Update the design so the `priority` field is not ignored. Higher priority orders should be routed before lower priority orders.

3. Resolve open design questions:
   - Malformed JSON should return HTTP 400.
   - Unrated suppliers should rank after all rated suppliers.
   - Supplier rating comparison should use quantity-weighted average, with documented pros and cons.
   - Identical duplicate product rows should be tolerated and documented.

4. Resolve priority support:
   - Initially, only `rush` and `standard` should be supported, and unknown priority values should be treated as `standard`.
   - Later revised: unknown priority values should be validation errors that inform users of the available values.

5. Implement the order routing service defined by `prompts/prompt.md` and `design.md`.

6. Move runtime data files:
   - Move `docs/products.csv` to `service_data/products.csv`.
   - Move `docs/suppliers.csv` to `service_data/suppliers.csv`.
   - Move `docs/sample_orders.json` to `test_data/sample_orders.json`.
   - Update all documentation and references to the new locations.

7. Add an appropriate `.gitignore` file so unnecessary build and test files are not committed.

8. Add API documentation and an interactive Swagger-style page for API execution.

9. Build a larger performance test set using `test_data/sample_orders.json` as an example. Stress test the order routing API for hundreds of orders, document findings, and make improvement suggestions.

10. Create and run an exhaustive audit test to validate the majority of `service_data/products.csv` and `service_data/suppliers.csv`. Document results, findings, and improvement suggestions.

11. Enhance the audit test with:
    - Optional per-category counts for visibility into thin supplier coverage areas.
    - A mode that samples local routing for every product category across major ZIP regions.

12. Explain why `CsvProductRepository.cs` changed.

13. Evaluate whether excessive growth of `suppliers.csv` and `products.csv` could cause in-memory storage issues, and resolve concerns where appropriate.