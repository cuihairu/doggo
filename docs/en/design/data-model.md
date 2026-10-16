# Data Model

[English](/en/design/data-model) | [简体中文](/design/data-model)

The target data model of the open-source edition, grouped by domain. Fields were distilled from about 70 tables in the legacy platform and reorganised for the .NET stack; sensitive data such as bot account credentials is **not stored in the Doggo database** and is managed by vapor.

## User domain

| Entity | Highlights |
| --- | --- |
| UserAccount | Phone / email registration, nickname, avatar, invite code, status; password hash |
| UserSteamProfile | The user's Steam binding: SteamId, nickname, avatar |
| UserTradeUrl | User trade links (multiple, default selectable, validation status and error) |
| UserCoin / UserCoinLog | Point balance and ledger; FIFO consumption, expiry by batch |
| UserCoupon / Coupon | Coupons: type (threshold discount / percentage), threshold, validity, applicable product scope |
| RedeemCode | Points / coupon redemption codes, batch generation and redemption |
| UserLoginLog / UserVisit | Login records (IP / UA / result), visit statistics (daily UV / PV) |
| InviteRewardLog | Invite reward ledger |
| SignRecord | Cyclic check-in (calendar week) and make-up signing |

## Product domain

| Entity | Highlights |
| --- | --- |
| Product | Product: type (case / key / bundle), classId, purchase price, reference price, price adjustment, final price, listing status |
| CasePlan / CasePlanOption | Plans and options: minimum batch quantity, markup rate (for pricing), whether coupons / points can be used, cashback multiplier, quantity tiers |
| PromotionPlan | Limited promotion: discounted price, stock, per-user limit, daily reset |
| QuantityOption | Quantity tiers and stock alert threshold |
| CaseContent | Case contents display: title, quality (seven grades), colour, image, reference price |
| Recommend | Recommendation slots |
| ExchangeRate | Multi-currency rates (for display) |

## Order domain

| Entity | Highlights |
| --- | --- |
| UserOrder | Entry, product type, plan, the four prices (list / sale / total / cost), points deduction, coupon deduction, cashback, payment channel and callback, trade-link reference, status, serviceMode (self-open / proxy-open) |
| PaymentRecord | Payment ledger: channel, amount, callback payload, idempotency key |
| RefundRecord | Refund record |

## Case-opening domain

| Entity | Highlights |
| --- | --- |
| GroupBuying | Batch: product, batch number, seat count, seats sold, status, optimistic-lock version |
| GroupBuyingSeat | Seat: index, case assetId, key assetId, prize assetId, status (selling → … → done) |
| UserPrize | Prize: assetId / classId / instanceId, market hash name, quality, appearance, floatValue, rarity, icon, inspection link, tradeable time, estimated value, order link, source |
| UserPrizeLog | Opening log (opening time, account reference, result) |
| RollRoom (+Config/Participants) | Roll room: template, rules, status, prize configuration (item / points + weights), participants, draw and claim records |

## News domain (new)

| Entity | Highlights |
| --- | --- |
| NewsSource | News source: name, type (official / community), scraping configuration, enabled flag |
| NewsItem | News entry: title, summary, body, original link, source, game (CS2 / Dota2), category, tags, publish time, fingerprint dedup key |
| Article | Editorial article: rich text, banner image, category, keywords, view count, listing status |
| Notice / Banner / Partner | Announcements, ad slots, partners |

## Market domain

| Entity | Highlights |
| --- | --- |
| MarketItem | Skin / case metadata: classId, market hash name, icon, type |
| MarketPriceSnapshot | Price snapshot: the point-in-time price synced by vapor, aggregated per day for trends |
| SyncCursor | Sync cursor (per data source) |

## System domain

| Entity | Highlights |
| --- | --- |
| AdminAccount / Role / Permission | RBAC: admin accounts, roles, permissions, role-permission links |
| AdminLoginLog / IpWhitelist | Admin login logs, IP allowlist |
| SiteSetting | Site configuration key/value pairs (feature flags) |
| SupportQrcode | Support QR code (time-windowed) |
| NotificationChannel / Event | Notification channel configuration and event records |
| ShortUrl | Short URLs and click counts |

## Common conventions

- Primary keys: generated on the application side (e.g. ULID / snowflake), to avoid leaking scale through auto-increment IDs.
- Money: `long` in minor currency units (cents); the currency field travels with the row.
- Soft delete: a deleted-at timestamp; queries filter it by default.
- Time: stored in UTC, converted to the local zone at display time.
- External asset references (Steam assetId and so on) keep the raw value plus the source cursor, for easier reconciliation.
