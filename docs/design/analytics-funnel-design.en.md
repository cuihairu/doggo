# Funnel Data Analytics Module Design

[English](/design/analytics-funnel-design.en) | [简体中文](/design/analytics-funnel-design)

Initiated 2026-10-12, design before implementation; this page is the sole design input. Scope: a four-step funnel from page view → add-to-cart (intent) → order → payment, including event instrumentation, the upload endpoint, server-side authoritative events, daily aggregation and the admin funnel page.

## 1. Background and current state

doggo's existing statistics only answer "how much was sold", not "where users drop off":

| Existing facility | Location | Answers | Cannot answer |
| --- | --- | --- | --- |
| UserVisitLog | `server/src/Doggo.Api/VisitLogMiddleware.cs` | whole-site server-side PV/UV (one row per request, written in finally, failures only logged) | client-side behaviour (clicks, intent), no client/channel dimension |
| `/api/admin/stats` family | `server/src/Doggo.Api/Endpoints/AdminEndpoints.cs` | overview counters, 14-day trends, product ranking | per-step conversion and drop-off |
| UserOrder / PaymentRecord | `server/src/Doggo.Domain/Orders/` | orders and payment results (authoritative) | drop-off before ordering |

The four clients: web (React + Vite), miniapp (Taro), mobile (Taro RN), admin (React back office). miniapp/mobile share `@doggo/shared` (`shared/src/client.ts`, a Taro request wrapper + kvGet/kvSet local storage), web has its own `web/src/api/client.ts` (a fetch wrapper).

## 2. Goals and non-goals

### Goals

1. Define and land a four-step conversion funnel, with UV, conversion rate and drop-off rate visible per step.
2. Support per-client, per-channel (channel) and per-date grouping.
3. A unified client-side event upload channel, with offline catch-up on weak networks and server-side idempotency dedup.
4. The order and payment steps use server-side authoritative events, not client reports.
5. A new funnel page in the back office, consistent with the existing stats route area and parameter style.
6. Clarify the same-source conventions with UserVisitLog, NewsItem.ViewCount and MarketPriceSnapshot.

### Non-goals

- No real-time dashboards, no second- or minute-level realtime statistics (T-1 daily aggregation only, see the §10 pending decisions).
- No third-party analytics SDK (the self-built event channel is what lets VisitorKey-style conventions align with existing data).
- No A/B experiment platform, no (Pachel-style user segmentation) capability.
- No event replay or self-service detail-level querying (details are for troubleshooting only, no back-office query UI).
- The admin client is outside the purchase funnel statistics scope (the Client enum keeps the admin value; nothing is reported currently).

## 3. Funnel definition and doggo's reality mapping

### 3.1 "Add to cart" mapping

doggo has no cart. The real purchase path is: product detail page → choose plan (CasePlan) / quantity tier (QuantityOption) / quantity → call `POST /api/orders` directly → invoke payment. There is no "add to cart then checkout" intermediate state.

Recommendation: map "add to cart" to **order intent**: on the product detail page, the user has selected the planId / optionId / quantity triple and clicked "buy now" (before entering order confirmation). This action means the user has finished the product decision, and every subsequent abandonment (failed payment invocation, price shock, network failure) is captured by the 2→3 step's drop-off rate.

### 3.2 The four-step funnel

| Step | Event name | Instrumentation source | Trigger point | Key properties |
| --- | --- | --- | --- | --- |
| 1 view product | `page_view_product` | client | product detail page render complete | productId, source position (source) |
| 2 add to cart (intent) | `checkout_intent` | client | clicked "buy now" after choosing plan/quantity on the detail page | productId, planId, optionId, quantity |
| 3 order | `order_created` | **server-side authoritative** | `OrderService.CreateOrderAsync` saved the order successfully | orderId, productId, totalPriceCents, entry |
| 4 payment | `order_paid` | **server-side authoritative** | `ApplyPaymentCallbackAsync` first successful callback | orderId, payType, amountCents |

### 3.3 Why step 4 must use a server-side event

- `POST /api/orders/{id}/pay` only **creates a payment order** (`OrderService.CreatePaymentAsync` asks the channel for a payment link); clicking the pay button is not payment success.
- The sole authoritative source of the payment result is the channel callback `POST /api/payments/callback` → `ApplyPaymentCallbackAsync`: status PendingPayment → PendingSettlement, `PaidAtUtc` written. The callback can arrive after the user has closed the app, so the client cannot report it at all.
- `PaymentRecord.IdempotencyKey` already dedups callbacks, and `ApplyPaymentCallbackAsync` advances status only in the "first success" branch, so an event at that branch is naturally neither missing nor duplicated.

Only the client knows the first two steps (the server has no "viewed the product page" fact), and from step 3 the server order data already exists — the event table is just the order table reshaped into a unified event form, so one aggregation logic handles everything.

## 4. Event instrumentation model

### 4.1 Entity sketch

```csharp
// server/src/Doggo.Domain/Analytics/AnalyticsEntities.cs
public enum AnalyticsClient { Web = 0, Miniapp = 1, Mobile = 2, Admin = 3 }

public class AnalyticsEvent : Entity
{
    public string EventName { get; set; } = string.Empty;      // 蛇形命名,见 4.4 事件字典
    public DateTime OccurredAtUtc { get; set; }                // 客户端事件时间(补报时可能明显早于现在)
    public DateTime ReceivedAtUtc { get; set; }                // 服务端入库时间
    public Guid SessionId { get; set; }                        // 客户端生成;服务端事件取请求上下文
    public Guid? UserId { get; set; }                          // 登录态,匿名事件为 null
    public string VisitorKey { get; set; } = string.Empty;     // 服务端生成,算法沿用 VisitLogMiddleware
    public AnalyticsClient Client { get; set; }
    public string Channel { get; set; } = "organic";           // 渠道,取值来源见 §10
    public string PropertiesJson { get; set; } = "{}";         // 白名单属性,见 §10,禁止 PII
    public string IdempotencyKey { get; set; } = string.Empty; // 客户端生成,重试不变
    public int SchemaVersion { get; set; } = 1;
}

public class FunnelDailyStat : Entity
{
    public DateOnly Date { get; set; }            // UTC 日,按漏斗第 1 步发生日归行,见 §7.3
    public AnalyticsClient Client { get; set; }
    public string Channel { get; set; } = string.Empty;
    public long ViewProductUv { get; set; }       // 第 1 步到达 UV
    public long CheckoutIntentUv { get; set; }
    public long OrderCreatedUv { get; set; }
    public long OrderPaidUv { get; set; }
}
```

Notes:

- Both extend `Entity` (Id + CreatedAtUtc/UpdatedAtUtc), following the `DoggoDbContext.ApplyAuditAndIds` convention: application-side GUID v7, `ValueGeneratedNever`, audit times auto-overwritten. AnalyticsEvent's `UpdatedAtUtc` always equals `CreatedAtUtc` — redundant but keeps the base-class convention; no special handling.
- Neither implements `ISoftDeletable`: log rows are not soft-deleted.
- Registration: `DoggoDbContext` adds `DbSet<AnalyticsEvent>` and `DbSet<FunnelDailyStat>`; index configuration goes in `ModelConfigurations.cs`.

### 4.2 Indexes and capacity

| Entity | Index | Purpose |
| --- | --- | --- |
| AnalyticsEvent | `IdempotencyKey` unique | idempotency dedup (offline catch-up, client resends) |
| AnalyticsEvent | `(EventName, OccurredAtUtc)` | the aggregation job scans by event name + date |
| AnalyticsEvent | `(SessionId, OccurredAtUtc)` | intra-session event ordering, drill-down |
| AnalyticsEvent | `(VisitorKey, OccurredAtUtc)` | anonymous-identity funnel |
| FunnelDailyStat | `(Date, Client, Channel)` | idempotent upsert, rolling recomputation overwrites |

- `PropertiesJson` uses `HasColumnType("longtext")` explicitly (columns exceeding the default varchar(255) all go through configuration; precedent: `NewsItemConfiguration`).
- `DateOnly` columns follow the explicit DATE conversion precedent in `MarketPriceDailyConfiguration` (MySql.EntityFrameworkCore needs a conversion for DateOnly).
- Event-table volume estimate: tens of thousands of PV per day across the four clients, ~300 bytes per row, within hundreds of millions per year, a single MySQL table is usable. Partition trade-off: MySQL RANGE partitioning requires every unique key to contain the partition column, which forces a `(Id, OccurredAtUtc)` composite primary key and breaks the IdempotencyKey unique index — **not adopted**; instead "retain then archive after aggregation": once aggregation lands in `FunnelDailyStat`, expired details can be deleted (§10 #9 implemented: `AnalyticsRetentionWorker` runs daily at 02:00 UTC, `FunnelDailyStat` retained forever).

### 4.3 Session definition

| Item | Rule |
| --- | --- |
| sessionId generation | generated as a UUID on the client's first availability, stored in local storage |
| Client session switch | cold start, or returning to the foreground more than 30 minutes after the last activity → a new sessionId |
| Server | **never switches sessions**, only records the client-provided sessionId |
| Fallback | if aggregation finds events of the same VisitorKey without a sessionId (should never happen in theory, guarding dirty data), derive a virtual session by VisitorKey + 30-minute inactivity window |

Rationale: client-side session switching is simple to implement, and by the time offline catch-up arrives the session is already closed; if the server switched sessions, late events would require recomputing historical session boundaries; 30 minutes is the industry-standard inactivity window, sufficient for the "browse products → order" scenario. Sessions in the funnel serve only as a correlation aid (see §7.3 — the funnel counts by identity, not by session), so imperfect session splitting does not affect funnel conclusions.

### 4.4 Event dictionary

| Event name | Source | SchemaVersion | Properties whitelist |
| --- | --- | --- | --- |
| `page_view_product` | client | 1 | productId, source |
| `checkout_intent` | client | 1 | productId, planId, optionId, quantity |
| `order_created` | server | 1 | orderId, productId, entry, serviceMode, fulfillmentType, totalPriceCents, payType |
| `order_paid` | server | 1 | orderId, payType, amountCents |

Later new events (news reading, market subscription, etc.) only extend the dictionary, not the table structure; property changes bump SchemaVersion, and old-version events stay permanently parseable.

## 5. Upload endpoint and offline catch-up

### 5.1 Endpoint contract

`POST /api/events`, usable anonymously (the event route group does not add `RequireAuthorization`); the request may carry `Authorization: Bearer`, filling UserId if `sub` resolves, otherwise null. VisitorKey is generated by the server from connection info (same algorithm as VisitLogMiddleware), **the client never sends it**.

Request:

```json
POST /api/events
{
  "events": [
    {
      "eventName": "checkout_intent",
      "occurredAtUtc": "2026-10-13T08:30:05.120Z",
      "sessionId": "01993c1e-8a4b-7c30-9d2f-1e5c6b7a8f01",
      "channel": "organic",
      "schemaVersion": 1,
      "idempotencyKey": "01993c1e-8a4b-7c30-9d2f-1e5c6b7a8f01:000042",
      "path": "/products/0c9d2f",
      "properties": { "productId": "0c9d2f", "planId": "3a1b", "quantity": 2 }
    }
  ]
}
```

Response:

```json
200 OK
{ "accepted": 49, "duplicates": 1, "rejected": 0 }
```

| Item | Convention |
| --- | --- |
| Batch limits | ≤ 50 events per batch; a single event's serialized properties ≤ 2 KB; request body ≤ 256 KB |
| Validation | an event name not in the dictionary or an unknown SchemaVersion → that event is rejected and dropped; an illegal body structure → 400 ProblemDetails |
| Idempotency | the IdempotencyKey unique index; conflicts are not errors, counted as duplicates |
| Time | the server does not trust but retains occurredAtUtc; ReceivedAtUtc is written by the server |
| Rate limiting | fixed-window rate limiting per VisitorKey (recommend ≤ 30 batch requests per 60 seconds), 429 over the limit; V1 is a single-instance deployment so in-memory counters suffice; switch to Redis when multi-instance |
| Failure | upload failure is silent: the client-side event queue retries; server exceptions are 5xx and never block business |

C# signature (the route goes into `Doggo.Api/Endpoints`, a new `EventEndpoints` alongside OrderEndpoints):

```csharp
public static class EventEndpoints
{
    public static void MapEventEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/events");   // 匿名可用
        group.MapPost("/", async (
            [FromBody] EventBatchRequest request,
            [FromServices] AnalyticsRecorder recorder,
            HttpContext context,
            CancellationToken cancellationToken) => { /* ... */ });
    }
}

public sealed record EventBatchRequest(IReadOnlyList<EventIngestItem> Events);
public sealed record EventIngestItem(
    string EventName,
    DateTime OccurredAtUtc,
    Guid SessionId,
    string? Channel,
    int SchemaVersion,
    string IdempotencyKey,
    string? Path,
    Dictionary<string, object>? Properties);
public sealed record EventBatchResponse(int Accepted, int Duplicates, int Rejected);
```

### 5.2 Which layer hosts the client wrapper

Current state: miniapp/mobile share `@doggo/shared` (a Taro wrapper), web has its own wrapper, the four clients share no runtime code, only the REST contract (the established principle in `docs/design/overview.md`). The upload channel respects this structure:

| Client | Wrapper location | Notes |
| --- | --- | --- |
| miniapp / mobile | `shared/src/events.ts` (new file, re-exported by `index.ts`) | queue uses the existing `kvSet/kvGet`, requests go through the existing `api()` (`skipAuth: true` tolerates anonymity) |
| web | `web/src/api/events.ts` (new file) | an equivalent fetch implementation with a localStorage queue |
| admin | does not report | the Client=Admin enum value is kept |

What the two ends share is **contract and algorithms** (the event dictionary, field names, session-splitting parameters, queue format) — no implementation sharing.

```ts
// shared/src/events.ts sketch signature
export function trackEvent(name: string, properties?: Record<string, unknown>): void
export function flushEvents(): Promise<void>   // 失败静默回队,永不抛错
```

Call sites add only two points: `page_view_product` on the product detail page's mount completion; `checkout_intent` on the "buy now" click. Instrumentation failure must never affect the page.

### 5.3 Offline catch-up and out-of-order events

| Item | Rule |
| --- | --- |
| Client queue | events are written to the local queue first (kvSet/localStorage), uploaded in batches (≥ 10 events or ≥ 15 seconds since the last upload) |
| Resend | a failed upload (network/5xx/429) returns the whole batch to the queue, retried with exponential backoff on next launch or trigger; IdempotencyKey does not change once generated, server dedup is the backstop |
| Queue cap | at most 500 cached locally; beyond that the oldest are dropped (tolerable for statistics; memory must stay bounded) |
| Acceptable delay | the server accepts events whose OccurredAtUtc is within 7 days of now; older ones are rejected (the catch-up value is already low) |
| Out-of-order | no ordering within or across batches is required; everything is attributed to a day by OccurredAtUtc; late events landing in an already-aggregated date are corrected by §7.4's rolling recomputation |
| Reconciliation rule | funnel statistics are based on OccurredAtUtc; a separate daily received volume by ReceivedAtUtc is provided (see the §8 reconciliation column); the difference between the two is the late/catch-up volume |

## 6. Server-side authoritative instrumentation

### 6.1 Instrumentation points

| Event | Write point | Transaction strategy |
| --- | --- | --- |
| `order_created` | `OrderService.CreateOrderAsync`, after the existing `db.UserOrders.Add(order)` and before `SaveChangesAsync`, append the event row | **same SaveChanges batch as the order (same transaction)** |
| `order_paid` | `OrderService.ApplyPaymentCallbackAsync`, append the event row inside the SaveChangesAsync batch that advances the status PendingPayment → PendingSettlement | same as above |

Same transaction recommended, no outbox:

- The order write already hard-depends on this one MySQL instance; the event row is one more INSERT on the same connection, adding no availability dependency, with zero loss and the shortest implementation.
- An outbox needs an outbox table + polling delivery, and this project has no MQ — delivery would ultimately write to the same database, i.e. splitting one INSERT into two plus a poller, a net-negative trade.
- If the event volume ever grows enough to slow ordering (it will not now), fall back to "after a successful save, write independently in try/catch, log a warning on failure and tolerate a small loss" — a reversible implementation-phase decision.

Event writes go through one unified entry point, never scattered:

```csharp
// server/src/Doggo.Infrastructure/Analytics/AnalyticsRecorder.cs
public interface IAnalyticsRecorder
{
    void EnqueueOrderCreated(UserOrder order);                       // 调用于业务 SaveChanges 前,同一批次落库
    void EnqueueOrderPaid(UserOrder order, PaymentRecord payment);   // 同上
    Task RecordAsync(AnalyticsEvent e, CancellationToken ct = default); // 供 /api/events 用,独立保存
}
```

### 6.2 Context field sources

| Field | order_created | order_paid |
| --- | --- | --- |
| UserId | the order's UserId | the order's UserId |
| SessionId / Client / Channel | the current request context (IHttpContextAccessor); the client sends X-Doggo-Session on the order request header, empty if absent | the callback request comes from the payment provider, no client context: SessionId/Client/Channel are recorded empty, and at aggregation time they are backfilled by joining the same order's `order_created` event on orderId |
| VisitorKey | IHttpContextAccessor, following the middleware algorithm | empty |
| OccurredAtUtc | the order's CreatedAtUtc | the order's PaidAtUtc (more accurate than the current time, and also right under idempotent replay) |

## 7. Aggregation job

### 7.1 Responsibilities

Aggregate `AnalyticsEvents` into `FunnelDailyStat` daily (date × client × channel × per-step UV). The funnel page reads only the aggregation table, never the event table.

### 7.2 Job shape

```csharp
// server/src/Doggo.Worker/Analytics/FunnelAggregateWorker.cs
public sealed partial class FunnelAggregateWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<FunnelAggregateWorker> logger) : BackgroundService
// 轮询模式沿用既有 Worker(OrderTimeoutWorker 等):Task.Delay 循环 + 每日 00:30 UTC 触发
// Program.cs:builder.Services.AddHostedService<FunnelAggregateWorker>();
```

In English:

```csharp
// server/src/Doggo.Worker/Analytics/FunnelAggregateWorker.cs
public sealed partial class FunnelAggregateWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<FunnelAggregateWorker> logger) : BackgroundService
// the polling pattern follows the existing Workers (OrderTimeoutWorker etc.):
// a Task.Delay loop + a daily trigger at 00:30 UTC
// Program.cs:builder.Services.AddHostedService<FunnelAggregateWorker>();
```

### 7.3 Funnel rules

| Item | Definition |
| --- | --- |
| Identity | UserId first, otherwise VisitorKey; anonymous → logged-in unification, see §10.3 |
| Funnel instance | one identity's `page_view_product` opens an instance; later steps are consumed in event-time order (one event is not reused across instances) |
| Window | a later step counts toward an instance only if completed within 7 days of step 1 |
| Step UV | if an instance reaches step k, that instance counts toward step k |
| Day attribution | an instance belongs to **step 1's occurrence day** (UTC); the four step UVs in the row share the same source, so rates within a row are directly comparable |
| Conversion rate | step k+1 UV / step k UV; drop-off rate = 1 − conversion rate |

Rationale for day attribution: after attributing by step 1, "the funnel page's paid UV on a day" is not equivalent to "the order report's paid count for that day" (the former includes people who entered the funnel on previous days) — deliberate, because the funnel page wants "the same cohort's conversion", where cross-row division means something; the calendar-day paid count already exists in `/api/admin/stats/trend`, and the two coexist for different purposes. A "strictly in-session funnel" rule is not adopted: users leaving and returning (a session switch) to complete an order is the norm, so session-based counting systematically undercounts the last two steps' conversion.

### 7.4 Scheduling and recomputation

| Item | Rule |
| --- | --- |
| Daily run | at 00:30 UTC, run T-1 (yesterday) |
| Rolling recomputation | simultaneously recompute T-7 … T-2: late catch-up and the order_paid backfill (§6.2) are all absorbed within the 7-day window |
| Idempotency | output is upserted by the `(Date, Client, Channel)` unique key, recomputation overwrites old values, reruns are safe |
| Lateness cap | events with OccurredAtUtc earlier than T-7 no longer join recomputation (§5.3 already rejects them at the ingnet; this is a double safety net) |
| Failure | a failed day is logged; the next loop retries the whole window (recomputation is idempotent, extra runs are harmless) |

The aggregation implementation layer (in-memory scan + grouping vs SQL GROUP BY) is left to the implementation phase; this page fixes only the inputs, outputs and rules.

## 8. Admin funnel page

### 8.1 Endpoint

Merged into the AdminEndpoints stats route area (`admin.MapGroup("/api/admin")`, Admin policy), with parameter style aligned to the existing endpoints: `[FromQuery]` defaults + `Math.Clamp` bounds; dates use `yyyy-MM-dd` (consistent with the `date` output of trend).

```csharp
admin.MapGet("/funnel", async (
    DoggoDbContext db,
    [FromQuery] DateOnly? from,      // 默认 T-13
    [FromQuery] DateOnly? to,        // 默认 T-1
    [FromQuery] string? groupBy,     // none | client | channel,默认 none
    [FromQuery] string? grain,       // day | week,默认 day
    [FromQuery] string? client,      // 可选过滤
    [FromQuery] string? channel,     // 可选过滤
    CancellationToken cancellationToken) => { /* ... */ });
```

Response sketch (when `groupBy=client`):

```json
{
  "from": "2026-10-01",
  "to": "2026-10-07",
  "grain": "day",
  "groupBy": "client",
  "groups": [
    {
      "key": "web",
      "steps": [
        { "step": "page_view_product", "label": "浏览商品", "uv": 12000, "rate": 1.0,    "dropRate": 0.0 },
        { "step": "checkout_intent",   "label": "加购",     "uv": 3600,  "rate": 0.30,  "dropRate": 0.70 },
        { "step": "order_created",     "label": "下单",     "uv": 900,   "rate": 0.25,  "dropRate": 0.75 },
        { "step": "order_paid",        "label": "支付",     "uv": 540,   "rate": 0.60,  "dropRate": 0.40 }
      ]
    },
    { "key": "miniapp", "steps": [ /* ... */ ] }
  ],
  "rows": [
    {
      "date": "2026-10-01",
      "client": "web",
      "channel": "organic",
      "viewProductUv": 1800, "checkoutIntentUv": 520,
      "orderCreatedUv": 130, "orderPaidUv": 81
    }
  ],
  "receivedToday": 52310
}
```

- `groups`: range summaries, per-step UV and inter-step conversion/drop-off rates; the data source for the funnel bar chart and the step detail table.
- `rows`: per-day per-group detail at day granularity (week granularity aggregates by ISO week); the data source for the by-day grouped table.
- `receivedToday`: the day's event volume received by ReceivedAtUtc, the reconciliation column (§5.3), returned along the way.

### 8.2 Page layout

A new standalone funnel page in the back office (menu "data analysis → conversion funnel"), layout top to bottom:

| Block | Contents |
| --- | --- |
| Filter row | date range (default last 14 days, at most T-1), client dropdown (all/web/miniapp/mobile), channel dropdown, group dimension (none/client/channel), granularity (day/week) |
| Funnel bar chart | horizontal bars per step under the current grouping, bar length = UV, bar tail labelled with UV and inter-step conversion rate; four steps in fixed order, no charting library needed — rectangular divs suffice (same preference as the Dashboard's hand-written SVG line chart) |
| Step detail table | rows = steps, columns = step name, UV, conversion rate, drop-off rate |
| By-day grouped table | rows = date × group key, columns = four step UV, sortable by date |

Data comes only from `GET /api/admin/funnel`; the page never scans details directly. This chapter is the implementation basis for the admin prototype `admin/funnel.html`: the prototype is a standalone static HTML (following the existing prototype practice), first rendering this layout with fixed mock data, switching to the real data source once the endpoint is ready, then merging into `admin/src/pages/`.

## 9. Same-source conventions

### 9.1 Relationship with existing data

| Data source | Perspective | Time convention | Identity convention | Responsibility | Relationship with AnalyticsEvent |
| --- | --- | --- | --- | --- | --- |
| UserVisitLog | all server-side requests (incl. API) | `Date` (DateOnly, UTC day) | UserId? / VisitorKey | whole-site PV/UV overview | **not replaced**, see 9.2 |
| AnalyticsEvent | client behaviour stream + server-side key actions | OccurredAtUtc (UTC) | UserId? / VisitorKey / SessionId | funnel and conversion analysis | the subject |
| NewsItem.ViewCount | cumulative read count per item | no time dimension (accumulating value) | none | content popularity ranking | complementary: the counter answers "which article is hot", the event stream answers "who is reading and where they go after"; if a news funnel is initiated later, add a `page_view_news` event and leave ViewCount alone |
| MarketPriceSnapshot / MarketPriceDaily | market time series (unrelated to user behaviour) | CapturedAtUtc / Date (UTC day) | none | price trends | unrelated; only the "UTC day alignment" convention is shared |

### 9.2 The event table does not replace UserVisitLog

| Dimension | UserVisitLog | AnalyticsEvent |
| --- | --- | --- |
| Collection point | server-side middleware, one row per request | client-side active + two server-side authoritative points |
| Coverage | all paths (including uninstrumented pages and APIs) | events in the dictionary |
| Properties | only Path | has session, channel, business properties |
| Volume | one row per request, cannot be retained long-term in full | whitelisted events, controllable |
| Use | PV/UV overview, path popularity | funnel, conversion, drop-off |

The funnel page does not read UserVisitLog: its Path perspective cannot infer intents like "chose a plan and clicked buy", and has no client/channel; conversely, the whole-site PV overview does not need to move to the event table (instrumenting every page costs more and has a higher loss rate than the server middleware).

### 9.3 Unified aggregation-analysis endpoint convention

All later aggregation-analysis endpoints (including this funnel, and future news/market analysis endpoints) follow:

1. Time: always UTC days, parameters and output `yyyy-MM-dd` (DateOnly);
2. Client: the `AnalyticsClient` enum strings (web/miniapp/mobile/admin), selectable for all;
3. Identity: UserId first, otherwise VisitorKey, the same algorithm as the existing UserVisitLog VisitorKey;
4. Parameter style: `[FromQuery]` + defaults + `Math.Clamp` bounds (e.g. days bounded 1–90);
5. Response: anonymous objects, camelCase, consistent with the stats area.

UserVisitLog has gained a Client column (see the implementation status section below): VisitLogMiddleware and the event upload / server-side authoritative instrumentation share `AnalyticsClients.ParseClient` to parse the X-Doggo-Client header (defaulting to web), with existing rows backfilled as web; all four clients now send the header (web/admin are static values, miniapp and mobile call setClient at startup). Per-client PV conventions can now be aligned, and the funnel page's per-client data can also be derived from this table besides the event table.

## 10. Privacy and compliance

### 10.1 Properties collect no PII

- Properties are collected per the §4.4 whitelist: the server validates each item, keys not in the whitelist are dropped, and values too long reject that event (counted as rejected).
- Explicitly forbidden: phone numbers, emails, nicknames, plaintext UserAgent, IP, precise geolocation, Steam profiles. The client wrapper layers (`shared/src/events.ts` / `web/src/api/events.ts`) do the same whitelist pre-filter — two lines of defence.
- Path records only the page path; user input in the query (search terms etc.) is stripped.

### 10.2 VisitorKey reuses the anonymisation scheme

- The event table's VisitorKey is same-source as UserVisitLog: `SHA256(ip|userAgent)` taking the first 32 hex characters (the existing VisitLogMiddleware implementation). Recommend extracting that algorithm into a common static class for both callers — **without changing** the existing middleware behaviour; raw IP and UA never land in the event table.

### 10.3 Anonymous and logged-in identity unification

| Scenario | Unification rule |
| --- | --- |
| Not logged in | identity = VisitorKey |
| Logged in | identity = UserId (VisitorKey is still recorded, for continuity with the anonymous period) |
| Continuous behaviour across login | session-level backfill: if any event within the same SessionId carries a UserId, all anonymous events of that session merge into that UserId at aggregation time |
| Cross-session | no backfill. Two independent sessions of the same VisitorKey are not identity-merged, to avoid merging multiple people sharing one device into one |

The backfill window equals the session lifecycle (30 minutes active + offline residue), not a fixed duration: the window closes naturally with sessionId, making both implementation and explanation simplest. The login action itself does **not** produce a backfill (it never rewrites the UserId of already-stored events); unification happens only at aggregation read time, and the detail table keeps the raw facts.

## 11. Implementation split (suggested)

| Phase | Contents | Dependencies |
| --- | --- | --- |
| 1 | entities + DbContext registration + `/api/events` + AnalyticsRecorder + the two server-side authoritative points | none |
| 2 | the `shared/src/events.ts` and `web/src/api/events.ts` wrappers + two instrumentation points on the product detail page + the order request carrying X-Doggo-Session | phase 1 |
| 3 | the FunnelAggregateWorker daily aggregation + rolling recomputation | phase 1 |
| 4 | `GET /api/admin/funnel` + the admin funnel page (prototype `admin/funnel.html` first) | phase 3 |

## 12. Pending decisions

| # | Item | This page's recommendation | Open questions |
| --- | --- | --- | --- |
| 1 | "Add to cart" mapping | checkout_intent = clicking "buy now" after choosing plan/quantity on the detail page | whether the mapping and trigger point are accepted |
| 2 | Session splitting | client-generated sessionId, switch session after 30 minutes of inactivity; the server never switches, aggregation backfill only | whether 30 minutes is accepted; whether the server should switch sessions |
| 3 | Anonymous event authorisation | login-free, relying on rate limiting (30 batches per 60 seconds) + batch/size caps + the property whitelist | whether login-free is accepted; the rate-limit threshold |
| 4 | Funnel day attribution | attribute rows by step 1's occurrence day, intra-row rates same-source and self-consistent | whether "funnel page paid count ≠ the order report's paid count for the day" is accepted |
| 5 | Aggregation materialisation granularity | (date × client × channel) | whether a product dimension is needed (steps 2/3 carry productId, addable); whether weekly materialisation is needed (currently aggregated on the fly from daily rows) |
| 6 | Channel value sources | client startup parameters / landing-page URL parameters, default organic; the server never guesses | who defines the channel naming list and campaign link conventions |
| 7 | Same-day data visibility | T-1 aggregation only; the same day offers only the receivedToday reconciliation number | whether admin needs a manual "query today now" trigger (scanning the event table directly, slow but fresh) |
| 8 | UserVisitLog Client column | recommended, to align per-client PV conventions | whether to do it in this round or as a later independent change |
| 9 | Event detail retention | details retained 90 days after aggregation, deleted when expired | the retention period and whether to archive (and where) |
| 10 | Late-event window | accept 7 days, recompute T-7…T-1 | whether 7 days is suitable |

Implementation status (2026-10-13, commit 69da251 server-side + 1b47726 client/back office): 1-10 are all implemented per this page's recommendations; 8 and 9 came in later commits, other items as tabled above. 8 UserVisitLog's Client column: `VisitLogMiddleware` and the event upload / server-side authoritative instrumentation share `AnalyticsClients.ParseClient` to parse the X-Doggo-Client header (defaulting to web), with existing rows backfilled as web; all four clients now send the header. 9 Event detail retention cleanup: `AnalyticsRetentionWorker` runs daily at 02:00 UTC (after the 00:30 aggregation), deleting details where both OccurredAtUtc and ReceivedAtUtc are earlier than the cutoff; the cutoff comes from `Analytics:EventRetentionDays` (default 90 days); judging staleness by a single timestamp would wrongly delete late events, hence the two-condition intersection; `FunnelDailyStat` is outside retention. Two implementation-phase differences from the sketch: order_created's OccurredAtUtc uses UtcNow at enqueue time (the audit pipeline stamps CreatedAtUtc in SaveChanges, same instant); the funnel instance matches by identity (UserId, otherwise VisitorKey) rather than by session — the server-side order_paid has no session context, and §7.3 already counts by identity.
