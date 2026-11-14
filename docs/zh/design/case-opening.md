# 开箱系统设计


## 基本立场

- **平台不控制开箱概率。** 开箱在真实 CS2 游戏内发生,掉落由 Valve 服务器决定。Doggo 的职责是:库存管理、流程状态机、结果记录、饰品交付。
- 需要概率算法的玩法只有 roll 房抽奖,使用权重 + 别名抽样法(AliasMethod),权重配置可审计。
- 展示用的"箱子内容物"配置(品质/图片/参考价)只用于资料展示,不参与掉落判定。

## 三种开箱模式

| 模式 | 流程 | 执行者 |
| --- | --- | --- |
| 自开 | **开源版未实现** —— 以下流程沿用原平台口径保留:凭据 + Steam Guard 交付 → 用户在游戏内开箱 → 平台拉快照与订单关联 → 用户申请取回。`ServiceMode.SelfOpen` 与 `ProductStatus.SelfOpenOnly` 存在,但代码里没有凭据交付、guard 会话与库存快照回填路径 | 用户 |
| 批次代开 | 账号上的一串箱子按座位售卖 → 付款占座 → 提交开箱 → vapor 只开该座位(每座位一个 `case_open` 任务,携带批次 `VaporCaseId` 与该座位 `crateAssetId` / `keyAssetId`) → 结果 Webhook 生成奖品 | vapor 代理 |
| 一键开箱 | 开掉账号下全部箱子 —— **开源版不支持**:`open_all_crate` 在 vapor 侧无对应能力,本仓库也无代码路径 | vapor 代理 |

## 与 vapor 的集成

原平台通过 Redis Stream 下发指令(`open_crate` / `open_all_crate` / `inventory` / `inventory_history` / `trading` 等),由外部机器人进程消费。开源版中这一层由 vapor 承担:

| 原指令 | vapor 能力 |
| --- | --- |
| open_crate / open_all_crate | CS:GO 插件的 `case_open` action;真实开箱在 vapor 侧受 `open.enabled` 门禁,请求携带 `caseId`(批次上的 VaporCaseId)与该座位的 `crateAssetId` / `keyAssetId` |
| inventory / inventory_history | 库存 action 与变更事件 |
| trading | 交易报价 action(`send_trade_offer`,资产条目键为 `asset_id` / `app_id`) |
| password_auth / enable_guard | 凭据与 SteamGuard 会话管理 |

Doggo 侧集成方式:通过 vapor REST API 发起动作,通过 Webhook 订阅结果事件(本仓库没有 SSE 通道);按订单号关联回本地奖品记录。2026-10-14 核对 vapor 源码:其通知目前只有 `job` / `session` / `auth` 三类,尚无开箱域事件;`case_opening` Webhook 事件类别(`opening_result` 及 `stock_fetch_started` / `stock_ready` / `trade_started` / `trade_completed` 链)为 doggo 侧约定,由 `CaseOpeningWebhookHandler` 处理;早期的客户端座位结果回填接口已在 Webhook 接管这些状态迁移后移除,座位结果只经 Webhook 到达。

## 状态机

### 订单

```
待支付 → 待结算 → 待取回 → 取回中 → 已完成
   │                    
   └→ 已取消(15 分钟未支付,默认 900 秒,可配置)
   └→ 已退款
```

- 待结算:已支付,账号/座位已交付或锁定。
- 待取回:箱子已开,饰品在机器人库存中。
- 取回中:交易报价已发起,未确认。
- 补偿任务:取回检查(30 秒粒度,可配置)、未支付取消。

### 库存座位(批次代开)

```
售卖中 → 锁定 → 已付款 → 开箱中 → 已开箱 → 获取库存 → 完成库存 → 交易中 → 完成
```

座位记录:批次 lot、序号 seat、箱子 assetId、钥匙 assetId、奖品 assetId、historyId 游标;批次用乐观锁控制并发售卖。

### 机器人账号(在 vapor 侧)

备货 → 盘点 → 审核 → 上架 → 锁定 → 已售 → 已取回,旁路:可复用 / 异常 / 逾期。Doggo 不直接管理账号凭据;本仓库的 vapor 客户端只会派发任务、回读任务与拉取行情,账号阶段列还没有对应的查询接口。

## 奖品记录

开箱结果落库字段(原平台字段全集,开源版沿用):

- 资产:`assetId` / `classId` / `instanceId`
- 资料:市场名、品质(七档稀有度)、外观(磨损档)、`floatValue`、稀有度、图标
- 取回:检查链接、可交易时间
- 估值:成交时市价(来自行情同步)
- 关联:订单号、账号、开箱时间、`PrizeSource`(`SelfOpen` / `GroupOpen` / `CloudOpen` / `Roll`;roll 房开奖复用同一奖品记录,来源记 `Roll`)

## 饰品取回

1. 用户配置 Steam 交易链接(多条,可设默认),平台校验有效性;
2. `SubmitRetrievalAsync` 校验前置条件:订单仍为 `PendingSettlement` / `PendingRetrieval`、交易链接属于该用户且未失效、vapor 已配置、订单存在可发送的奖品;
   入口:`POST /api/orders/{id}/retrieval`(body `{"tradeUrlId": …}`)发起并置为 `Retrieving`、`POST /api/orders/{id}/retrieval/fail` 带原因回退;订单落定由 `order_retrieval` / `trade_completed` Webhook 驱动,不接受客户端提供的 offer id;
3. 发起交易报价(vapor trading action),等待确认;
4. 报价确认后订单完成;失败回退"待取回"并记录错误,由补偿任务重试。

## roll 房(权重抽奖)

- 房间状态机:创建中 → 进行中 → 开奖中 → 开奖结束 → 领奖 → 结束,自动滚动下一期。
- 参与规则:登录领取、每单参与、免单套餐、邀请码(固定/随机)、出特定稀有度免单等,规则可组合配置。
- 开奖:按奖品权重 AliasMethod 抽取;奖品来源为库存饰品(走交易发货)或金币。
- 定时任务:到期开奖检查。

## 并发与一致性要点

- 占座/锁库存:批次行乐观锁 + 座位状态条件更新,失败即售罄。
- 支付回调幂等:订单状态条件更新,重复回调直接返回成功。
- 开箱结果幂等:按任务/事件 ID 去重,奖品记录唯一约束(订单号 + assetId)。
- 交易状态:以 Steam 侧事实为准,本地状态允许回退,禁止跳步。
