# Performance Findings

## Test Data

- File: `test_data/performance_orders.json`
- Size: 500 orders
- Shape: deterministic expansion of `test_data/sample_orders.json` patterns with varied ZIPs, item mixes, quantities, and priorities.
- Priority mix: every fifth order is `rush`; the rest are `standard`.

## Method

Historical command before the test project split:

```powershell
dotnet run --project tests\OrderRouting.Tests\OrderRouting.Tests.csproj -- --stress --orders test_data\performance_orders.json --concurrency 25
```

After the unit/integration split, reintroduce this as a dedicated load-test tool or pipeline job before treating it as an active rollout gate.

The runner starts the API locally, waits for `/health`, sends the orders to `POST /api/route` with the requested concurrency, and reports HTTP status counts, feasibility counts, throughput, and latency percentiles.

## Results

### Baseline Slice

Command:

```powershell
dotnet run --project tests\OrderRouting.Tests\OrderRouting.Tests.csproj --no-build -- --stress --orders test_data\performance_orders.json --concurrency 25
```

Result:

| Metric | Value |
| --- | ---: |
| Total orders | 500 |
| Completed requests | 500 |
| HTTP 200 responses | 500 |
| Feasible routes | 500 |
| Infeasible responses | 0 |
| Failed requests | 0 |
| Total elapsed | 5.15 seconds |
| Throughput | 97.13 requests/sec |
| p50 latency | 234.85 ms |
| p95 latency | 444.40 ms |
| p99 latency | 487.97 ms |
| max latency | 499.16 ms |

### Higher Concurrency

Command:

```powershell
dotnet run --project tests\OrderRouting.Tests\OrderRouting.Tests.csproj --no-build -- --stress --orders test_data\performance_orders.json --concurrency 50
```

Result:

| Metric | Value |
| --- | ---: |
| Total orders | 500 |
| Completed requests | 500 |
| HTTP 200 responses | 500 |
| Feasible routes | 500 |
| Infeasible responses | 0 |
| Failed requests | 0 |
| Total elapsed | 4.45 seconds |
| Throughput | 112.28 requests/sec |
| p50 latency | 380.77 ms |
| p95 latency | 780.58 ms |
| p99 latency | 817.59 ms |
| max latency | 879.20 ms |

### Queue Overload

Command:

```powershell
dotnet run --project tests\OrderRouting.Tests\OrderRouting.Tests.csproj --no-build -- --stress --orders test_data\performance_orders.json --concurrency 125
```

Result:

| Metric | Value |
| --- | ---: |
| Total orders | 500 |
| Completed requests | 500 |
| HTTP 200 responses | 500 |
| Feasible routes | 106 |
| Infeasible responses | 394 |
| Failed requests | 0 |
| Total elapsed | 1.39 seconds |
| Throughput | 359.52 requests/sec |
| p50 latency | 1.26 ms |
| p95 latency | 970.56 ms |
| p99 latency | 1298.22 ms |
| max latency | 1314.49 ms |

The overload run is faster because many requests are rejected immediately by the configured in-process queue capacity. These are HTTP 200 business failures with the documented capacity error, not transport failures.

## Findings

- The API successfully handled hundreds of orders under the baseline and higher-concurrency runs with no HTTP failures and no business infeasibility.
- The baseline run completed 500 feasible route requests in about 5.15 seconds at roughly 97 requests/sec.
- Increasing client concurrency from 25 to 50 improved throughput to roughly 112 requests/sec, but p95 latency rose from about 444 ms to about 781 ms because requests wait longer in the in-process priority scheduler.
- The overload run at concurrency 125 exercised `Routing__MaxQueuedRequests=100`; 394 requests were rejected with `feasible:false` capacity responses. That confirms the service protects itself instead of allowing unbounded queue growth.
- During the first attempted 500-order run, the stress runner timed out because it redirected API logs without draining stdout/stderr. The runner was fixed to drain child-process logs before collecting final metrics. This was a test harness issue, not an API routing issue.
- Current routing performance is acceptable for "hundreds of orders" when concurrency remains below the configured queue capacity.

## Suggestions

- Keep `Routing__MaxQueuedRequests` documented and tune it for the target environment. The current default of 100 prevents runaway memory/latency, but can reject bursts above that threshold.
- Add production telemetry for queue wait time, routing duration, feasible/infeasible counts, and capacity rejections. The logs currently help locally, but structured metrics would make load behavior much easier to monitor.
- Consider a multi-worker priority scheduler if the service must process sustained traffic above roughly 100 requests/sec on similar hardware. The current scheduler intentionally routes one order at a time, which keeps priority ordering simple but limits CPU parallelism.
- Consider pre-indexing suppliers by category at startup. Each route currently scans all suppliers for each item. With 1,100 suppliers this is fine for the tested load, but category indexes would reduce per-request work and help future growth.
- If strict priority behavior across multiple workers is needed, replace the in-process scheduler with a bounded channel or durable priority queue plus worker pool semantics that preserve the desired ordering guarantees.
