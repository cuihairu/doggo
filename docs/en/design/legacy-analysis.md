# Legacy Platform Feature Analysis


This page inventories the full feature set of the production platform this project originated from, as the feature baseline for the open-source rewrite. It records per module what the platform actually did and how, without any reference to the original branding.

## 1. Platform shape

The legacy platform is a **CS2 case proxy-buy + case-opening service**, core model:

- users buy a "Steam account loaded with real weapon cases plus keys";
- openings are fulfilled two ways: a platform-hosted Steam bot **proxy-opens**, or credentials are handed over and the user **opens them themselves**;
- the drop is decided by Valve servers — the **platform does not control item probabilities**;
- opened items are returned to the user via Steam trade offers;
- the platform handles account hosting, automation execution, snapshot records, inventory and order state machines, and item claims.

Supporting capabilities: a skin price crawler (8 domestic trading sites), a marketing system (roll rooms / coupons / points / campaigns), multi-channel ad attribution tracking, and an admin console.

Technical shape: a Java 21 + Spring Boot 3.4 single, multi-module monorepo (MySQL 8 + Redis) with about 744 Java files; the admin console is React 18 + Ant Design Pro (UmiJS); the Steam bot is a separate process outside the repository, talking to the platform over a Redis Stream message queue.

## 2. Module split

| Module | Responsibility |
| --- | --- |
| common | 37 business enums, utilities (alias sampling algorithm, encryption/decryption, SteamGuard helpers), a delayed task queue |
| dao | MyBatis-Plus entities for about 70 tables, annotation-driven table and index creation |
| service | all business logic, about 130 services (orders / inventory / openings / roll rooms / campaigns / payment / SMS / points / coupons / notifications) |
| bot | Bot task bridge layer: writes commands into Redis Stream, consumes the result stream and persists it |
| tracking | Ad conversion attribution (Baidu Marketing API, 360点睛 API) |
| server | Consumer REST API (JWT auth) + all timed jobs + payment gateway integration |
| admin | Admin API (RBAC, IP allowlist, OSS, retention stats), about 70 controllers |
| scraper | Skin price crawler as a separate process (Selenium/Chrome + HTTP API) |

## 3. Steam bot integration

### 3.1 Communication boundary

The legacy Java side never parses the Steam protocol; the real bot is an external process. The boundary is two Redis Streams plus one persistence table:

- command stream `bot_task_<command>`, result stream `bot_task_ret_<hostname>`;
- the `bot_task` table records tasks (producer / consumer / action / encrypted arguments / encrypted result / status / deadline);
- 2 second rate limit per account per action; tasks time out at 30 minutes; results are claimed by the `server` consumer group and dispatched by handlers; tasks older than 30 days are purged daily;
- consumers register their online status by hostname.

### 3.2 Command set

| Command | Effect |
| --- | --- |
| password_auth | Validates that Steam account credentials work |
| enable_guard | Binds a Steam Guard token to an account (used for batch import) |
| inventory | Pulls a backpack snapshot for a bot account |
| inventory_history | Pulls incremental backpack changes (CS2 only, cursor by historyId) |
| trading | Sends an item trade offer to the user's trade link |
| trade_url_info | Validates the user's trade link |
| open_crate | Group-buy batch opening (a series of cases) |
| open_all_crate | One-click open of every case on the account |
| open_crate_trading / open_crate_inventory / open_crate_inventory_history | Dedicated variants for group-buy orders |

Task arguments always carry the account credentials and SteamGuard secret (stored encrypted), items, trade link and order number.

### 3.3 Result handling

| Handler | Behaviour |
| --- | --- |
| Password validation | On success writes the credentials to the user's bound account |
| Backpack snapshot | Writes the snapshot table, distinguishing admin warehouse accounts by source |
| Backpack change | Parses item gains/losses and the trade counterpart, writes the inventory ledger transactionally, advances the cursor |
| Trading | Success → order set to "claiming"; failure rolls back with the error recorded |
| Opening result | Writes a prize record per order number (filling in market price and image), advances group-buy inventory status, aggregates the order status |
| Trade-link validation | Writes back the validation status / nickname / error |

### 3.4 Bot account state machine

`creating (stocking) → pending inventory → inventory in progress → inventory done (manual review) → online and listed → order-locked → sold → claimed`, with bypass states reusable / has balance / selling / abnormal / overdue.

Operations: Excel batch account import, batch Guard-token import, batch online/offline, inventory reconciliation, converting to a warehouse account, backpack snapshots, SKU stock summary (in stock / cooldown / sold / stocked / error), cooldown reasons and saleable time.

## 4. Price crawler

- Scope: CS2 (appId 730) only, the two product types weapon cases and keys; no Dota2, no news scraping.
- Sources: 8 domestic third-party skin trading sites (a mix of HTTP API and Selenium, e.g. 悠悠有品, C5Game, ZBT, IGXE, CSQAQ), some sites doubling as exchange-rate sources.
- Scheduling: one round every 10 minutes, staggered by a delayed queue using per-site fetch intervals and request delays; random sleeps to mimic human behaviour; non-headless Chrome.
- Persistence: `fetcher_price` (site / name / price / quantity / type / currency / image) plus a site management table (interval / headers / last and next fetch times).
- Consumption: synchronised as the product pricing reference (final price = max(crawler reference, purchase price) + adjustment); opening rewards are filled with the current market price by item name; the admin console offers "sync crawler prices" and "clear cache" operations.

## 5. Product system

- Three product kinds: key proxy-buy, case plan, component plan; types distinguish case / key / bundle.
- Plan model: plan (case_plan) + option (case_plan_option). Options carry minimum batch quantity, markup ratio (**the key markup rate, not a drop rate**), whether coupons / points can be used, cashback and its multiplier, quantity tiers (min/max).
- Limited promotion: discounted price, stock, per-user limit, daily reset.
- Quantity tiers: purchase-quantity threshold plus a stock alert line.
- Recommended plans, purchase exchange rates (multi-currency + Steam rates, two data sources).
- Case contents display configuration (case_content: title / quality / colour / image / reference price); quality is the seven CS2 rarity grades (white / light blue / blue / purple / pink / red / gold).

## 6. Orders and payment

- Five order entry points: campaign page / case opening / key / Taobao / group buy; order numbers are an entry prefix + base36.
- At order time the price is assembled and one bot account is locked as inventory; the payment deadline is 10 minutes, expiring to an automatic cancellation.
- Payment channels: Alipay SDK, aggregated payment (main channel), DSA-certificate gateway, WeChat official-account payment, full points deduction, coupon deduction on top; callbacks converge on one entry point that updates the order.
- Order state machine: pending payment → pending settlement → pending claim → claiming → done; bypasses: cancelled / refunded.
- Timed compensation: trade checks (20 minutes plus hourly / 4-hour compensation), claim status reconciliation, daily reset of promotion limits.
- Taobao card codes: order number + Base64 card code redeems account credentials, entered with a trade link to claim.

## 7. Case-opening flows

Three modes:

1. **Self-open**: after purchase the system delivers one bot account's credentials plus the Steam Guard token; the user opens the cases in the real game; the platform pulls a backpack snapshot and links it to the order; at claim time it validates (cases / keys used up, trade cooldown) and then sends a trade command to deliver the item.
2. **Group-buy proxy-open ("one shot, one soul")**: one batch is a series of cases on one bot account, sold by seat (lot/seat); after the user pays and reserves a seat, the opening command is submitted and the bot also runs the earlier unopened cases on the same account (queued openings + keeping the session logged in); the result callback creates the prize records.
3. **One-click open (cloud open)**: opens every case on the account.

Seat state chain: selling → locked → paid → opening → opened → fetching inventory → inventory done → trading → done.

Prize record fields: assetId / classId / instanceId, market hash name, quality, appearance (wear tier), floatValue, rarity, inspection link, tradeable time, estimated value, image.

## 8. User system

- Registration (SMS verification code), password / code login, JWT; avatar / nickname / phone / password management; real-name verification; login and visit logs (daily UV / PV).
- **Platform points**: balance, ledger, redemption-code top-ups, FIFO consumption, hourly expiry, refund credit-back.
- **Coupons**: coupon types (threshold discount / percentage), applicable products, redemption via codes.
- **Invite rebates**: invite codes, reward ledger.
- **Check-in**: calendar-week cycle, points rewards, make-up signing with that day's order.

## 9. Marketing and campaigns

- **Roll rooms**: room state machine (creating → running → drawing → draw finished → claiming → finished); eight participation rules (claim on login / every purchase / free-plan orders / fixed or random invite codes / all-blue / red drop / free on a gold drop); the draw uses **alias sampling (AliasMethod) by weight**, prizes are warehouse items (shipped by bot trade) or points; supports pre-allocation and manual assignment; auto-rolls to the next round.
- **Campaign centre**: lotteries, prize wheels (reward value / weight / stock / limit, supporting simulated batch draws), friend blessings, wish making; campaign configuration and claim.
- **Content**: banners, announcements, partners, support QR codes (time-windowed), article management (categories: general / esports / news / events, rich text, view count) — this was the legacy platform's only "news" capability.
- **Display embellishment (operational tricks, cuttable in the rewrite)**: a home-page statistic inflation factor, luck leaderboards / lucky moments, scrolling fake orders, simulated-data generation jobs (fake users / fake prizes / fake roll room participants / fake sales, driven by timed jobs).

## 10. Data statistics (admin)

Dashboard (total users / new today / active / topped up), new users, order trend (ARPU / ARPPU / active count), channel unique visitors, user retention, product tracking, spending ranking, promotion-channel management (registration entity / domain).

## 11. Ad attribution tracking

- Event attribution: registration / login / payment / first payment / next-day retention / click-to-buy, attributed asynchronously to the Baidu Marketing API and the 360点睛 API with retry on failure.
- Channel attribution: each placement channel has its own domain / registration entity / ICP number / public-security number / API token; nginx carries 10 server blocks to support multiple placement sites.

## 12. Data model overview

About 70 tables, grouped by domain (key fields):

| Domain | Tables | Highlights |
| --- | --- | --- |
| Products | product, case_plan (+option), key_plan (+option), crate_group_buy_plan (+option), case_promotion_plan, case_quantity_option, product_recommend, product_tracker, case_content | Multi-level pricing (purchase / reference / adjusted / final), planFlags, daily visitor and sales stats |
| Orders | user_order | Entry / four prices / points deduction / coupon deduction / cashback / payment channel / trade link / status / error / serviceMode |
| Openings | group_buying, group_buying_inventory, user_prize, user_prize_log | Batch / seat / optimistic lock; the seat stores case and key assetId, prize assetId, status 0~10; prizes carry the full item field set |
| Bot assets | steam_bot_account, steam_bot_backpack_snap, steam_bot_inventory_history, steam_bot_repo_account (+inventory), steam_asset, bot_task | Encrypted credentials, state machine, snapshot JSON, ledger cursor |
| Users | user_account, user_steam_account, user_steam_trade_url, user_steam_backup_code, user_coin (+log/redeem_code), user_coupon (+coupon/redeem_code), user_sign (+record), user_invite_reward_log, user_login_log, user_visit, user_retention_stats | FIFO point consumption and expiry, multiple trade links (preferred) |
| Operations | banner, notice, article, partner, support_qrcode, site_settings, short_url, exchange_rate, activity×7, roll_room×4, fetcher_price (+site) | Site settings drive feature flags |
| Admin | admin_account, admin_role, permission, role_permission, admin_ip_whitelist, admin_login_log, admin_notification_channel (+event) | RBAC + IP allowlist + notification channels |

## 13. Middleware and deployment

- **Storage**: MySQL 8 (utf8mb4, automatic table creation, logical delete), Redis (cache / distributed counters / rate limiting, plus a Redis Stream message queue on a separate instance with custom encoding).
- **Runtime**: Undertow container, virtual threads (crawler), timed jobs embedded in the server process.
- **Third parties**: Alipay / aggregated payment / WeChat official-account payment, Alibaba Cloud SMS and OSS, an exchange-rate API, Steam public APIs (trade-link parsing, inventory JSON).
- **Notifications**: DingTalk bot, WeCom, email, SMS (factory pattern, events: new order / refund / low stock, etc.).
- **Security**: Spring Security + JWT, admin IP allowlist + login logs, OWASP AntiSamy XSS cleaning, encrypted credential storage.
- **Deployment**: nginx with multiple domains, systemd, GitHub Actions (self-hosted runner), daily MySQL backups.

## 14. Mapping and trade-offs in the open-source edition

| Legacy capability | Where it goes in the open-source edition |
| --- | --- |
| External Steam bot process + Redis Stream bridge | Taken over by [vapor](https://github.com/cuihairu/vapor): control plane / proxy, SteamGuard, trading, inventory, opening actions, REST API / webhook integration |
| Skin price crawler (8 domestic sites) | Converged into vapor's Steam-side market sync; third-party site sources remain as an extension point |
| Java server/service/admin API | Rewritten in ASP.NET Core (Doggo.Api) |
| News article management (single module) | Expanded into a full CS2 / Dota 2 news aggregation pipeline (Doggo.Scraper, in-house) |
| React admin console | Rewritten (tech choices in [Design overview](./overview)) |
| Ad attribution multi-domain system | Out of scope for the open-source edition (sensitive operational setup such as registration entities) |
| Display embellishment jobs (fake-data generation, etc.) | Out of scope for the open-source edition |
| Payment gateways (Alipay / aggregated / WeChat) | Designed as a pluggable payment interface; concrete channels wired in as needed |
