# Price Crawler Module Design


> Status: implemented (2026-10-13, commit 0b08b2c). This page is the design input; the implementation record is in §11.
> Related docs: `docs/design/overview.md`, `docs/design/data-model.md`.

## 1. Goals and non-goals

### Goals

- Monitor prices of CS2 / Dota2 items on third-party sites, building an external price series that accumulates over time.
- Produce trend indicators (day-over-day, week-over-week, range change) for front-end display and back-office analysis.
- Compare against the in-site market (vapor → `MarketPriceSnapshot` / `MarketPriceDaily`) and produce a spread report between crawled prices and in-site prices.
- Module independence: `Doggo.PriceCrawler` is a standalone classlib, independently packageable (NuGet), wired into doggo via a project reference; site adapters, scheduling, cleaning, persistence, rate control and compliance all live inside the module, polluting no existing project.

### Non-goals

- No scraping of the Steam official community market. Steam-side market data is pushed by vapor and lands in `MarketPriceSnapshot`; this module only scrapes "other sites".
- No automated price comparison or repricing, no write-back or overwriting of `MarketPriceSnapshot` / `MarketPriceDaily`; crawled data is read-only for comparison.
- No trading, ordering, listing or inventory syncing.
- No rewrite of `Doggo.Scraper`, no changes to the news pipeline; only its skeleton is reused (see 2.3).
- No general-purpose crawler framework; site adapters are implemented per site.
- No replacement of the existing trend pipeline (`MarketPriceDaily` OHLC); trend indicators are computed from the external crawled series only.

## 2. Module boundary and shape

### 2.1 Shape

| Item | Decision |
| --- | --- |
| Project | new `server/src/Doggo.PriceCrawler/`, .NET classlib (`Microsoft.NET.Sdk`) |
| Module contents | adapter interfaces and implementations, scheduler, robots checks, cleaning pipeline, conversion and aggregation queries, admin endpoint extension methods, `PriceCrawlerOptions` |
| Dependency direction | `Doggo.PriceCrawler` → `Doggo.Domain` (+ AngleSharp for HTML parsing); no direct dependency on `Doggo.Infrastructure` |
| Persistence | repository interfaces defined in the module (mapping/series/queries), implemented and registered by `Doggo.Infrastructure` (same layer as `MarketStore`) |
| Entity location | `Doggo.Domain/Price/` (a sibling of `Market`), configuration and migrations in `Doggo.Infrastructure` |

Rationale: a clean packaging boundary — if it becomes an independent project later it carries no EF/MySql dependency; inside doggo it is one extra line of registration.

### 2.2 Host wiring recommendation

Recommendation: merge into the `Doggo.Scraper` process for phase 1.

| Option | Approach | Pros | Cost |
| --- | --- | --- | --- |
| A merge into Scraper | `Doggo.Scraper` references `Doggo.PriceCrawler`; `Program.cs` registers one more HostedService + config section + HttpClient | reuses the Hosting skeleton/config/DB injection/logging; zero new deployment units; `Dockerfile.worker` is already parameterised by PROJECT, untouched | shares CPU/bandwidth/memory in the same process |
| B standalone Worker | new `Doggo.PriceCrawler.Worker`, a third PROJECT in `Dockerfile.worker` | process isolation, independent scaling, faults do not propagate | one more deployment, config and monitoring surface; few sites in phase 1, poor value |

A recommended: few sites and low fetch frequency in phase 1 — the ops cost of a separate process outweighs the isolation benefit; news and prices are mutually independent (each with its own BackgroundService, scope, HttpClient and rate limiting), so co-hosting the risk is manageable.

Split-off path: `Doggo.PriceCrawler` depends on no `Doggo.Scraper` type, with independent interfaces and entities; to split into a standalone Worker later, only a new entry project (copying the `Program.cs` pattern) + one more PROJECT in `Dockerfile.worker` is needed — the module code does not move.

### 2.3 Reuse list (Doggo.Scraper)

| Reused item | Source | How reused |
| --- | --- | --- |
| Worker skeleton | `ScraperWorker.cs` | new `PriceCrawlerWorker` in the same pattern (BackgroundService + per-round scope) |
| Config binding | `ScraperOptions` + `Program.cs` | new `PriceCrawlerOptions` in the same pattern, `Bind("PriceCrawler")` |
| Fetch abstraction | `IPageFetcher` / `HttpPageFetcher` | injected and reused directly; timeout/retry logic is already in there |
| Playwright fetching | `PlaywrightPageFetcher` | pending a decision (see 10.4); if enabled, registered to the crawler |
| Adapter registry | `SourceAdapterRegistry` | new `SiteAdapterRegistry` in the same pattern (`IEnumerable` injection + dictionary lookup) |
| Circuit-breaker / failure-count approach | `ScraperScheduler` | same approach reimplemented in the new scheduler; failure counts stored in its own table |
| Fingerprint dedup approach | `NewsFingerprint` | pattern reference; price dedup is new (different window semantics) |
| EF / audit conventions | `DoggoDbContext` | new entities follow Guid v7 + `CreatedAtUtc`/`UpdatedAtUtc` + varchar(255) |
| Dockerfile.worker | PROJECT parameter | unchanged in phase 1; add a PROJECT when splitting off |

Not reused directly: `HtmlSanitizer` (news body cleaning, not needed for prices); the AngleSharp usage pattern of `HtmlSourceAdapter` (CSS selector + ConfigJson) is a reference, but price parsing is per-site custom.

### 2.4 New-build list (Doggo.PriceCrawler)

| Component | Responsibility |
| --- | --- |
| `ISiteAdapter` + site adapters | one implementation per site: discover items, fetch prices, unique key, currency, rate-limit hints |
| `PriceCrawlerWorker` / Scheduler | an independent loop per site, staggered starts, a global concurrency gate, failure backoff, circuit breaker |
| `RobotsChecker` + cache | robots.txt fetch/parse/cache/Disallow check/Crawl-delay |
| `PriceFingerprint` | page fingerprint and dedup key (URL normalisation + window key, following the `NewsFingerprint` pattern) |
| Cleaning pipeline | parse validation, currency normalisation, window dedup, outlier flagging, conversion and persistence |
| `ExchangeRateReader` | reads `ExchangeRate` only, converts to CNY (see 4.6) |
| `PriceCrawlerQueries` | series queries, spread report, trend computation (computed at query time) |
| `PriceCrawlerAdminEndpoints` | admin query/review endpoints (endpoint extension methods) |
| `PriceCrawlerOptions` + config section | see chapter 9 |

### 2.5 Boundary against existing pipelines

| Pipeline | Boundary |
| --- | --- |
| vapor → `MarketPriceSnapshot` | untouched; the sole source of Steam official market data |
| `MarketPriceDaily` | read-only, as the in-site baseline for spread comparison |
| `Doggo.Scraper` news | mutually independent; same process but separate HostedService/scope/HttpClient each |
| `Doggo.Api` | only adds admin query registration; no existing endpoint changes |

## 3. Site adapter model

### 3.1 Interface sketch

```csharp
namespace Doggo.PriceCrawler;

public interface ISiteAdapter
{
    string SiteKey { get; }                       // 站点标识，如 "example-market"
    IReadOnlyList<string> SupportedCurrencyCodes { get; } // 站点价格币种（ISO 4217）
    SiteRateLimit SuggestedRateLimit { get; }     // 适配器建议的间隔/并发/时段

    // 发现：按查询条件找出站点上的外部条目（站内已有 MarketItem 驱动）
    Task<IReadOnlyList<ExternalItemRef>> DiscoverAsync(DiscoveryQuery query, CancellationToken ct);

    // 抓取：按外部标识批量取当前报价
    Task<IReadOnlyList<ExternalPriceFetch>> FetchPricesAsync(IReadOnlyList<string> externalIds, CancellationToken ct);
}

public sealed record SiteRateLimit(int MinIntervalSeconds, int MaxConcurrent, string? AllowedWindowUtc);

public sealed record DiscoveryQuery(string? MarketHashName, string? ClassId, string? Keyword);

public sealed record ExternalItemRef(string ExternalId, string Title, string DetailUrl);

public sealed record ExternalPriceFetch(string ExternalId, long PriceCents, string CurrencyCode, DateTimeOffset ObservedAtUtc);
```

Conventions:

- `ExternalId` is the site-side item's unique key inside the adapter (URL slug / item id etc.), stable within a site; not comparable across sites.
- `PriceCents` is the minor unit of the site's currency; the currency is declared by `CurrencyCode` and the adapter must not convert implicitly.
- `ObservedAtUtc` is the UTC time the adapter observed the price (the time labelled on the page if present, otherwise the fetch time).
- Fetch failures / missing items: the adapter returns an empty set or skips the item without throwing (network exceptions excepted); the scheduler records the HTTP status per result.

### 3.2 Adapter registration and discovery

- Registration: `builder.Services.AddSingleton<ISiteAdapter, XxxSiteAdapter>()` (one per site); the scheduler injects `IEnumerable<ISiteAdapter>` and builds the `SiteAdapterRegistry` (mirroring `SourceAdapterRegistry`).
- The site list comes from config `PriceCrawler:Sites` (Key/Enabled/IntervalMinutes/MaxItemsPerRun etc.); sites not configured or not enabled are not scheduled even if the adapter is registered.
- Inside adapter implementations: HTML sites parse with AngleSharp (following the `HtmlSourceAdapter` CSS selector + ConfigJson pattern); JSON API sites deserialize directly; render-heavy sites go through `IPageFetcher` (Playwright, pending a decision).
- The adapter version (SemVer) is persisted in the `AdapterVersion` field, for tracing parsing-logic changes.

### 3.3 Discovery and fetch flow

```
发现（低频，默认每日一次，可配置）：
  站内 MarketItem（可按 game/白名单筛选）→ 调 DiscoverAsync → 新 ExternalId 写 PriceCrawlItemMap（Unconfirmed）

抓取（按站点间隔，默认 3600s）：
  取该站点 Unconfirmed + Confirmed 映射（限 MaxItemsPerRun）→ FetchPricesAsync → 原始记录 → 清洗管道 → 序列
```

In English:

```
Discovery (low frequency, default once per day, configurable):
  in-site MarketItem (optionally filtered by game/whitelist) → call DiscoverAsync → new ExternalIds written to PriceCrawlItemMap (Unconfirmed)

Fetch (per-site interval, default 3600s):
  take the site's Unconfirmed + Confirmed mappings (capped at MaxItemsPerRun) → FetchPricesAsync → raw records → cleaning pipeline → series
```

## 4. Data model

### 4.1 Entity overview

| Entity | Purpose |
| --- | --- |
| `PriceCrawlItemMap` | external item ↔ in-site MarketItemId mapping, with manual confirmation status |
| `PriceCrawlRaw` | raw fetch record (raw price, currency, HTTP status, page fingerprint) |
| `PriceCrawlSeries` | cleaned price series (raw price + CNY conversion, window dedup, outlier flags) |
| `PriceCrawlTrend` (optional) | daily trend indicator materialisation table (off by default, see 4.5) |

Common conventions (following `DoggoDbContext`): primary keys are Guid v7 generated on the application side; `CreatedAtUtc`/`UpdatedAtUtc` are auto-overwritten by `SaveChanges`; strings default to varchar(255); times are UTC. Entities live in `Doggo.Domain/Price/`, configuration and migrations in `Doggo.Infrastructure` (`ApplyConfigurationsFromAssembly` applies `IEntityTypeConfiguration` automatically).

### 4.2 PriceCrawlItemMap (external item mapping)

| Field | Type | Meaning | Index / constraint |
| --- | --- | --- | --- |
| Id | Guid | PK | PK |
| SiteKey | string(32) | site key | UQ(SiteKey, ExternalId) |
| ExternalId | string(255) | site-side item unique key | UQ(SiteKey, ExternalId) |
| ExternalTitle | string(255) | site-side item name (redundant snapshot) | |
| ExternalUrl | string(2048) | detail page | |
| MarketItemId | Guid? | in-site item; null when unconfirmed | IX(MarketItemId) |
| MapStatus | int | 0 Unconfirmed / 1 Confirmed / 2 Rejected | IX(MapStatus) |
| MatchSource | int | 0 Auto (discovery) / 1 Manual (human binding) | |
| CreatedAtUtc / UpdatedAtUtc | DateTime | automatic audit | |

Note: the unique constraint (SiteKey, ExternalId) prevents duplicate mappings of the same item on the same site; MarketItemId is nullable and backfilled on confirmation.

### 4.3 PriceCrawlRaw (raw fetch record)

| Field | Type | Meaning | Index / constraint |
| --- | --- | --- | --- |
| Id | Guid | PK | PK |
| SiteKey | string(32) | site key | IX(SiteKey, ExternalId, ObservedAtUtc) |
| ExternalId | string(255) | item key | same |
| PriceCents | long? | parsed raw price (site currency, cents); null on parse failure | |
| CurrencyCode | string(8) | ISO currency | |
| RawPriceText | string(64) | raw price text from the page (with symbols, for traceability) | |
| PageFingerprint | string(64) | normalised page SHA-256 (`PriceFingerprint`) | |
| HttpStatusCode | int | status code of the latest request | |
| ObservedAtUtc | DateTime | observation time | same |
| FetchedAtUtc | DateTime | fetch completion time (for dedup) | |
| AdapterVersion | string(32) | adapter version | |

Note: raw records are append-only, for tracing and reconciliation; retention is pending decision 10.8.

### 4.4 PriceCrawlSeries (cleaned price series)

| Field | Type | Meaning | Index / constraint |
| --- | --- | --- | --- |
| Id | Guid | PK | PK |
| MapId | Guid | linked mapping | IX(MapId) |
| MarketItemId | Guid | in-site item (only Confirmed mappings land in the series) | IX(MarketItemId, ObservedAtUtc) |
| SiteKey | string(32) | site key | UQ(SiteKey, ExternalId, WindowKey) |
| ExternalId | string(255) | item key | same |
| PriceCents | long | cleaned raw price (site currency, cents) | |
| CurrencyCode | string(8) | ISO currency | |
| ConvertedCents | long? | converted CNY price; null when the rate is missing | |
| ExchangeRate | decimal? | the rate used (original value retained) | |
| ObservedAtUtc | DateTime | observation time (window-aligned) | |
| WindowKey | string(32) | dedup window key (yyyyMMddHHmm, aligned by DedupWindowMinutes) | UQ(SiteKey, ExternalId, WindowKey) |
| IsSuspect | bool | outlier flag (deviation from the rolling median) | |
| CreatedAtUtc | DateTime | automatic audit | |

Dedup rule: UQ(SiteKey, ExternalId, WindowKey) guarantees at most one row per item per window; "take the last value" is guaranteed by the cleaning pipeline (see chapter 6).

### 4.5 PriceCrawlTrend (trend indicators, optional materialisation)

| Field | Type | Meaning | Index / constraint |
| --- | --- | --- | --- |
| Id | Guid | PK | PK |
| MarketItemId | Guid | in-site item | UQ(MarketItemId, SiteKey, Date) |
| SiteKey | string(32) | site | same |
| Date | DateOnly | UTC day | same |
| CloseCents | long | the day's last cleaned price (CNY-converted; original currency when no rate) | |
| DayOverDayPct | decimal? | day-over-day | |
| WeekOverWeekPct | decimal? | week-over-week | |
| PeriodChangePct | decimal? | range change (last 7/30 days, decided by a query parameter) | |
| ComputedAtUtc | DateTime | computation time | |

Materialise vs compute-at-query-time: the default is **compute at query time** (small data volume, definitions adjustable at any time, no extra writes). When the series grows (sites × items × daily frequency) or admin queries become hot, enable this table as a daily cache refreshed in batch after each daily close. Whether to materialise is pending decision 10.3.

### 4.6 Relationship with ExchangeRates

- `ExchangeRate` (CurrencyCode, Rate, Source) is an existing entity, meaning "multi-currency rates (for display)". This module is **read-only** on it — no writes, no refreshes (a refresh pipeline, if any, is a separate design).
- Conversion direction convention: 1 unit of CurrencyCode = Rate CNY (to be confirmed in pending decision 10.6; if the existing data runs the other way, flip multiply to divide).
- ConvertedCents = round(PriceCents × Rate); if the rate lacks the currency → ConvertedCents = null, ExchangeRate = null, the series keeps the original currency value, nothing blocks.
- Rate snapshot retention: the `ExchangeRate` field records the original value used, for historical traceability (the current rate may change).

### 4.7 Registration and migrations

- Entity registration: `DoggoDbContext` adds four lines like `DbSet<PriceCrawlItemMap> PriceCrawlItemMaps` (consistent with other domains).
- Configuration: `IEntityTypeConfiguration` files go in `Doggo.Infrastructure/Persistence/` (applied automatically by `ApplyConfigurationsFromAssembly`); unique indexes and indexes are declared in configuration.
- Migrations: appended in the Migrations directory of `Doggo.Infrastructure` (following the existing migration process).

## 5. Scheduling and rate control

### 5.1 Scheduling model

| Item | Design |
| --- | --- |
| Model | one independent async loop per site (a per-site queue), serial within a site, sites throttled by a global concurrency gate |
| Site interval | an independent IntervalMinutes per site (default 60), a config item |
| Global concurrency | a SemaphoreSlim shared across sites, default 8 (config item) |
| Staggering | first wake-up delay = site index × spread seconds + random jitter, avoiding on-the-hour concurrency |
| Discovery cadence | once per day per site (default around 04:00 UTC, configurable), decoupled from the fetch loop |
| Per-site circuit breaker | consecutive failures ≥ threshold → skipped during the cooldown (see 5.2) |
| Graceful exit | CancellationToken throughout; each round has its own scope, exceptions do not spread to other sites |

### 5.2 Failure backoff (exponential + jitter)

| Parameter | Default | Notes |
| --- | --- | --- |
| MaxAttempts | 3 | max attempts per task |
| BaseDelaySeconds | 5 | backoff base |
| Factor | 2 | exponential factor: delay after the nth failure = Base × 2^n |
| CapSeconds | 1800 | max backoff 30 minutes |
| JitterRatio | 0.25 | jitter ratio: final delay = min(Cap, Base × 2^n) × (1 + rand × 0.25) |
| CircuitFailureThreshold | 10 | consecutive-failure breaker threshold |
| CircuitCooldownMinutes | 60 | breaker cooldown |
| Reset on success | — | any success → consecutive-failure counter resets to zero |

Note: backoff applies at site level (the site sleeps) and does not affect other sites; backoff stacks with the interval in 5.1 (the larger wins).

### 5.3 robots.txt checks

Flow:

1. First visit to a host: GET /robots.txt, parse Disallow / Crawl-delay / Sitemap.
2. Cache: IMemoryCache (or Redis) keyed by host, TTL default 24h (config item `Robots:CacheHours`).
3. Before fetching, check each URL: a Disallow path-prefix hit → skip and count (no request made).
4. A declared Crawl-delay greater than the site's configured interval → the larger wins.
5. robots.txt fetch failure: fail-open by default (treated as unrestricted) + logging; configurable to fail-close.
6. A per-site toggle (`PriceCrawler:Sites:RobotsEnabled`), on by default; before enabling a site, compliance must be confirmed manually (pending decision 10.9).

### 5.4 UA declaration and fetch windows

- UA: `DoggoPriceBot/1.0 (+https://<site-domain>/price-crawler; contact: <admin email>)`, configurable; never disguised as a browser.
- Time window: unrestricted by default; an optional NightWindowUtc (e.g. 02:00–06:00) pauses fetching, avoiding the target site's peak. Within the window the site's loop sleeps.

## 6. Cleaning and dedup rules

| Step | Rule |
| --- | --- |
| Parse validation | PriceCents must be > 0 and ≤ the cap (default 10,000,000,000 cents, a config item); otherwise treated as a parse failure — not entering the series, counted in the raw record's HTTP status/errors |
| Currency normalisation | the site-declared currency → ISO 4217 uppercase three letters; unknown/missing → dropped and counted by default (pending decision 10.6 may change this to keep the original value) |
| Window dedup | for the same (SiteKey, ExternalId), align the WindowKey by DedupWindowMinutes (default 60); within a window keep the last row (max FetchedAtUtc) |
| Outliers | deviation beyond ±SuspectThresholdPct (default 50%) from the rolling median (the past N=20 cleaned points) → IsSuspect=true, kept not dropped; excluded from spread reports by default, includable in queries |
| Conversion | ConvertedCents = round(PriceCents × Rate) (when a rate exists); missing → null |
| Cross-site | no merging: different sites are different rows; comparisons happen per site dimension |
| Mapping gate | only mappings with MapStatus=Confirmed enter the series (with MarketItemId); Unconfirmed only lands in raw records and the mapping, as history reference for manual confirmation |

## 7. Aggregation and analysis rules

### 7.1 Comparison dimensions and baseline

| Dimension | Rule |
| --- | --- |
| Item | the same MarketItemId (mapping Confirmed) |
| Currency | unified CNY: crawled prices use ConvertedCents; the in-site baseline uses `MarketPriceDaily.CloseCents` (existing convention, platform currency CNY) |
| Time | UTC day alignment: each day takes the ConvertedCents of the last non-suspect series point as the day's crawled price, aligned with that day's Daily |
| Missing | a day with no crawled point → no comparison that day; a missing Daily (vapor does not cover the item) → flagged "no in-site baseline", no spread computed |

### 7.2 Spread indicators

| Indicator | Definition |
| --- | --- |
| Absolute spread SpreadCents | crawled price − in-site price (cents, CNY) |
| Percentage spread SpreadPct | (crawled price − in-site price) ÷ in-site price × 100, null when the in-site price is 0 |
| Trend indicators (of the external series itself) | day-over-day = (today's close − yesterday's close) ÷ yesterday's close × 100; week-over-week = (today's close − the close 7 days ago) ÷ that close × 100; range change = (range end − range start) ÷ range start × 100 (default last 7 days, parameterisable) |

### 7.3 Query sketches

1. Spread report (EF, joined with `MarketPriceDaily` by UTC day):

```csharp
var rows = await db.PriceCrawlSeries
    .Where(s => !s.IsSuspect && s.ObservedAtUtc >= from && s.ObservedAtUtc < to)
    .GroupBy(s => new { s.MarketItemId, Day = DateOnly.FromDateTime(s.ObservedAtUtc) })
    .Select(g => new { g.Key.MarketItemId, g.Key.Day,
                      CrawlCents = g.OrderByDescending(s => s.ObservedAtUtc).First().ConvertedCents })
    .ToListAsync(ct);

var report = rows.Select(r => new
{
    r.MarketItemId,
    r.Day,
    StationCents = db.MarketPriceDailies
        .Where(d => d.MarketItemId == r.MarketItemId && d.Date == r.Day)
        .Select(d => (long?)d.CloseCents).FirstOrDefault(),
    r.CrawlCents
}).Select(x => new
{
    x.MarketItemId, x.Day, x.StationCents, x.CrawlCents,
    SpreadCents = x.CrawlCents - x.StationCents,
    SpreadPct = x.StationCents == 0 ? (decimal?)null
        : (x.CrawlCents - x.StationCents) * 100m / x.StationCents
}).ToList();
```

(The real implementation goes through the module's `PriceCrawlerQueries`, avoiding EF assembly in the Api layer; this is a rule sketch.)

2. An item's external price series (grouped by site):

```csharp
var points = await db.PriceCrawlSeries
    .Where(s => s.MarketItemId == marketItemId && s.ObservedAtUtc >= from && s.ObservedAtUtc < to)
    .OrderBy(s => s.ObservedAtUtc)
    .Select(s => new { s.SiteKey, s.ObservedAtUtc, s.PriceCents, s.CurrencyCode, s.ConvertedCents, s.IsSuspect })
    .ToListAsync(ct);
```

## 8. Endpoint surface (admin sketch)

Route group `/api/admin/price-crawl` (Admin policy inherited). Implemented (maps list and review, prices series, spread-report), authorised by the Admin policy.

```
GET /api/admin/price-crawl/maps?siteKey=&mapStatus=&page=&pageSize=
→ { total, items: [ { mapId, siteKey, externalId, externalTitle, externalUrl,
                     marketItemId, marketHashName, mapStatus, matchSource, updatedAtUtc } ] }

GET /api/admin/price-crawl/prices/{marketItemId}?from=&to=&includeSuspect=
→ { marketItemId, points: [ { siteKey, observedAtUtc, priceCents, currencyCode,
                              convertedCents, isSuspect } ] }

GET /api/admin/price-crawl/spread-report?from=&to=&siteKey=
→ { rows: [ { marketItemId, marketHashName, date, crawlCents, stationCents,
             spreadCents, spreadPct } ] }

POST /api/admin/price-crawl/maps/{mapId}/confirm   { } → 200 { mapId, mapStatus: "Confirmed" }
POST /api/admin/price-crawl/maps/{mapId}/reject    { } → 200 { mapId, mapStatus: "Rejected" }
```

Note: the endpoint extension method `MapPriceCrawlerAdminEndpoints(this IEndpointRouteBuilder)` lives in `Doggo.PriceCrawler`; `Doggo.Api` registers it in one line inside the AdminEndpoints group (or as its own group). Authorisation follows the Admin policy.

## 9. Configuration items

```jsonc
{
  "PriceCrawler": {
    "Enabled": true,                    // 模块总开关
    "ScanIntervalSeconds": 300,         // 调度器重新检查间隔
    "GlobalConcurrentFetches": 8,        // 全局并发上限（跨站点 SemaphoreSlim）
    "FetchTimeoutSeconds": 60,          // 单次抓取超时
    "MaxItemsPerRun": 200,              // 每站点每次抓取条目数上限
    "Retry": {
      "MaxAttempts": 3, "BaseDelaySeconds": 5, "Factor": 2,
      "CapSeconds": 1800, "JitterRatio": 0.25
    },
    "CircuitBreaker": { "FailureThreshold": 10, "CooldownMinutes": 60 },
    "Robots": { "Enabled": true, "CacheHours": 24, "FailOpen": true },
    "UserAgent": "DoggoPriceBot/1.0 (+https://<host>/price-crawler)",
    "NightWindowUtc": { "Enabled": false, "Start": "02:00", "End": "06:00" },
    "Discover": { "IntervalHours": 24, "RunAtUtc": "04:00" },
    "DedupWindowMinutes": 60,
    "SuspectThresholdPct": 50,
    "RollingMedianPoints": 20,
    "MaxPriceCents": 10000000000,
    "Sites": [
      { "Key": "example-market", "Enabled": true, "IntervalMinutes": 60, "RobotsEnabled": true }
    ],
    "SeedDefaultSites": true
  }
}
```

## 10. Pending decisions

| # | Item | Default recommendation | Decider |
| --- | --- | --- | --- |
| 1 | Target site list (first batch of sites, URLs, API/HTML shape) | fix the first batch of adapters once the user provides them | user |
| 2 | Whether mappings need manual review before entering the spread report | Confirmed only; Unconfirmed merely accumulates data | user |
| 3 | Trend indicators materialised vs computed at query time | query time; materialise when scale grows | user |
| 4 | Whether Playwright render-heavy fetching is allowed | disallowed by default (HttpClient only); revisit if a site needs it | user |
| 5 | Host shape: merged into Scraper vs standalone Worker | merged into the Doggo.Scraper process in phase 1 | user |
| 6 | ExchangeRate direction convention and handling of missing rates / unknown currencies | 1 foreign = Rate CNY; no conversion when missing, keep the original value; drop and count unknown currencies | user |
| 7 | Fetch frequency and night window | default 60min per site; night window off by default | user |
| 8 | Data retention policy (raw record retention days, series retention, cleanup task) | raw 90 days, series forever; add a cleanup task later | user |
| 9 | Compliance authorisation for target sites (whether robots/ToS allow price scraping) | enable a site only after per-site manual confirmation | user |
| 10 | First-batch monitoring scope (all MarketItems vs whitelist) | all items by game + configurable whitelist | user |

## 11. Implementation status (2026-10-13, commit 0b08b2c)

Pending decisions 2-10 landed per the default recommendations: only Confirmed mappings enter the series and comparisons; trend is computed at query time, not materialised; Playwright not enabled (HttpClient + AngleSharp only); the host is merged into the Scraper process; the rate direction is 1 foreign = Rate CNY, missing rates keep the original value without conversion, unknown currencies are dropped and counted; sites default to a 60-minute interval and the night window is implemented; raw rows are cleaned in batch after 90 days; the robots gate is on by default; monitoring is all items by game + a configurable whitelist. 1 (the target site list) still awaits the user; the site adapters are config-driven — a config is an onboarding.

Entities landed as the four tables ItemMap / Raw / Series / SiteState (the Trend materialisation table from the design sketch was not created; trend is computed at query time); spread comparison goes through `RecentCompareCentsAsync` with a rolling 20 points for suspect detection. The module has 81 + Api 5 tests, written first and green.

## 12. Front-end exposure rule (2026-10-16 代决, user may override)

The front end exposes the aggregate rule only, never per-site rows:

| Item | Rule |
| --- | --- |
| Value | for each confirmed site, the latest non-suspect, CNY-converted crawled price within the last 7 days; the median across sites |
| Exclusions | observations with `IsSuspect=true` or `ConvertedCents` null (missing rate) |
| In-site baseline | the latest close of `MarketPriceDaily` (§7.1) |
| Spread | (external median − in-site close) ÷ in-site close × 100; null when the in-site close is 0 or there is no external data |
| Placement | the web market trend page; no extra column on list pages, mobile / miniapp do not add it in phase 1 |
| Not covered | when no site is onboarded or the item has no series, the fields are null and the page does not render the comparison block |

Rationale: suspect rows and unconverted raw values cannot go out directly; the aggregate median is a single directly quotable number; the per-site quote table stays on the admin side; the user only needs one baseline to judge "is the in-site price reasonable". The implementation does not write back to `MarketPriceSnapshot` / `MarketPriceDaily` (the §1 read-only comparison constraint) and adds no table, no materialisation (§10-3 default: compute at query time).

Implemented as `GET /api/market/items/{id}/reference` (MarketEndpoints): median and spread, suspect and unconverted exclusions, nulls when no series, 404 for unknown items — 4 test cases.
