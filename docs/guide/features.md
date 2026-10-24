# Features


This page lists Doggo's full planned feature set and current status. The baseline comes from the [legacy platform feature analysis](../design/legacy-analysis), trimmed and extended for the open-source edition.

## News aggregation (in-house)

| Feature | Description | Status |
| --- | --- | --- |
| CS2 / Dota 2 news sources | Official announcements, patch notes, esports news; sources are configuration data with built-in seed sources | done |
| Scraping pipeline | Scheduling, fetching, deduplication, storage (`Doggo.Scraper`) | done |
| News display | List / detail (`GET /api/news`) | done |
| Editorial content | Announcements, banners, custom articles (API + admin console); the web home shows announcements / banners, the article detail page is `/articles/:id`, the miniapp and mobile homes show announcements | done |

The legacy platform had only one generic article module; multi-source CS2 / Dota 2 news aggregation is a **new capability** of the open-source edition.

## Skin market data (based on vapor)

| Feature | Description | Status |
| --- | --- | --- |
| Market price sync | Pulls Steam market listings through vapor, backs off automatically on failure; reads the latest snapshot when vapor is unavailable | done (vapor integration pending vapor) |
| Price snapshots and dailies | Every sync stores a snapshot, skipping unchanged prices; folds into daily OHLC | done |
| Market list and search | `GET /api/market/items` (keyword, game, type, paging, includes latest price); the web market page has a keyword search box, game/type filters and "load more" paging | done |
| Price trend display | `GET /api/market/items/{id}/prices` as a daily OHLC series | done |
| Exchange rates | `GET /api/exchange-rates`; the web market list shows a CNY equivalent for non-CNY prices using the rate | done |
| vapor webhook | `POST /api/vapor/webhook` (HMAC-SHA256 signature check, time-window validation, Redis idempotency dedup) | done |
| Skin catalog | Skin metadata synced by vapor (name, market hash name, icon) | done (vapor integration pending vapor) |
| Cases / plans / contents / quantity tiers | Entities exist, the catalog import pipeline is not implemented | not implemented |

## Third-party price crawler (in-house)

| Feature | Description | Status |
| --- | --- | --- |
| Third-party site price scraping | `Doggo.PriceCrawler` as a standalone module, configuration-driven HTML adapters, merged into the Scraper process | done |
| Scheduling and rate control | Per-site serial loop, night window, robots gate, circuit breaker, exponential backoff, 90-day purge of raw rows | done |
| Cleaning and dedup | Currency normalisation, price upper bound, 60-minute window dedup, rolling-median suspect marking | done |
| Trends and comparison | Daily close, day-over-day, week-over-week, period change; spread report against in-site snapshots | done |
| admin API | `/api/admin/price-crawl`: map list and review, price series, spread-report comparison | done |
| Front-end reference price | `GET /api/market/items/{id}/reference`: the median of each confirmed site's newest non-suspect converted crawled price over the last 7 days, against the latest in-site close as a spread; the web market trend page renders the comparison block (fields are null and the block is hidden when there is no series); per-site rows stay admin-only, mobile does not add it in phase 1 | done |

## Case-opening system

| Feature | Description | Status |
| --- | --- | --- |
| Batch proxy opening | Batch / seat sales: optimistic-lock seat reservation, sell-out detection, vapor opening submission, result events stored as prizes; see the [case-opening system design](../design/case-opening) | done (vapor integration pending vapor) |
| Prize records | Full item fields (asset ID / wear / rarity / estimated value), my-prizes list and detail, market price snapshot links, opening log queries | done |
| Inventory management | Seat state machine (selling → … → done), batch optimistic locks, skipped / rolled-back steps rejected | done |
| Skin claim | Delivered via Steam trade offer, trade-link validation | done (vapor integration pending vapor) |
| Product list and search | `GET /api/products` (keyword, type, paging, listed products only); the web store page has a keyword search box, type filters and "load more" paging, the miniapp and mobile product lists have keyword search and load more | done |
| Roll rooms | Template-based rooms (8 rule kinds: claim on login / per-order participation / invite code and so on), join validation, draw on expiry (AliasMethod weighted draw), item or points payout, auto-roll to the next round per template, `GET /api/roll/rooms`, `GET /api/roll/rooms/{id}` | done (item prizes record first, real delivery goes through the claim flow; vapor integration pending vapor) |

## Steam bot (based on vapor)

| Feature | Description | Status |
| --- | --- | --- |
| Account hosting | Encrypted credential storage, SteamGuard, session maintenance | provided by vapor |
| Opening execution | In-game case-opening action | provided by vapor |
| Trading | Sending item trade offers, trade-link management | provided by vapor |
| Inventory | Backpack snapshots, change ledger, inventory reconciliation | provided by vapor |
| Integration | Doggo calls vapor through REST API / webhooks | done |

## Users and orders

| Feature | Description | Status |
| --- | --- | --- |
| User accounts | Register / login, Steam OpenID / WeChat miniapp login, JWT | done |
| Orders | State machine (pending payment → pending settlement → pending claim → claiming → done, cancel / refund), timeout cancellation, reconciliation and compensation (auto-refund for stuck orders, retry for failed claims) | done |
| Trade links and skin claims | Trade-link CRUD and format validation (bindable from the user center on web, mobile and miniapp), claims initiated through vapor, completion / failure callbacks advance the order | done (vapor integration pending vapor) |
| Payment | Pluggable payment interface with a built-in mock channel (payment link, callback signature check, idempotency, refund); real channels wired in as needed | done (mock channel) |
| Points / coupons | Coin ledger (itemised on all three clients, full view in an admin drawer), coupon claim and deduction, expiry policy, coupon centre (claimable from the user center on all three clients), redemption codes for points or coupons (entry in the user center on web / miniapp / mobile) | done |
| Invites and check-in | Invite rebates (`GET /api/invites` for one's own rebate detail and total, visible on web / miniapp / mobile), weekly cyclic check-in | done |

## Admin console

| Feature | Description | Status |
| --- | --- | --- |
| Content management | News, announcements, banners, articles, news sources | done |
| Product management | Create and edit products, list / delist, plans and options, quantity tiers, content writes (whole-group replace), promotions, exchange rates | done |
| Order management | Queries, refunds, prize viewing | done |
| User management | Account disable, coin ledger, login logs | done |
| Statistics | Overview panel, 14-day trend, product ranking | done |
| Site settings | In-row editing of `SiteSetting` key/value pairs (`GET/PUT /api/admin/settings`), with add / overwrite | done |
| Crawler site overview | The settings page aggregates map count and pending-confirmation count from `price-crawl/maps` | done |
| Price crawler review | The "analytics → price crawler" page: filter maps by status + confirm / reject, switch the comparison table between 7 / 30 days, read-only comparison with no write-back to in-site market data | done |
| Payment channels and delivery windows | The settings-page channels card manages PaymentChannel config (merchant id / app id / fee bps / secret ref via `PUT /api/admin/payment-channels/{provider}`; secret values stay in server env vars); the content-center Banner tab manages delivery windows (same-position overlap rejected with 409) | done |
| Permissions | IsAdmin flag + role claim policy; see `admin/` | done |

## Funnel analytics (in-house)

| Feature | Description | Status |
| --- | --- | --- |
| Four-step funnel | View product → add to cart (intent) → place order → pay, UV / conversion rate / drop-off rate | done |
| Event channel | Client-side queued reporting to `POST /api/events`, offline replay, idempotency dedup, rate limiting | done |
| Server-authoritative events | order_created / order_paid in the same transaction as the order and callback | done |
| Daily aggregation | Runs T-1 at 00:30 UTC, rolling recompute over 7 days, grouped by client / channel / day | done |
| Admin funnel page | admin "analytics → conversion funnel" | done |

## Clients

| Feature | Description | Status |
| --- | --- | --- |
| WeChat miniapp | News, announcements, market, product orders, opening results, user center (coins / coupons / redemption codes / trade links / check-in), WeChat login (`miniapp/`, Taro) | done |
| Android / iOS | A mobile app reusing the miniapp shared layer (`mobile/`, Taro RN, currently delivering an Android APK), the same feature surface plus order queries and payment polling | done |

## Deployment

| Feature | Description | Status |
| --- | --- | --- |
| Docker full stack | MySQL / Redis / migrations / API / Scraper / Worker / web / admin orchestration, see [Deployment](./deployment) | done |
| Daily build | GHCR nightly images plus optional auto-roll, see `.github/workflows/daily.yml` | done |

## Out of scope for the open-source edition

- Multi-domain advertising placement and tracking (depends on registration-entity and similar operational setup)
- Display-data generation jobs
- Specific payment channels (the interface is provided; the deployer wires in the real channel)
