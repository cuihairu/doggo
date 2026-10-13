# 漏斗数据分析模块设计

2026-10-12 立项,先设计后实现,本文是唯一设计输入。范围:页面浏览 → 加购(意图)→ 下单 → 支付的四步漏斗,含事件埋点、上报接口、服务端权威打点、每日聚合与后台漏斗页。

## 1. 背景与现状

doggo 已有的统计设施只回答"卖了多少",回答不了"用户在哪一步走的":

| 既有设施 | 位置 | 能回答 | 不能回答 |
| --- | --- | --- | --- |
| UserVisitLog | `server/src/Doggo.Api/VisitLogMiddleware.cs` | 全站服务端 PV/UV(每请求一行,finally 写入,失败只记日志) | 客户端行为(点击、意图),无端/渠道维度 |
| `/api/admin/stats` 系列 | `server/src/Doggo.Api/Endpoints/AdminEndpoints.cs` | 概览计数、14 日走势、商品排行 | 每步转化与流失 |
| UserOrder / PaymentRecord | `server/src/Doggo.Domain/Orders/` | 订单与支付结果(权威) | 下单前的流失 |

四端客户端:web(React + Vite)、小程序(Taro)、手机端(Taro RN)、admin(React 后台)。miniapp/mobile 共用 `@doggo/shared`(`shared/src/client.ts`,Taro request 封装 + kvGet/kvSet 本地存储),web 有独立的 `web/src/api/client.ts`(fetch 封装)。

## 2. 目标与非目标

### 目标

1. 定义并落地四步转化漏斗,每步可看 UV、转化率、流失率。
2. 支持 web / miniapp / mobile 分端、按渠道(channel)、按日期分组查看。
3. 客户端事件统一上报通道,支持弱网离线补报与服务端幂等去重。
4. 下单、支付两步用服务端权威事件,不依赖客户端回报。
5. 后台新增漏斗页,与现有 stats 路由区、参数风格一致。
6. 明确与 UserVisitLog、NewsItem.ViewCount、MarketPriceSnapshot 的同源口径。

### 非目标

- 不做实时大屏、不做秒级/小时级实时统计(只做 T-1 日聚合,见 §10 待拍板项)。
- 不引入第三方统计 SDK(自建事件通道,VisitorKey 等口径才能与既有数据对齐)。
- 不做 A/B 实验平台,不做分群(Pachel 用户分群)能力。
- 不做事件回放/明细级自助查询(明细仅用于排查,不建后台查询界面)。
- admin 端不在购买漏斗统计范围内(Client 枚举保留 admin 值,当前不上报)。

## 3. 漏斗定义与 doggo 现实映射

### 3.1 「加购」映射

doggo 没有购物车。现实购买路径是:商品详情页 → 选套餐(CasePlan)/数量档位(QuantityOption)/数量 → 直接调 `POST /api/orders` → 调起支付。没有"加入购物车再结算"的中间态。

建议把「加购」映射为**下单意图**:商品详情页上用户已选定 planId / optionId / quantity 四元组并点击"立即购买"(进入下单确认前)。该动作说明用户完成了商品决策,此后的放弃(调起支付失败、价格劝退、网络失败)都能被第 2→3 步的流失率捕捉。

### 3.2 四步漏斗

| 步 | 事件名 | 埋点来源 | 触发点 | 关键属性 |
| --- | --- | --- | --- | --- |
| 1 浏览商品 | `page_view_product` | 客户端 | 商品详情页渲染完成 | productId、来源位置(source) |
| 2 加购(意图) | `checkout_intent` | 客户端 | 详情页选定套餐/数量后点击"立即购买" | productId、planId、optionId、quantity |
| 3 下单 | `order_created` | **服务端权威** | `OrderService.CreateOrderAsync` 保存订单成功 | orderId、productId、totalPriceCents、entry |
| 4 支付 | `order_paid` | **服务端权威** | `ApplyPaymentCallbackAsync` 首次成功回调 | orderId、payType、amountCents |

### 3.3 为何第 4 步必须用服务端事件

- `POST /api/orders/{id}/pay` 只是**创建支付单**(`OrderService.CreatePaymentAsync` 向渠道要支付链接),点击支付按钮不等于支付成功。
- 支付结果的唯一权威来源是渠道回调 `POST /api/payments/callback` → `ApplyPaymentCallbackAsync`:状态 PendingPayment → PendingSettlement、写入 `PaidAtUtc`。回调可能发生在用户已关闭 App 之后,客户端根本无法回报。
- `PaymentRecord.IdempotencyKey` 已对回调去重,`ApplyPaymentCallbackAsync` 只在"首次成功"分支推进状态,在该分支打点天然不重不漏。

前两步只有客户端知道(服务端没有"看过商品页"这个事实),第 3 步起服务端订单数据本来就有,事件表只是把订单表转成统一事件口径,便于同一套聚合逻辑处理。

## 4. 事件埋点模型

### 4.1 实体草表

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

说明:

- 二者继承 `Entity`(Id + CreatedAtUtc/UpdatedAtUtc),沿用 `DoggoDbContext.ApplyAuditAndIds` 的约定:应用侧 GUID v7、`ValueGeneratedNever`、审计时间自动覆盖。AnalyticsEvent 的 `UpdatedAtUtc` 恒等于 `CreatedAtUtc`,冗余但保持基类约定,不特殊处理。
- 两者均不实现 `ISoftDeletable`:日志行不做软删。
- 注册:`DoggoDbContext` 增加 `DbSet<AnalyticsEvent>`、`DbSet<FunnelDailyStat>`;索引配置放 `ModelConfigurations.cs`。

### 4.2 索引与容量

| 实体 | 索引 | 用途 |
| --- | --- | --- |
| AnalyticsEvent | `IdempotencyKey` 唯一 | 幂等去重(离线补报、客户端重发) |
| AnalyticsEvent | `(EventName, OccurredAtUtc)` | 聚合 job 按事件名 + 日期扫描 |
| AnalyticsEvent | `(SessionId, OccurredAtUtc)` | 会话内事件排序、下钻 |
| AnalyticsEvent | `(VisitorKey, OccurredAtUtc)` | 匿名身份漏斗 |
| FunnelDailyStat | `(Date, Client, Channel)` 唯一 | 幂等 upsert,滚动重算覆盖 |

- `PropertiesJson` 显式 `HasColumnType("longtext")`(超过默认 varchar(255) 的列都走配置,先例:`NewsItemConfiguration`)。
- `DateOnly` 列沿用 `MarketPriceDailyConfiguration` 的显式 DATE 转换先例(MySql.EntityFrameworkCore 对 DateOnly 需要转换)。
- 事件表单量级估算:四端每天 PV 数万级,一行约 300 字节,年亿级以内,MySQL 单表可用。分区取舍:MySQL RANGE 分区要求所有唯一键含分区列,会迫使 `(Id, OccurredAtUtc)` 复合主键并破坏 IdempotencyKey 唯一索引,**不采用**;改为"聚合完成后的保留期归档",聚合一旦落 `FunnelDailyStat`,明细过期即可删(§10 #9 已实现:`AnalyticsRetentionWorker` 每日 02:00 UTC 运行,`FunnelDailyStat` 永久保留)。

### 4.3 会话定义

| 项 | 口径 |
| --- | --- |
| sessionId 生成 | 客户端首次可用时生成 UUID,存本地存储 |
| 客户端切会话 | 冷启动,或回到前台时距上次活动 > 30 分钟,换新 sessionId |
| 服务端 | **不切会话**,只记录客户端带来的 sessionId |
| 兜底 | 聚合时发现同 VisitorKey 事件缺 sessionId(理论不应发生,防脏数据),按 VisitorKey + 30 分钟不活跃切窗派生虚拟会话 |

取舍:客户端切会话实现简单,离线补报到达时会话早已关闭,若由服务端切会话,迟到事件会要求重算历史会话边界;30 分钟是不活跃切窗的行业常规值,对"逛商品 → 下单"的场景够用。会话在漏斗里只作关联辅助字段(见 §7.3,漏斗按身份而非按会话),会话切得不完美不影响漏斗结论。

### 4.4 事件字典

| 事件名 | 来源 | SchemaVersion | Properties 白名单 |
| --- | --- | --- | --- |
| `page_view_product` | 客户端 | 1 | productId、source |
| `checkout_intent` | 客户端 | 1 | productId、planId、optionId、quantity |
| `order_created` | 服务端 | 1 | orderId、productId、entry、serviceMode、fulfillmentType、totalPriceCents、payType |
| `order_paid` | 服务端 | 1 | orderId、payType、amountCents |

后续新增事件(如资讯阅读、行情订阅)只增字典不改表结构;属性变更升 SchemaVersion,旧版本事件永久可解析。

## 5. 上报接口与离线补报

### 5.1 接口契约

`POST /api/events`,匿名可用(事件路由区不加 `RequireAuthorization`);请求可带 `Authorization: Bearer`,能解析出 `sub` 则填 UserId,否则 null。VisitorKey 由服务端从连接信息生成(与 VisitLogMiddleware 同算法),**客户端不传**。

请求:

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

响应:

```json
200 OK
{ "accepted": 49, "duplicates": 1, "rejected": 0 }
```

| 项 | 约定 |
| --- | --- |
| 批量上限 | 单批 ≤ 50 条;单条 properties 序列化 ≤ 2 KB;请求体 ≤ 256 KB |
| 校验 | 事件名不在字典、SchemaVersion 不认识 → 该条记 rejected 丢弃;body 结构非法 → 400 ProblemDetails |
| 幂等 | IdempotencyKey 唯一索引,冲突不报错,计入 duplicates |
| 时间 | 服务端不信任但保留 occurredAtUtc;ReceivedAtUtc 服务端写 |
| 限频 | 同一 VisitorKey 固定窗口限频(建议 60 秒内 ≤ 30 次批量请求),超限 429;V1 单实例部署,内存计数即可,多实例后再切 Redis |
| 失败 | 上报失败静默:客户端事件回队重发;服务端异常 5xx,不阻断业务 |

C# 签名(路由并入 `Doggo.Api/Endpoints`,与 OrderEndpoints 同级新开 `EventEndpoints`):

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

### 5.2 客户端封装挂在哪一层

现状:miniapp/mobile 共用 `@doggo/shared`(Taro 封装),web 独立封装,四端不共享运行时代码只共享 REST 契约(`docs/design/overview.md` 的既定原则)。上报通道尊重这个结构:

| 端 | 封装位置 | 说明 |
| --- | --- | --- |
| miniapp / mobile | `shared/src/events.ts`(新文件,`index.ts` re-export) | 队列用既有 `kvSet/kvGet`,请求走既有 `api()`(`skipAuth: true` 容忍匿名) |
| web | `web/src/api/events.ts`(新文件) | fetch 版等价实现,localStorage 队列 |
| admin | 不上报 | Client=Admin 枚举保留 |

两端共享的是**契约与算法**(事件字典、字段名、会话切分参数、队列格式),不共享实现。

```ts
// shared/src/events.ts 草签
export function trackEvent(name: string, properties?: Record<string, unknown>): void
export function flushEvents(): Promise<void>   // 失败静默回队,永不抛错
```

调用点只加两处:`page_view_product` 挂商品详情页挂载完成;`checkout_intent` 挂"立即购买"点击。埋点失败不允许影响页面。

### 5.3 离线补报与乱序

| 项 | 口径 |
| --- | --- |
| 客户端队列 | 事件先写本地队列(kvSet/localStorage),攒批(≥ 10 条或距上次 ≥ 15 秒)后上报 |
| 重发 | 上报失败(网络/5xx/429)整批回队,指数退避,下次启动或触发时重试;IdempotencyKey 生成后不变,服务端去重兜底 |
| 队列上限 | 本地最多缓存 500 条,超出丢最旧(统计可容忍,内存不允许无界) |
| 可接受延迟 | 服务端接受 OccurredAtUtc 距今 ≤ 7 天的事件,更旧的记 rejected(补报价值已低) |
| 乱序 | 不要求批次内或跨批有序,一律按 OccurredAtUtc 归日;迟到事件落在已聚合日期时,靠 §7.4 的滚动重算校正 |
| 对账口径 | 漏斗统计以 OccurredAtUtc 为准;另提供按 ReceivedAtUtc 统计的日收量(见 §8 对账列),两者差值即迟到/补报规模 |

## 6. 服务端权威打点

### 6.1 打点位置

| 事件 | 写入点 | 事务策略 |
| --- | --- | --- |
| `order_created` | `OrderService.CreateOrderAsync`,在现有 `db.UserOrders.Add(order)` 之后、`SaveChangesAsync` 之前追加事件行 | **与订单同一 SaveChanges 批次(同事务)** |
| `order_paid` | `OrderService.ApplyPaymentCallbackAsync`,在状态推进 PendingPayment → PendingSettlement 的那个 SaveChangesAsync 批次内追加事件行 | 同上 |

建议同事务,不做 outbox:

- 订单写入本就强依赖这一个 MySQL 实例,事件行是同一连接上的多一行 INSERT,不新增可用性依赖,且零丢失、实现最短。
- outbox 需要 outbox 表 + 轮询投递,而本项目无 MQ,投递最终仍是写同一个库,等于把一行 INSERT 拆成两行加一个轮询器,收益为负。
- 若后续事件量级大到拖慢下单(现状不会),再退到"保存成功后 try/catch 独立写、失败仅记 warning 允许少量丢失"的方案,属于实现期可逆决策。

写事件通过统一入口,不散落:

```csharp
// server/src/Doggo.Infrastructure/Analytics/AnalyticsRecorder.cs
public interface IAnalyticsRecorder
{
    void EnqueueOrderCreated(UserOrder order);                       // 调用于业务 SaveChanges 前,同一批次落库
    void EnqueueOrderPaid(UserOrder order, PaymentRecord payment);   // 同上
    Task RecordAsync(AnalyticsEvent e, CancellationToken ct = default); // 供 /api/events 用,独立保存
}
```

### 6.2 上下文字段来源

| 字段 | order_created | order_paid |
| --- | --- | --- |
| UserId | 订单 UserId | 订单 UserId |
| SessionId / Client / Channel | 当前请求上下文(IHttpContextAccessor);客户端在下单请求头带上 X-Doggo-Session,无则空 | 回调请求来自支付服务商,无客户端上下文:SessionId/Client/Channel 记空,聚合时按 orderId 关联同单的 `order_created` 事件回填 |
| VisitorKey | IHttpContextAccessor 沿用中间件算法 | 空 |
| OccurredAtUtc | 订单 CreatedAtUtc | 订单 PaidAtUtc(比取当前时间准确,幂等重放也对) |

## 7. 聚合任务

### 7.1 职责

每日把 `AnalyticsEvents` 聚合成 `FunnelDailyStat`(日期 × 端 × 渠道 × 各步 UV)。漏斗页只读聚合表,不扫事件表。

### 7.2 任务形态

```csharp
// server/src/Doggo.Worker/Analytics/FunnelAggregateWorker.cs
public sealed partial class FunnelAggregateWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<FunnelAggregateWorker> logger) : BackgroundService
// 轮询模式沿用既有 Worker(OrderTimeoutWorker 等):Task.Delay 循环 + 每日 00:30 UTC 触发
// Program.cs:builder.Services.AddHostedService<FunnelAggregateWorker>();
```

### 7.3 漏斗口径

| 项 | 定义 |
| --- | --- |
| 身份 | UserId 优先,否则 VisitorKey;匿名 → 登录归一见 §10.3 |
| 漏斗实例 | 某身份的一个 `page_view_product` 开一个实例,按事件时间顺序依次消费后续步骤(同一事件不复用于多个实例) |
| 窗口 | 第 1 步起 7 天内完成的后续步才计入该实例 |
| 步骤 UV | 该实例走到第 k 步,则该实例计入第 k 步 |
| 归日 | 实例归属**第 1 步发生日**(UTC),行内四步 UV 同源,转化率同行可除 |
| 转化率 | 第 k+1 步 UV / 第 k 步 UV;流失率 = 1 − 转化率 |

归日取舍:按第 1 步归日后,"漏斗页某日支付 UV"与"订单报表当日支付数"不等价(前者含前几日进漏斗的人),这是刻意的——漏斗页要的是"同批人的转化",跨行相除才有意义;日历日口径的支付数在 `/api/admin/stats/trend` 已有,两者并存、用途不同。不采用"按会话内严格漏斗"口径:用户离开再回来(换会话)完成下单是常态,按会话会系统性低估后两步转化。

### 7.4 调度与重算

| 项 | 口径 |
| --- | --- |
| 每日运行 | 00:30 UTC 跑 T-1(昨日) |
| 滚动重算 | 同时重算 T-7 … T-2:迟到补报、order_paid 回填(§6.2)都在 7 天窗口内被吸收 |
| 幂等 | 输出按 `(Date, Client, Channel)` 唯一键 upsert,重算覆盖旧值,重复跑安全 |
| 迟到上限 | OccurredAtUtc 早于 T-7 的事件不再参与重算(§5.3 已在入口拒收,此处双保险) |
| 失败 | 单日失败记日志,下轮循环重试整窗(重算幂等,多跑无害) |

聚合实现层(内存扫描 + 分组,还是 SQL GROUP BY)留给实现期,本文只定输入输出与口径。

## 8. 后台漏斗页

### 8.1 接口

并入 AdminEndpoints 的 stats 路由区(`admin.MapGroup("/api/admin")`,Admin 策略),参数风格对齐既有端点:`[FromQuery]` 默认值 + `Math.Clamp` 限幅;日期用 `yyyy-MM-dd`(与 trend 的 `date` 输出一致)。

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

响应草图(`groupBy=client` 时):

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

- `groups`:区间汇总,每步 UV 与步间转化率/流失率,漏斗条形图与步骤明细表的数据源。
- `rows`:day 粒度时逐日逐组明细(week 粒度按 ISO 周聚合),按日分组表数据源。
- `receivedToday`:当日按 ReceivedAtUtc 收到的事件量,对账列(§5.3),顺手返回。

### 8.2 页面布局

后台新增独立漏斗页(菜单"数据分析 → 转化漏斗"),布局自上而下:

| 区块 | 内容 |
| --- | --- |
| 筛选行 | 日期区间(默认近 14 天,最远 T-1)、端下拉(全部/web/miniapp/mobile)、渠道下拉、分组维度(none/client/channel)、粒度(day/week) |
| 漏斗条形图 | 当前分组下各步横向条形,条长 = UV,条尾标注 UV 值与步间转化率;四步固定顺序,不需要图形库,矩形 div 即可(与 Dashboard 手写 SVG 折线图同一取向) |
| 步骤明细表 | 行 = 步骤,列 = 步骤名、UV、转化率、流失率 |
| 按日分组表 | 行 = 日期 × 分组键,列 = 四步 UV,支持按日期排序 |

数据只来自 `GET /api/admin/funnel`,页面不直接扫明细。本章作为 admin 原型 `admin/funnel.html` 的实现依据:原型为独立静态 HTML(同既有原型做法),先用固定 mock 数据按此布局出页面,接口就绪后换数据源再并入 `admin/src/pages/`。

## 9. 同源口径

### 9.1 与既有数据的关系

| 数据源 | 视角 | 时间口径 | 身份口径 | 职责 | 与 AnalyticsEvent 关系 |
| --- | --- | --- | --- | --- | --- |
| UserVisitLog | 服务端全部请求(含 API) | `Date`(DateOnly,UTC 日) | UserId? / VisitorKey | 全站 PV/UV 大盘 | **不被替代**,见 9.2 |
| AnalyticsEvent | 客户端行为流 + 服务端关键动作 | OccurredAtUtc(UTC) | UserId? / VisitorKey / SessionId | 漏斗与转化分析 | 主体 |
| NewsItem.ViewCount | 条目维度的累计阅读数 | 无时间维度(累加值) | 无 | 内容热度排行 | 互补:计数器答"哪篇热",事件流答"谁在看、看完去哪";若将来资讯漏斗立项,补 `page_view_news` 事件,ViewCount 保持不动 |
| MarketPriceSnapshot / MarketPriceDaily | 行情时序(与用户行为无关) | CapturedAtUtc / Date(UTC 日) | 无 | 价格趋势 | 无关;仅共享"UTC 日对齐"约定 |

### 9.2 事件表不替代 UserVisitLog

| 维度 | UserVisitLog | AnalyticsEvent |
| --- | --- | --- |
| 采集点 | 服务端中间件,每个请求一行 | 客户端主动 + 服务端两处权威打点 |
| 覆盖 | 全部路径(含未埋点的页面与 API) | 字典内事件 |
| 属性 | 只有 Path | 有会话、渠道、业务属性 |
| 量级 | 每请求一行,不可全量长期留 | 白名单事件,可控 |
| 用途 | PV/UV 大盘、路径热度 | 漏斗、转化、流失 |

漏斗页不读 UserVisitLog:它的 Path 视角推不出"选了套餐点购买"这类意图,也无端/渠道;反过来全站 PV 大盘也没必要迁到事件表(每页都埋点成本高、丢失率高于服务端中间件)。

### 9.3 统一聚合分析接口约定

后续所有聚合分析接口(含本次 funnel,及将来的资讯/行情分析接口)遵循:

1. 时间:一律 UTC 日,参数与输出 `yyyy-MM-dd`(DateOnly);
2. 端:`AnalyticsClient` 枚举字符串(web/miniapp/mobile/admin),可全选;
3. 身份:UserId 优先、否则 VisitorKey,与既有 UserVisitLog 的 VisitorKey 同一算法;
4. 参数风格:`[FromQuery]` + 默认值 + `Math.Clamp` 限幅(如 days 限 1–90);
5. 返回:匿名对象、camelCase,与 stats 区一致。

UserVisitLog 目前没有 Client 列,分端 PV 口径暂时缺位;建议后续补列(§10 待拍板),补列前漏斗页的分端数据只来自事件表。

## 10. 隐私与合规

### 10.1 Properties 不收 PII

- 属性按 §4.4 白名单收:服务端逐条校验,未列入白名单的键丢弃,超长值拒收该条(计 rejected)。
- 明确禁止:手机号、邮箱、昵称、UserAgent 明文、IP、精确地理位置、Steam 个人资料。客户端封装层(`shared/src/events.ts` / `web/src/api/events.ts`)做同样的白名单前置过滤,双道防线。
- Path 只记页面路径,query 中的用户输入(搜索词等)剥除。

### 10.2 VisitorKey 沿用脱敏方案

- 事件表 VisitorKey 与 UserVisitLog 同源:`SHA256(ip|userAgent)` 取前 32 hex(VisitLogMiddleware 既有实现)。建议把该算法抽成公共静态类供两处调用,**不改变**现有中间件行为;原始 IP、UA 不落事件表。

### 10.3 匿名与登录身份归一

| 场景 | 归一规则 |
| --- | --- |
| 未登录 | 身份 = VisitorKey |
| 已登录 | 身份 = UserId(VisitorKey 仍记录,用于与匿名期衔接) |
| 登录前后的连续行为 | 会话级回填:同一 SessionId 内若出现任一带 UserId 的事件,聚合时该会话的全部匿名事件归并到该 UserId |
| 跨会话 | 不回填。同一 VisitorKey 的两次独立会话之间不做身份拼接,避免共用设备的多人被并成一人 |

回填窗口 = 会话生命周期(30 分钟活跃 + 离线残留),而不是固定时长:窗口随 sessionId 天然闭合,实现和解释都最简单。登录动作本身**不**产生补报(不回改已入库事件的 UserId),归一只发生在聚合读取时,明细表保持原始事实。

## 11. 实施切分(建议)

| 阶段 | 内容 | 依赖 |
| --- | --- | --- |
| 1 | 实体 + DbContext 注册 + `/api/events` + AnalyticsRecorder + 服务端两处权威打点 | 无 |
| 2 | `shared/src/events.ts` 与 `web/src/api/events.ts` 封装 + 商品详情页两处埋点 + 下单请求带 X-Doggo-Session | 阶段 1 |
| 3 | FunnelAggregateWorker 每日聚合 + 滚动重算 | 阶段 1 |
| 4 | `GET /api/admin/funnel` + admin 漏斗页(原型 `admin/funnel.html` 先行) | 阶段 3 |

## 12. 待拍板项

| # | 事项 | 本文建议 | 待定问题 |
| --- | --- | --- | --- |
| 1 | 「加购」映射 | checkout_intent = 详情页选完套餐/数量点"立即购买" | 映射与触发点是否认可 |
| 2 | 会话切分 | 客户端生成 sessionId,30 分钟不活跃切会话;服务端不切,仅聚合兜底 | 参数 30 分钟是否认可;是否需要服务端切会话 |
| 3 | 匿名事件鉴权 | 免登录,靠限频(60 秒 30 批)+ 批量/体积上限 + 属性白名单 | 是否接受免鉴权;限频阈值 |
| 4 | 漏斗归日口径 | 按第 1 步发生日归行,行内转化率同源自洽 | 是否认可"漏斗页支付数 ≠ 当日订单报表支付数" |
| 5 | 聚合物化粒度 | (日期 × 端 × 渠道) | 是否需要商品维度(第 2/3 步带 productId,可加);是否需要周粒度物化(现按周即时从日行聚合) |
| 6 | Channel 取值来源 | 客户端启动参数/落地页 URL 参数,缺省 organic;服务端不猜 | 渠道命名清单与运营链接约定由谁定 |
| 7 | 当日数据可见性 | 只看 T-1 聚合;当日仅给 receivedToday 对账数 | 是否需要 admin 手动触发"当日即时查询"(直接扫事件表,慢但新鲜) |
| 8 | UserVisitLog 补 Client 列 | 建议补,对齐分端 PV 口径 | 是否本轮一起做,还是后续独立改 |
| 9 | 事件明细保留期 | 聚合落表后明细保留 90 天,过期删除 | 保留期与是否归档(归档到哪) |
| 10 | 迟到事件窗口 | 接受 7 天,重算 T-7…T-1 | 7 天是否合适 |

实现状态（2026-10-13，提交 69da251 服务端 + 1b47726 客户端/后台）：1-7、9、10 已按本文建议实现；仅 8（UserVisitLog 补 Client 列）未做，仍为开放项。9 事件明细保留期清理：`AnalyticsRetentionWorker` 每日 02:00 UTC 运行（在 00:30 聚合之后），删除 `OccurredAtUtc` 与 `ReceivedAtUtc` 均早于 cutoff 的明细，cutoff 取 `Analytics:EventRetentionDays`（默认 90 天）；只按单时间戳判旧会误删迟到事件，故取双条件交集；`FunnelDailyStat` 不参与保留期。实现期两处与草表不同：order_created 的 OccurredAtUtc 用入队时 UtcNow（审计管线在 SaveChanges 时才盖 CreatedAtUtc，同一时刻）；漏斗实例按身份（UserId 否则 VisitorKey）而非会话匹配（服务端 order_paid 无会话上下文，§7.3 本就以身份为口径）。
