# Data Audit Findings

## Purpose

Validate the majority of `service_data/products.csv` and `service_data/suppliers.csv` through the same parsing, normalization, eligibility, and routing code used by the service.

## Method

Command:

```powershell
dotnet run --project tools\OrderRouting.Diagnostics\OrderRouting.Diagnostics.csproj --no-build -- data-audit
```

The data audit now lives in the dedicated diagnostics project so it can be run as an operational validation or rollout gate without coupling it to unit or integration test discovery.

The audit performs these checks:

- Loads `service_data/products.csv` through `CsvProductRepository`.
- Loads `service_data/suppliers.csv` through `CsvSupplierRepository`.
- Confirms every normalized product category has supplier coverage.
- Confirms every normalized supplier category maps to at least one product.
- Routes every unique product code as a single-line-item mail-order-capable order.
- Validates every supplier can locally fulfill at least one known product category at one of its own service ZIPs.
- Reports per-category product counts, total supplier coverage, and mail-order supplier coverage.
- Samples local routing coverage for every product category in major ZIP regions.

## Results

| Metric | Value |
| --- | ---: |
| Product CSV rows | 1,200 |
| Unique products loaded | 1,195 |
| Duplicate product rows ignored | 5 |
| Product categories | 24 |
| Supplier CSV rows | 1,100 |
| Suppliers loaded | 1,100 |
| Supplier categories | 24 |
| Rated suppliers | 916 |
| Unrated suppliers | 184 |
| Mail-order suppliers | 459 |
| Product categories without suppliers | 0 |
| Supplier categories without products | 0 |
| Product route failures | 0 |
| Supplier validation failures | 0 |

## Category Coverage

| Category | Products | Suppliers | Mail-order suppliers |
| --- | ---: | ---: | ---: |
| ankle brace | 48 | 246 | 99 |
| back brace | 48 | 242 | 103 |
| blood pressure monitor | 48 | 263 | 105 |
| cane | 49 | 265 | 114 |
| cervical collar | 49 | 263 | 110 |
| commode | 53 | 272 | 116 |
| compression stockings | 51 | 246 | 105 |
| cpap | 56 | 237 | 94 |
| cpm machine | 45 | 242 | 108 |
| crutches | 52 | 240 | 98 |
| glucose meter | 48 | 277 | 120 |
| heating pad | 45 | 236 | 92 |
| hospital bed | 56 | 263 | 97 |
| ice machine | 45 | 235 | 109 |
| knee scooter | 46 | 245 | 96 |
| nebulizer | 48 | 272 | 107 |
| oxygen | 56 | 233 | 96 |
| patient lift | 51 | 246 | 92 |
| rollator | 49 | 240 | 95 |
| shower chair | 50 | 242 | 105 |
| tens unit | 45 | 239 | 101 |
| traction device | 44 | 274 | 117 |
| walker | 58 | 251 | 109 |
| wheelchair | 55 | 267 | 108 |

The thinnest supplier coverage categories are:

- `oxygen`: 233 suppliers
- `ice machine`: 235 suppliers
- `heating pad`: 236 suppliers
- `cpap`: 237 suppliers
- `tens unit`: 239 suppliers

Even the thinnest categories still have broad supplier representation in this data set.

## Regional Local Coverage

The audit sampled local, non-mail-order routing coverage for every product category in representative major ZIP regions.

| Region | ZIP | Covered categories | Uncovered categories |
| --- | --- | ---: | --- |
| New York | `10015` | 24 / 24 | None |
| Brooklyn | `11221` | 24 / 24 | None |
| Boston | `02130` | 24 / 24 | None |
| Chicago | `60610` | 24 / 24 | None |
| Houston | `77059` | 24 / 24 | None |
| Los Angeles | `90020` | 24 / 24 | None |
| Philadelphia | `19131` | 24 / 24 | None |

## Findings

- The data set is internally consistent for routing purposes.
- All 1,195 unique products can be routed successfully when mail order is allowed.
- All 1,100 suppliers have at least one product category that maps to known product data and can be locally matched at one of their own service ZIPs.
- The loader correctly tolerated 5 identical duplicate product rows.
- No product categories lack supplier coverage.
- No supplier categories refer to categories missing from product data.
- Every audited major ZIP region has local supplier coverage for all 24 product categories.
- Per-category supplier coverage is broad; the smallest category still has 233 suppliers.
- 184 suppliers have `no ratings yet`; this is expected and handled by ranking unrated suppliers after rated suppliers.

## Suggestions

- Keep this audit in CI or as a release gate whenever product or supplier CSV data changes.
- Consider turning the 5 identical duplicate product rows into a source-data cleanup task. They do not break routing, but removing them would reduce loader warnings and make data ownership cleaner.
- Add a machine-readable audit output file if future workflows need trend tracking across data drops.
- Extend regional coverage sampling if the business expands beyond the current representative ZIPs. The current audit checks seven major regions, not every ZIP in the supplier data.
- Consider adding thresholds for minimum supplier count per category and minimum mail-order supplier count per category. The audit now exposes the numbers, but it does not currently fail on thin coverage unless a category has no coverage.
