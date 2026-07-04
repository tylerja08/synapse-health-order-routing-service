# AI Prompt Log

This file records the significant user prompts and decisions that shaped the order routing service implementation.

## Session Prompts

1. 
> Create an implementation design for an order routing service using [prompt.md](prompts/prompt.md) .   This is a containerized service that should be written in c#.

2. 
> Following up on the design, the priority field should not be ignored.  Higher priority orders should be routed before before lesser priority orders.

3. 
> Answering the open design questions:
>
> Should malformed JSON return HTTP 400, or should the service attempt to wrap it in feasible: false? 
> - Malformed JSON should fail with a 400.
>  
> Should unrated suppliers rank as 0.0, neutral 5.0, or last after all rated suppliers? The design proposes 0.0 so known quality wins.
>   - Known quality should win, unknown rating should be treated as last. 
>
> Should rating comparison use weighted average by quantity, simple average by supplier, or minimum supplier rating?
>  - Lets stick with the weighted average by quantity, but add documentation detailing this design decision and the pros/cons of it.
>
> Should exact duplicate product codes with identical data be tolerated? 
>  - Yes, identical duplicates should be tolerated.  Document this detail and the design decision.

4. 
> Are the only supported priority values rush and standard, or should the implementation support additional levels such as urgent, expedited, or low?
>
> - We are going to only support rush and standard, an unknown priority will be treated as standard.

5. 
> Actually after thinking lets have a non rush or standard priority be a validation error, informing the user of the available values.

6. 
> Lets now implement this order routing service defined in [prompt.md](prompts/prompt.md) and the design [design.md](design.md) 

7. 
> Lets move [products.csv](docs/products.csv) and [suppliers.csv](docs/suppliers.csv) to a new service data directory instead of being in the docs directory.
>
> Move [sample_orders.json](docs/sample_orders.json)  to a test_data directory.
>
> Update all documentation and references to these files to match their new locations.

8. 
> Lets add an appropriate .gitignore file so we do not commit unnecessary build/test files.

9. 
> Lets add a api doc/swagger page for interactive API execution

10. 
> Using the [sample_orders.json](test_data/sample_orders.json) as an example, lets build a larger performance test set.
>
> The goal of this is to stress test our order routing api to verify it will be able to handle large loads of hundreds of orders.
>
> After building this test set, lets perform the stress test and document our findings and make suggestions for improvements if any are needed.

11. 
> Lets create and run an exhaustive test to validate the majority of the data in [products.csv](service_data/products.csv) and [suppliers.csv](service_data/suppliers.csv).  After the test run has completed, document the results, any findings, and suggestions for improvements that may have surfaced from the run.

12. 
> Lets enhance the audit test with the following:
>
> Add optional per-category counts to the audit if business users want visibility into thin supplier coverage areas, not just pass/fail coverage.
> Add an audit mode that samples local routing for every product category across major ZIP regions if geographic coverage quality becomes more important than broad mail-order feasibility.

13. 
> What was the reason for change in the CsvProductRepository.cs?

14. 
> If our [suppliers.csv](service_data/suppliers.csv) and [products.csv](service_data/products.csv) grow excessively do we have any concerns of in memory storage issues? If so lets resolve those.

15. 
> Should we add [launchSettings.json](src/OrderRouting.Api/Properties/launchSettings.json) to the git ignore?

16. 
> add the broader one

17. 
> Analyze our test coverage and make suggestions for additional integration tests or unit tests.

18. 
> Implement these suggested tests, after implementation run them and document the findings for integration tests.

19. 
> Implement the suggested additions [integration-test-findings.md](docs/integration-test-findings.md) 

20. 
> Add an integration test for missing supplier data specifically, mirroring the missing product data startup failure. 
>
>  Add an integration test for malformed supplier data startup failure

21. 
> Instead of having a custom CSV parser, lets use a well known/highly rated csv parser

22. 
> Refactor tests to use a standard .net unit test framework so they can be ran in vscode or visual studio

23. 
> Separate the unit tests and integration tests, so we can have clean unit test runs as part of a CI pipeline and integration tests part of the CD pipeline if this was to be productionized for a real rollout.

24. 
> Update the exception handling so it does not surface to the user, and instead is logged.  This has surfaced while running the integration tests, if an exception occurs the user has to click ok on a popup.

25. 
> Extract historical stress/data-audit runners into a dedicated diagnostics or operations project

26. 
> Perform security review and make any enhancements and document findings
>  -  Pay attention to possible injection attacks that could occur within the CSV input
>  -  Make initial adjustments for productionizing this, otherwise document

27. 
> Update readme documentation to have Docker as the primary method to run the service and local cli running as a less prominent option.

28. 
> Perform a code review based on the initial [prompts](prompts/) . 

29. 
> Lets fix these findings
