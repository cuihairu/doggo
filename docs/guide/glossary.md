# Glossary

This glossary defines concepts at the responsibility-layer level. File references point to the authoritative implementation or declaration, not merely to a similarly named directory.

| Term | English | Definition | Evidence |
| --- | --- | --- | --- |
| Contract and rules layer | Contract and rules layer | Framework-independent entities, state machines, probability, pricing, and analytics vocabulary consumed by every server process. | `server/src/Doggo.Domain/DomainMarker.cs:1` |
| Order state machine | Order state machine | The allowed transitions between created, paid, delivering, completed, canceled, and refunded order states. | `server/src/Doggo.Domain/Orders/OrderStateMachine.cs:8` |
| Roll-room state machine | Roll-room state machine | The lifecycle gate for pending, running, drawing, drawn, claiming, and finished roll rooms. | `server/src/Doggo.Domain/Cases/RollRoomStateMachine.cs:8` |
| Seat state machine | Seat state machine | The per-seat lifecycle and optimistic-version invariants used by group buying. | `server/src/Doggo.Domain/Cases/SeatStateMachine.cs:6` |
| Alias method | Alias method | Constant-time categorical sampling used to select a case prize. | `server/src/Doggo.Domain/Cases/AliasMethod.cs:9` |
| Price rules | Price rules | Effective price, quantity-tier resolution, plan-option precedence, and cashback calculation. | `server/src/Doggo.Domain/Products/PriceCalculator.cs:23` |
| Analytics event dictionary | Analytics event dictionary | Server-side whitelist of accepted funnel event names and property keys, plus its schema version. | `server/src/Doggo.Domain/Analytics/AnalyticsEventDictionary.cs:9` |
| Persistence and application services | Persistence and application-services layer | Scoped EF/transaction use cases, mappings, external auth/Steam clients, and cross-table business operations. | `server/src/Doggo.Infrastructure/DoggoInfrastructureExtensions.cs:11` |
| Application database context | Application database context | The EF Core unit of work exposing all MySQL domain sets and applying audit changes. | `server/src/Doggo.Infrastructure/Persistence/DoggoDbContext.cs:15` |
| Model configurations | Model configurations | EF table, key, index, conversion, and JSON-column mapping declarations for all persisted domains. | `server/src/Doggo.Infrastructure/Persistence/Configurations/ModelConfigurations.cs:1` |
| Order service | Order service | Transaction owner for creation, payment callbacks, cancellation, refund, retrieval, expiry, and reconciliation. | `server/src/Doggo.Infrastructure/Orders/OrderService.cs:55` |
| Group-buying service | Group-buying service | Transaction owner for case-plan seats, ownership checks, optimistic concurrency, opening, prizes, and Vapor rollback. | `server/src/Doggo.Infrastructure/Cases/GroupBuyingService.cs:33` |
| Roll service | Roll service | Transaction owner for roll rooms, joins, cryptographic draws, winner claims, and period rollover. | `server/src/Doggo.Infrastructure/Cases/RollService.cs:31` |
| Market store | Market store | Writer/reader for market items, snapshots, daily series, sync cursors, and Vapor listing application. | `server/src/Doggo.Infrastructure/Market/MarketStore.cs:19` |
| Vapor 客户端 | Vapor client | Typed outbound HTTP adapter for asynchronous Steam control-plane jobs and bearer authentication. | `server/src/Doggo.Infrastructure/Vapor/VaporClient.cs:40` |
| Vapor Webhook | Vapor webhook | Signed inbound notification contract with timestamp anti-replay verification. | `server/src/Doggo.Infrastructure/Vapor/VaporWebhook.cs:23` |
| Idempotency-claim store | Idempotency-claim store | Redis SET NX claim that suppresses webhook replay, with process-local fallback when Redis cannot serve claims. | `server/src/Doggo.Infrastructure/Vapor/WebhookIdempotencyStore.cs:11` |
| Analytics recorder | Analytics recorder | Shared recorder that joins business save batches or persists anonymous event rows after whitelist filtering. | `server/src/Doggo.Infrastructure/Analytics/AnalyticsRecorder.cs:17` |
| Service orchestration | Service orchestration layer | Independently deployable REST/auth/visit-logging host that translates external calls into application-service calls. | `server/src/Doggo.Api/Program.cs:25` |
| Endpoint map | Endpoint map | ASP.NET Core route-group module that owns a REST surface, request DTOs, authorization, and response shapes. | `server/src/Doggo.Api/Endpoints/OrderEndpoints.cs:33` |
| Admin endpoints | Admin endpoints | The admin-only back-office surface covering content, catalog, orders, users, roll rooms, price review, settings, and statistics. | `server/src/Doggo.Api/Endpoints/AdminEndpoints.cs:27` |
| Visit-log middleware | Visit-log middleware | Post-response middleware that writes one visit row and never fails the HTTP response. | `server/src/Doggo.Api/VisitLogMiddleware.cs:13` |
| Event rate limiter | Event rate limiter | Process-local fixed-window limiter for anonymous analytics batches. | `server/src/Doggo.Api/Endpoints/EventEndpoints.cs:195` |
| Vapor Webhook 端点 | Vapor webhook endpoint | The API's inbound `/api/vapor/webhook` protocol adapter and claim/release orchestration. | `server/src/Doggo.Api/Endpoints/VaporEndpoints.cs:10` |
| Scheduling and async work | Scheduling and async layer | Background-service host that independently performs market, order, roll, coin, funnel, and retention work. | `server/src/Doggo.Worker/Program.cs:18` |
| Market-sync worker | Market-sync worker | Scheduled job that creates/polls Vapor jobs and folds resulting listings into durable market state. | `server/src/Doggo.Worker/Market/MarketSyncWorker.cs:17` |
| Reconciliation worker | Reconciliation worker | Periodic compensation for stuck payments, refunds, and retrievals. | `server/src/Doggo.Worker/Orders/OrderReconciliationWorker.cs:13` |
| Funnel-aggregation worker | Funnel-aggregation worker | Daily T-1 recompute of funnel daily statistics before event detail retention. | `server/src/Doggo.Worker/Analytics/FunnelAggregateWorker.cs:12` |
| External ingestion | External ingestion layer | News and price acquisition processes/libraries that schedule untrusted sources, clean payloads, deduplicate, and persist results. | `server/src/Doggo.Scraper/Program.cs:11` |
| News source adapter | News source adapter | Normalizes RSS, Steam App News, or configured HTML into a common news candidate. | `server/src/Doggo.Scraper/News/INewsSourceAdapter.cs:6` |
| Source-adapter registry | Source-adapter registry | Selects the adapter matching a configured news source's type. | `server/src/Doggo.Scraper/News/SourceAdapterRegistry.cs:9` |
| Scraper scheduler | Scraper scheduler | Per-source due-time, failure threshold, and cooldown state machine for news fetches. | `server/src/Doggo.Scraper/News/ScraperScheduler.cs:10` |
| News store | News store | Persists normalized news candidates and maintains source counters after a crawl. | `server/src/Doggo.Scraper/News/NewsStore.cs:9` |
| Price-crawler worker | Price-crawler worker | Hosted loop that discovers and crawls configured price sites through the scheduler. | `server/src/Doggo.PriceCrawler/PriceCrawlerWorker.cs:18` |
| Price-site adapter | Price-site adapter | Turns site HTML/configuration or coded logic into normalized price observations. | `server/src/Doggo.PriceCrawler/Html/ConfiguredHtmlSiteAdapter.cs:14` |
| Price-crawler scheduler | Price-crawler scheduler | Applies global/site interval, concurrency, night-window, robots, retry, and circuit policies. | `server/src/Doggo.PriceCrawler/PriceCrawlerScheduler.cs:36` |
| Price-crawl ingestor | Price-crawl ingestor | Persists raw observations, maps external IDs, cleans/deduplicates series, and updates site state. | `server/src/Doggo.PriceCrawler/PriceCrawlIngestor.cs:22` |
| Price-review endpoint | Price-review endpoint | Admin API surface for mapping external crawl IDs to market items. | `server/src/Doggo.PriceCrawler/PriceCrawlerAdminEndpoints.cs:11` |
| Client experience | Client experience layer | Platform-specific user/operator interfaces, local presentation state, and REST translation. | `web/src/main.tsx:11` |
| Browser API client | Browser API client | Web fetch wrapper with cookie sessions, ProblemDetails parsing, and transparent refresh. | `web/src/api/client.ts:22` |
| Shared Taro client | Shared Taro client | The miniapp/RN request, token, storage-seam, session, and analytics transport implementation. | `shared/src/client.ts:8` |
| Shared endpoint functions | Shared endpoint functions | Typed functions describing miniapp/RN REST paths, parameters, and response views. | `shared/src/endpoints.ts:1` |
| Client event queue | Client event queue | Offline-tolerant local queue and batch flush for funnel analytics. | `shared/src/events.ts:1` |
| Admin access control | Admin access control | Runtime gate driven by the authenticated admin role claim. | `admin/src/access.ts:2` |
| Runtime surfaces and shared library | Runtime surfaces and shared-library layer | Database/cache, migration, proxy/static hosting, orchestration, and reusable Taro client library. | `deploy/docker-compose.prod.yml:35` |
| Production stack | Production stack | Ordered MySQL, Redis, migrator, API, Scraper, Worker, web, and admin deployment. | `deploy/docker-compose.prod.yml:6` |
| Database migrator | Database migrator | One-shot EF migration container completed before API startup. | `deploy/docker-compose.prod.yml:35` |
| Frontend reverse proxy | Frontend reverse proxy | Routes `/api` to the service and serves built web assets in production. | `deploy/nginx/doggo.conf.template:19` |
| Client identifier | Client identifier | `X-Doggo-Client` value parsed into the analytics client enum for visits and events. | `server/src/Doggo.Infrastructure/Analytics/AnalyticsClients.cs:10` |
| Visitor key | Visitor key | Stable salted hash of IP and user agent used to correlate anonymous activity without storing raw IP. | `server/src/Doggo.Infrastructure/Analytics/VisitorKeys.cs:8` |
| Session identifier | Session identifier | Client-generated durable identifier sent by `X-Doggo-Session`, refreshed after 30 idle minutes. | `shared/src/client.ts:179` |
