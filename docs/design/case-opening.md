# Case-Opening System Design


## Basic stance

- **The platform does not control opening probabilities.** Openings happen inside real CS2 and the drop is decided by Valve servers. Doggo's responsibilities are: inventory management, flow state machine, result records, item delivery.
- The only gameplay needing a probability algorithm is the roll room draw, which uses weights plus alias sampling (AliasMethod); the weight configuration is auditable.
- The display "case contents" configuration (quality / image / reference price) is metadata for display only and takes no part in drop decisions.

## Three opening modes

| Mode | Flow | Executor |
| --- | --- | --- |
| Self-open | **Not implemented in the open-source edition** — the flow below is kept for parity with the legacy platform: credential + Steam Guard handover → the user opens the case in game → the platform pulls a snapshot and links it to the order → the user requests the item back. `ServiceMode.SelfOpen` exists and is sold as `ProductStatus.SelfOpenOnly`, but there is no credential handover, guard flow or inventory-snapshot link-back in code | the user |
| Batch proxy-open | A series of cases on an account is sold by seat → payment reserves a seat → the opening is submitted → vapor opens that one seat (one `case_open` job per seat, carrying the batch `VaporCaseId` and the seat `crateAssetId` / `keyAssetId`) → the result webhook creates the prize | vapor proxy |
| One-click open | Opens every case on the account — **not supported in the open-source edition**: `open_all_crate` has no vapor counterpart and no code path here | vapor proxy |

## Integration with vapor

The legacy platform issued commands over Redis Stream (`open_crate` / `open_all_crate` / `inventory` / `inventory_history` / `trading`, etc.) consumed by an external bot process. In the open-source edition that layer is vapor:

| Legacy command | vapor capability |
| --- | --- |
| open_crate / open_all_crate | the `case_open` action of the CS:GO plugin; a real opening is gated by `open.enabled` on the vapor side, the request carries the `VaporCaseId` on the batch plus the seat's `crateAssetId` / `keyAssetId` |
| inventory / inventory_history | the inventory action and change events |
| trading | the trade-offer action (`send_trade_offer`, asset item keys `asset_id` / `app_id`) |
| password_auth / enable_guard | credential and SteamGuard session management |

Integration from the Doggo side: actions are started through the vapor REST API and result events are subscribed via webhooks (there is no SSE channel in this repo); results are linked back to the local prize record by order number. Checked against the vapor source on 2026-10-14: its notifications currently cover only the `job` / `session` / `auth` categories with no case-opening domain event yet, so the `case_opening` webhook category (`opening_result` plus the `stock_fetch_started` / `stock_ready` / `trade_started` / `trade_completed` chain) is a doggo-side convention handled by `CaseOpeningWebhookHandler`; the earlier client-facing seat-result endpoint was removed once the webhook drove those transitions, so seat results arrive only through the webhook.

## State machines

### Order

```
待支付 → 待结算 → 待取回 → 取回中 → 已完成
   │                    
   └→ 已取消(15 分钟未支付,默认 900 秒,可配置)
   └→ 已退款
```

- Pending settlement: paid, the account / seat has been delivered or locked.
- Pending claim: the case has been opened and the item sits in the bot's inventory.
- Claiming: the trade offer was sent and is not confirmed yet.
- Compensation jobs: claim checks (30 second granularity, configurable), unpaid cancellation.

### Inventory seat (batch proxy-open)

```
售卖中 → 锁定 → 已付款 → 开箱中 → 已开箱 → 获取库存 → 完成库存 → 交易中 → 完成
```

In English: selling → locked → paid → opening → opened → fetching inventory → inventory done → trading → done.

Seat record fields: batch lot, seat index, case assetId, key assetId, prize assetId, historyId cursor; the batch uses optimistic locking to control concurrent sales.

### Bot account (on the vapor side)

Stocked → inventoried → reviewed → listed → locked → sold → claimed, with bypass states reusable / abnormal / overdue. Doggo does not manage account credentials directly; the vapor client in this repository only dispatches jobs, reads a job back and pulls market listings, so the account stage column has no query endpoint behind it yet.

## Prize records

Fields persisted for an opening result (the full legacy field set, carried over by the open-source edition):

- Asset: `assetId` / `classId` / `instanceId`
- Metadata: market hash name, quality (seven rarity grades), appearance (wear tier), `floatValue`, rarity, icon
- Claim: inspection link, tradeable time
- Valuation: market price at sale time (from market sync)
- Links: order number, account, opening time, `PrizeSource` (`SelfOpen` / `GroupOpen` / `CloudOpen` / `Roll`; the roll-room draw reuses the same prize record with `Roll`)

## Item claim

1. The user configures Steam trade links (multiple, one default), the platform validates them.
2. Preconditions checked by `SubmitRetrievalAsync`: the order is still `PendingSettlement` / `PendingRetrieval`, the trade link belongs to this user and is not invalid, vapor is configured, and the order has at least one prize to send.
   Entry points: `POST /api/orders/{id}/retrieval` (body `{"tradeUrlId": …}`) starts it and moves the order to `Retrieving`; `POST /api/orders/{id}/retrieval/fail` rolls it back with a reason. Settlement itself is driven by the `order_retrieval` / `trade_completed` webhook, never by a client-supplied offer id.
3. A trade offer is sent (vapor trading action) and confirmation is awaited.
4. Once the offer is confirmed the order completes; on failure it rolls back to "pending claim" with the error recorded and is retried by the compensation job.

## Roll rooms (weighted draw)

- Room state machine: creating → running → drawing → draw finished → claiming → finished, auto-rolling to the next round.
- Participation rules: claim on login, per-order participation, free-plan orders, invite codes (fixed / random), free order when a specific rarity is drawn, and so on; rules can be combined in configuration.
- Draw: weighted AliasMethod draw over the prizes; prizes come from inventory items (shipped by trade) or from coins.
- Timed job: expiry draw checks.

## Concurrency and consistency notes

- Seat reservation / inventory locking: optimistic lock on the batch row plus conditional seat-status updates; failure means sold out.
- Payment callback idempotency: conditional order-status update; a duplicate callback simply returns success.
- Opening result idempotency: dedup by task / event ID, and a unique constraint on the prize record (order number + assetId).
- Trade status: the Steam side is the source of truth; local status may roll back but must never skip a step.
