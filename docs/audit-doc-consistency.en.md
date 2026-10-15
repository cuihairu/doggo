# Documentation vs Source Consistency Audit

[English](/audit-doc-consistency.en) | [简体中文](/审计-文档一致性)

2026-10-13. Audited scope: README (EN and ZH), every page under `docs/**` and the design documents, the VitePress config, the public GitHub front (description + topics), and the TODO.md checkboxes. Method: every claim is checked against `file:line`; negative conclusions are only recorded after confirming the search path exists.

Categories: ① docs ahead of code (risky) / ② code ahead of docs / ③ description mismatch.

## Differences

| ID | Page | Claim | Source evidence (file:line) | Category | Severity | Fix |
| --- | --- | --- | --- | --- | --- | --- |
| D-01 | README.md L7/L26, README.zh.md L7/L26, docs/index.md L6-7, docs/.vitepress/config.mts L11, docs/guide/introduction.md L3, GitHub description | "gaming info & community platform / 游戏资讯与社区平台" | `server/src/Doggo.Domain/` has 11 domain directories (Analytics/Cases/Common/Market/News/Orders/Price/Products/System/Users) with no community/forum/comment entities; a repo-wide grep for comment/forum/community returns zero business hits | ① | P0 | Remove the "community/社区" wording; sync the description with `gh repo edit` |
| D-02 | docs/guide/features.md L26 | Case and skin catalog: "箱子、套餐、内容物、数量档,资料随抓取导入", marked done | No code writes `Product` rows: `Doggo.Infrastructure/Market/MarketStore.cs:108` only writes `MarketItem`; migrations contain no `InsertData`; neither Api nor Worker has a Product creation entry point | ① | P0 | Split into two items: the skin catalog (vapor sync) is done, the case plan / product catalog import is not implemented |
| D-03 | docs/guide/architecture.md L36 | Module table lists `Doggo.Core` | `server/Doggo.slnx` only contains Api/Domain/Infrastructure/PriceCrawler/Scraper/Worker, no Doggo.Core | ① | P1 | Drop Doggo.Core, keep Doggo.Domain |
| D-04 | docs/guide/architecture.md L35 | `Doggo.Worker` responsibilities include "message push" | `Doggo.Worker/Program.cs:19,50-55` registers 6 workers: MarketSync/OrderTimeout/OrderReconciliation/RollRoom/CoinExpiry/FunnelAggregate, no push task | ① | P1 | Drop "message push"; note that the crawler schedule lives in `Doggo.Scraper/News/ScraperScheduler.cs:33` |
| D-05 | docs/guide/architecture.md L59 | Observability: "structured logging + Prometheus metrics" | No csproj references prometheus-net or any metrics package; `MapMetrics` has zero hits | ① | P1 | Change to "structured logging" and mark Prometheus as planned |
| D-06 | docs/design/price-crawler-design.md L3, L358 | "this round only delivers this document"; "routing and JSON sketches only, not implemented" | Implemented: `server/src/Doggo.PriceCrawler/` (13 files); mounted in `Doggo.Scraper/Program.cs:46`; admin endpoints registered in `Doggo.Api/Program.cs:135`; migration `20261013000731_PriceCrawlTables` | ② | P1 | Change the status line to implemented (0b08b2c) and mark §8 implemented |
| D-07 | docs/guide/features.md (whole file) | No "external price crawler" module entry | The `Doggo.PriceCrawler` module and the `/api/admin/price-crawl` surface (maps/prices/spread-report/confirm/reject) are implemented | ② | P1 | Add an "external price crawler" section to features.md |
| D-08 | docs/guide/features.md (whole file) | No "funnel analytics" entry | `AnalyticsEvent` / `FunnelDailyStat` entities, `POST /api/events`, `GET /api/admin/funnel`, and the admin funnel page are all implemented | ② | P1 | Add a "funnel analytics" section to features.md |
| D-09 | docs/design/overview.md L57-60 | Repository layout src list omits `Doggo.PriceCrawler` | `server/src/Doggo.PriceCrawler/` exists | ② | P1 | Add Doggo.PriceCrawler to the layout diagram |
| D-10 | docs/guide/architecture.md L31-41 | Module responsibility table lacks the PriceCrawler and Analytics rows | Same as D-07 / D-08 | ② | P1 | Add both rows |
| D-11 | README.md L87 | "all 159 checklist items in TODO.md are done" | TODO.md actually has 185 items (185 checked, 0 unchecked) | ③ | P1 | Change to 185 |
| D-12 | docs/guide/introduction.md L26 | "项目处于早期建设阶段,当前以文档与架构设计为主" | M0-M12 all shipped; 469 server tests green | ② | P1 | Update to the current state and point at the TODO open items |
| D-13 | docs/guide/miniapp-compliance.md L29 | Case-opening result via "GET /api/prizes/mine" | The actual route is `GET /api/users/{userId}/prizes` (`PrizeEndpoints.cs:10`); `/api/prizes/mine` does not exist | ③ | P1 | Fix the path |
| D-14 | docs/guide/getting-started.md L59-67 | Roadmap lacks the price crawler and funnel modules | Both modules are implemented | ② | P2 | Add both entries to the roadmap |
| D-15 | docs/.vitepress/config.mts L35-45 | Design sidebar only holds 4 documents | `price-crawler-design.md`, `analytics-funnel-design.md`, `ui-system.md`, `prototypes/README.md` exist but are not in the sidebar | ② | P2 | Add the 4 entries to the sidebar |
| D-16 | docs/design/analytics-funnel-design.md §12 | All 10 pending items listed as undecided | 1-7 and 10 implemented per the recommendations (69da251); 8 (Client column on UserVisitLog) and 9 (detail retention) not done | ② | P2 | Annotate the implementation status in the table |
| D-17 | docs/design/price-crawler-design.md §10 | All 10 pending items listed as undecided | 2-10 implemented per the default suggestions (0b08b2c); 1 (target site list) still awaits the user | ② | P2 | Annotate the table |
| D-18 | docs/design/case-opening.md L37 | Order "cancelled (10 minutes without payment)" | `Doggo.Infrastructure/Orders/OrderOptions.cs:8` PayTimeoutSeconds=900, i.e. 15 minutes | ③ | P2 | Change to 15 minutes |
| D-19 | docs/design/case-opening.md L44 | "claim check (20 minute granularity + periodic compensation)" | `Doggo.Infrastructure/Cases/RollOptions.cs:9` WorkerIntervalSeconds=30, i.e. 30 second granularity (configurable) | ③ | P2 | Change to 30 seconds (configurable) |
| D-20 | web/README.md (whole file) | Vite template boilerplate ("This template provides a minimal setup…") | `web/` is the doggo consumer site (React 19 + Vite 8, multi-page multi-route) | ③ | P2 | Replace with a real description |
| D-21 | docs/guide/miniapp-compliance.md L21 | "本地仅存 JWT accessToken/refreshToken" | `shared/src/client.ts:163-164,185-203` also stores `doggo.analyticsSession` and `doggo.analyticsChannel` | ③ | P2 | Reword to "the login state stores only the JWT pair, plus local session and channel keys" |
| D-22 | docs/guide/deployment.md L53 | "after replacing …ADMIN_ALLOW_IP" | `deploy/nginx/doggo.conf.template:83-85` the allow/deny lines are commented placeholders with no active lines | ③ | P2 | Note that they must be uncommented and filled in |

## Verified consistent (sampled)

- Guides: healthz response shape (`Program.cs:137-161` matches getting-started L42), MySQL 8.4 + Redis 7 (`deploy/docker-compose.yml:3,23`), the ef migration commands, ports and env variables (`docker-compose.prod.yml:66,111,121`), the backup policy (`backup-mysql.sh:18-19,29-30`), the five GHCR nightly images with the 48 hour skip (`daily.yml:120-169`), the DEPLOY gate (`daily.yml:180-192`).
- features.md: news / market / exchange rates / vapor webhook / case opening / prizes / seat state machine / claiming / products / roll rooms (8 rule kinds, `CaseEntities.cs:35-45`) / order state machine / payment mock / points and coupons / invite and check-in / admin console / mobile — checked item by item.
- Architecture: the vapor control plane POST /v1/jobs + polling, webhook signature verification and idempotency, snapshot fallback, exponential backoff (`VaporClient.cs:65-157`, `VaporWebhook.cs:34-78`, `MarketSyncWorker.cs:97-122`).
- Miniapp compliance: the AppID placeholder, wx.login, the openid unique index, payOrder semantics, the three plain-text tabBar items, urlCheck, the WeChat env variables — checked item by item.
- The prototype file structure (the prototypes README matches the actual file tree); the files named in ui-system §8 exist.

## Fix record

2026-10-14: D-01 through D-22 all fixed (in-page edits + the README count wording + an Analytics row added to the architecture module table + the front page description synced). The TODO count was refreshed again as the checkboxes advanced (§10.3-2 web checkout flow).

Front page change: `gh repo edit cuihairu/doggo --description "Open-source gaming info platform: CS2 / Dota 2 news aggregation, skin market data, case opening. C# / .NET backend, TypeScript + React frontend."`
