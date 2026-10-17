# Case-Opening System Design


## Basic stance

- **The platform does not control opening probabilities.** Openings happen inside real CS2 and the drop is decided by Valve servers. Doggo's responsibilities are: inventory management, flow state machine, result records, item delivery.
- The only gameplay needing a probability algorithm is the roll room draw, which uses weights plus alias sampling (AliasMethod); the weight configuration is auditable.
- The display "case contents" configuration (quality / image / reference price) is metadata for display only and takes no part in drop decisions.

## Three opening modes

| Mode | Flow | Executor |
| --- | --- | --- |
| Self-open | The platform hands over account credentials + Steam Guard token → the user opens the case in game → the platform pulls a snapshot and links it to the order → the user requests the item back | the user |
| Batch proxy-open | A series of cases on an account is sold by seat → payment reserves a seat → the opening is submitted → the bot opens cases in queue (with earlier unopened cases on the same account) → the result callback creates the prize | vapor proxy |
| One-click open | Opens every case on the account | vapor proxy |

## Integration with vapor

The legacy platform issued commands over Redis Stream (`open_crate` / `open_all_crate` / `inventory` / `inventory_history` / `trading`, etc.) consumed by an external bot process. In the open-source edition that layer is vapor:

| Legacy command | vapor capability |
| --- | --- |
| open_crate / open_all_crate | the `case_open` action of the CS:GO plugin; a real opening is gated by `open.enabled` on the vapor side, the request carries the `VaporCaseId` on the batch plus the seat's `crateAssetId` / `keyAssetId` |
| inventory / inventory_history | the inventory action and change events |
| trading | the trade-offer action (`send_trade_offer`, asset item keys `asset_id` / `app_id`) |
| password_auth / enable_guard | credential and SteamGuard session management |

Integration from the Doggo side: actions are started through the vapor REST API and result events are subscribed via webhooks / SSE; results are linked back to the local prize record by order number. Checked against the vapor source on 2026-10-14: its notifications currently cover only the `job` / `session` / `auth` categories with no case-opening domain event yet, so seat results are back-filled through the `/api/group-buyings/{id}/seats/{seatNo}/result` endpoint; the `case_opening` event category on the webhook side is kept as a doggo-side convention until vapor provides domain events.

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

Stocked → inventoried → reviewed → listed → locked → sold → claimed, with bypass states reusable / abnormal / overdue. Doggo does not manage account credentials directly; it queries status through vapor's account API.

## Prize records

Fields persisted for an opening result (the full legacy field set, carried over by the open-source edition):

- Asset: `assetId` / `classId` / `instanceId`
- Metadata: market hash name, quality (seven rarity grades), appearance (wear tier), `floatValue`, rarity, icon
- Claim: inspection link, tradeable time
- Valuation: market price at sale time (from market sync)
- Links: order number, account, opening time, source (self-open / proxy-open / one-click)

## Item claim

1. The user configures Steam trade links (multiple, one default), the platform validates them.
2. Claim preconditions are checked: all cases / keys in the order are used up, no trade cooldown.
3. A trade offer is sent (vapor trading action) and confirmation is awaited.
4. Once the offer is confirmed the order completes; on failure it rolls back to "pending claim" with the error recorded and is retried by the compensation job.

## Roll rooms (weighted draw)

- Room state machine: creating → running → drawing → draw finished → claiming → finished, auto-rolling to the next round.
- Participation rules: claim on login, per-order participation, free-plan orders, invite codes (fixed / random), free order when a specific rarity is drawn, and so on; rules can be combined in configuration.
- Draw: weighted AliasMethod draw over the prizes; prizes come from inventory items (shipped by trade) or from points.
- Timed job: expiry draw checks.

## Concurrency and consistency notes

- Seat reservation / inventory locking: optimistic lock on the batch row plus conditional seat-status updates; failure means sold out.
- Payment callback idempotency: conditional order-status update; a duplicate callback simply returns success.
- Opening result idempotency: dedup by task / event ID, and a unique constraint on the prize record (order number + assetId).
- Trade status: the Steam side is the source of truth; local status may roll back but must never skip a step.
